using System.Text;
using System.Text.Json;
namespace EZapi;

/// <summary>只读观察结果。Readable只证明文本可解析，不证明存档结构、物品目录或持久事务完成。</summary>
public enum SaveReadStatus { Readable, Missing, TooLarge, ChangedDuringRead, Malformed, Unavailable, Invalid }

/// <summary>使用using释放解析内存；Root及其子元素的寿命跟随本结果，跨寿命保留须Clone。</summary>
public sealed class SaveReadResult:IDisposable
{
    private JsonDocument? _document;
    internal SaveReadResult(SaveReadStatus status,int bytes,string? reason=null,JsonDocument? document=null)
        =>(Status,ByteCount,Reason,_document)=(status,bytes,reason,document);
    public SaveReadStatus Status{get;}
    /// <summary>观察到的原始字节数，包含UTF-8 BOM；未读到数据时为0。</summary>
    public int ByteCount{get;}
    public string? Reason{get;}
    public JsonElement Root=>(_document??throw new InvalidOperationException("EZAPI_SAVE_READ_ROOT: no readable document or result disposed")).RootElement;
    public void Dispose()=>Interlocked.Exchange(ref _document,null)?.Dispose();
}

/// <summary>
/// 无游戏依赖的只读存档工具；可在游戏外或后台使用。不调用ES3、不反序列化游戏对象、不写文件。
/// 支持当前文本ES3中的整数对象键、字符串原始控制字符和无效反斜杠转义；其余JSON语法仍严格检查。
/// </summary>
public static class SaveReadbackApi
{
    public const int DefaultMaxBytes=16*1024*1024;
    public const int DefaultMaxDepth=256;
    private static readonly UTF8Encoding Utf8=new(false,true);

    /// <summary>
    /// 独占写入共享权的只读文件观察，前后核对长度/写入与创建时间。正在保存或被锁时返回Unavailable，
    /// 不重试、不阻止后续保存。读取后再发生的变化不在此快照保证内。
    /// </summary>
    public static SaveReadResult ReadFile(string path,int maxBytes=DefaultMaxBytes,int maxDepth=DefaultMaxDepth)
    {
        if(string.IsNullOrWhiteSpace(path)||maxBytes<=0||maxDepth<=0)return Invalid("path and positive read limits required");
        try {
            var info=new FileInfo(path);info.Refresh();
            if(!info.Exists)return new(SaveReadStatus.Missing,0);
            var length=info.Length;var written=info.LastWriteTimeUtc;var created=info.CreationTimeUtc;
            if(length>maxBytes)return new(SaveReadStatus.TooLarge,0,"EZAPI_SAVE_READ_LIMIT: file exceeds maxBytes");
            // FileShare.Read allows readers, but denies an overlapping writer/delete on Windows.
            using var input=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,FileShare.Read);
            bool Same(){info.Refresh();return info.Exists&&info.Length==length&&input.Length==length&&info.LastWriteTimeUtc==written&&info.CreationTimeUtc==created;}
            if(!Same())return new(SaveReadStatus.ChangedDuringRead,0,"EZAPI_SAVE_READ_CHANGED: file changed before read");
            return ReadStable(input,(int)length,Same,maxBytes,maxDepth);
        }catch(FileNotFoundException){return new(SaveReadStatus.Missing,0);}
        catch(DirectoryNotFoundException){return new(SaveReadStatus.Missing,0);}
        catch(Exception e)when(e is ArgumentException or NotSupportedException){return Invalid(e.Message);}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException){return new(SaveReadStatus.Unavailable,0,"EZAPI_SAVE_READ_IO: "+e.Message);}
    }
    // Also used by deterministic short-read/growth/change tests; not a public arbitrary stream API.
    internal static SaveReadResult ReadStable(Stream input,int length,Func<bool> unchanged,int maxBytes,int maxDepth)
    {
        var buffer=new byte[length];var count=0;
        while(count<length) {
            var n=input.Read(buffer,count,length-count);
            if(n==0)return new(SaveReadStatus.ChangedDuringRead,count,"EZAPI_SAVE_READ_CHANGED: truncated during read");
            count+=n;
        }
        if(input.ReadByte()!=-1||!unchanged())return new(SaveReadStatus.ChangedDuringRead,count,"EZAPI_SAVE_READ_CHANGED: size or metadata changed");
        var result=ReadOwnedBytes(buffer,maxBytes,maxDepth);
        if(unchanged())return result;
        result.Dispose();return new(SaveReadStatus.ChangedDuringRead,count,"EZAPI_SAVE_READ_CHANGED: file changed during parsing");
    }

    /// <summary>复制调用者字节，再严格解码UTF-8。BOM可选。原字节超限在复制/解析前拒绝。</summary>
    public static SaveReadResult ReadBytes(ReadOnlyMemory<byte> bytes,int maxBytes=DefaultMaxBytes,int maxDepth=DefaultMaxDepth)
    {
        if(maxBytes<=0||maxDepth<=0)return Invalid("positive read limits required");
        if(bytes.Length>maxBytes)return new(SaveReadStatus.TooLarge,0,"EZAPI_SAVE_READ_LIMIT: bytes exceed maxBytes");
        return ReadOwnedBytes(bytes.ToArray(),maxBytes,maxDepth);
    }
    private static SaveReadResult ReadOwnedBytes(byte[] bytes,int maxBytes,int maxDepth)
    {
        if(bytes.Length>maxBytes)return new(SaveReadStatus.TooLarge,0,"EZAPI_SAVE_READ_LIMIT: bytes exceed maxBytes");
        try {
            var offset=bytes.AsSpan().StartsWith(new byte[]{0xef,0xbb,0xbf})?3:0;
            var text=Utf8.GetString(bytes,offset,bytes.Length-offset);
            return new(SaveReadStatus.Readable,bytes.Length,document:Parse(text,maxDepth));
        }catch(Exception e)when(e is JsonException or DecoderFallbackException or EncoderFallbackException){return new(SaveReadStatus.Malformed,bytes.Length,"EZAPI_SAVE_READ_FORMAT: "+e.Message);}
    }
    private static SaveReadResult Invalid(string reason)=>new(SaveReadStatus.Invalid,0,"EZAPI_SAVE_READ_ARGUMENT: "+reason);

    /// <summary>低层抛异常入口，调用者负责Dispose。无字节上限；不可信文本应走有界ReadBytes。</summary>
    public static JsonDocument Parse(string source,int maxDepth=DefaultMaxDepth)
    {
        if(maxDepth<=0)throw new ArgumentOutOfRangeException(nameof(maxDepth));
        ArgumentNullException.ThrowIfNull(source);Utf8.GetByteCount(source);
        return JsonDocument.Parse(Normalize(source),new JsonDocumentOptions{MaxDepth=maxDepth});
    }

    /// <summary>
    /// 返回true表示parent是对象且此名称不重复；缺少时仍为true/present=false。
    /// 重复或非对象为false，不采用最后一个同名值。名称按解码后的Ordinal比较。
    /// </summary>
    public static bool TryGetUniqueProperty(JsonElement parent,string name,out JsonElement value,out bool present)
    {
        ArgumentNullException.ThrowIfNull(name);value=default;present=false;
        if(parent.ValueKind!=JsonValueKind.Object)return false;
        foreach(var p in parent.EnumerateObject())if(p.NameEquals(name)) {
            if(present){value=default;return false;}
            value=p.Value;present=true;
        }
        return true;
    }
    public static JsonElement RequireProperty(JsonElement parent,string name)
    {
        if(!TryGetUniqueProperty(parent,name,out var value,out var present)||!present)
            throw new InvalidDataException("EZAPI_SAVE_READ_PROPERTY: missing, duplicate or invalid property: "+name);
        return value;
    }

    /// <summary>
    /// 只规范化已知ES3文本扩展，不验证完整语法，不去重，不排序，不把数字值改成文本。
    /// 仅在对象期待属性名且后跟冒号时给整数键加引号；数组中的数字原样保留。
    /// </summary>
    public static string Normalize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output=new StringBuilder(source.Length);var containers=new List<(char Kind,bool Key)>();
        bool WantKey()=>containers.Count>0&&containers[^1].Kind=='{'&&containers[^1].Key;
        void Named(){if(WantKey())containers[^1]=('{',false);}
        for(int i=source.Length>0&&source[0]=='\ufeff'?1:0;i<source.Length;) {
            var c=source[i];
            if(c=='"') {
                Named();output.Append(c);i++;
                while(i<source.Length) {
                    c=source[i++];
                    if(c=='"'){output.Append(c);break;}
                    if(c=='\\') {
                        var valid=i<source.Length&&("\"\\/bfnrt".Contains(source[i])||source[i]=='u'&&i+4<source.Length&&
                            IsHex(source[i+1])&&IsHex(source[i+2])&&IsHex(source[i+3])&&IsHex(source[i+4]));
                        if(valid){output.Append(c).Append(source[i++]);continue;}
                        output.Append("\\\\");continue;
                    }
                    if(c<0x20)output.Append("\\u").Append(((int)c).ToString("x4",System.Globalization.CultureInfo.InvariantCulture));
                    else output.Append(c);
                }
                continue;
            }
            if(WantKey()&&(c=='-'||c is >= '0' and <= '9')) {
                var end=i+(c=='-'?1:0);var digits=end;
                while(end<source.Length&&source[end] is >= '0' and <= '9')end++;
                var colon=end;while(colon<source.Length&&source[colon] is ' ' or '\r' or '\n' or '\t')colon++;
                if(end>digits&&colon<source.Length&&source[colon]==':') {
                    output.Append('"').Append(source,i,end-i).Append('"');i=end;Named();continue;
                }
            }
            if(c is '{' or '['){Named();containers.Add((c,c=='{'));}
            else if(c is '}' or ']'){if(containers.Count>0)containers.RemoveAt(containers.Count-1);}
            else if(c==','&&containers.Count>0&&containers[^1].Kind=='{')containers[^1]=('{',true);
            output.Append(c);i++;
        }
        return output.ToString();
    }
    private static bool IsHex(char c)=>c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';
}

using EZapi;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
var cases=new List<object>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(new{name,passed=true});}
void Reject<T>(Action a,string name)where T:Exception {try{a();}catch(T){Check(true,name);return;}throw new Exception("accepted: "+name);}
SaveReadResult Read(string s,int maxBytes=SaveReadbackApi.DefaultMaxBytes,int maxDepth=SaveReadbackApi.DefaultMaxDepth)=>SaveReadbackApi.ReadBytes(Encoding.UTF8.GetBytes(s),maxBytes,maxDepth);
string Value(string s,string name="text"){using var d=SaveReadbackApi.Parse(s);return SaveReadbackApi.RequireProperty(d.RootElement,name).GetString()!;}
foreach(var text in new[]{"{}","[]","null","true","false","0","-1","2.3e4","\"你好\""}) {
 using var result=Read(text);Check(result.Status==SaveReadStatus.Readable&&result.ByteCount==Encoding.UTF8.GetByteCount(text),"JSON root: "+text);
}
foreach(var text in new[]{""," ","{","{\"x\":1,}","[1,]","{x:1}","{+1:2}","{/*comment*/}","[]x","{1.1:2}","{--1:2}","{\"x\":NaN}"}) {
 using var result=Read(text);Check(result.Status==SaveReadStatus.Malformed,"malformed: "+text);
 Reject<InvalidOperationException>(()=>{var root=result.Root;},"unreadable Root: "+text);
}
using(var bom=SaveReadbackApi.ReadBytes(new byte[]{0xef,0xbb,0xbf,0x7b,0x7d})){Check(bom.Status==SaveReadStatus.Readable&&bom.ByteCount==5,"UTF8 BOM accepted, raw size counted");}
foreach(var bytes in new[]{new byte[]{0xff},new byte[]{0xc0,0xaf},new byte[]{0xe2,0x28,0xa1},new byte[]{0xed,0xa0,0x80},new byte[]{0xff,0xfe,0x7b,0},new byte[]{0xef,0xbb}}) {
 using var result=SaveReadbackApi.ReadBytes(bytes);Check(result.Status==SaveReadStatus.Malformed,"strict invalid UTF8: "+Convert.ToHexString(bytes));
}
Check(Value("{\"text\":\"a\nb\r\t\0\"}")=="a\nb\r\t\0","raw controls escaped without changing data");
foreach(var n in Enumerable.Range(0,32))Check(Value("{\"text\":\""+(char)n+"\"}")==((char)n).ToString(),"all controls: "+n);
foreach(var escape in new[]{"\\q","\\uZZZZ","\\u12","\\v","C:\\path\\item"})Check(Value("{\"text\":\""+escape+"\"}")==escape,"invalid escape preserved literal: "+escape);
Check(Value("{\"text\":\"\\u4f60\\u597d\"}")=="你好","valid unicode escape preserved");
Check(Value("{\"text\":\"\\\\n\\\"\\/\"}")=="\\n\"/","valid slash quote and escaped-backslash preserved");
Check(Value("{\"text\":\"{1:2},12:3\"}")=="{1:2},12:3","numeric-looking string untouched");
using(var d=SaveReadbackApi.Parse("{12:1,-7:2,001:3,\"nested\":{42:4},\"array\":[1,2,{5:6}]}")) {
 var root=d.RootElement;
 Check(root.GetProperty("12").GetInt32()==1&&root.GetProperty("-7").GetInt32()==2&&root.GetProperty("001").GetInt32()==3,"integer property text preserved including leading zeros");
 Check(root.GetProperty("nested").GetProperty("42").GetInt32()==4,"nested integer property");
 Check(root.GetProperty("array")[0].ValueKind==JsonValueKind.Number&&root.GetProperty("array")[2].GetProperty("5").GetInt32()==6,"arrays preserve values and nested object keys");
}
foreach(var text in new[]{"{1 : 3}","{ -1\t:4}","\ufeff{0:0}","{\"a\":[{1:2},{2:3}]}"}) {
 var normal=SaveReadbackApi.Normalize(text);Check(SaveReadbackApi.Normalize(normal)==normal,"normalization idempotent: "+text);
 using var d=SaveReadbackApi.Parse(text);Check(d.RootElement.ValueKind==JsonValueKind.Object,"normalized parses: "+text);
}
using(var d=SaveReadbackApi.Parse("{\"x\":null,\"X\":2,\"dupe\":1,\"dupe\":2,\"escaped\":1,\"\\u0065scaped\":2,1:1,\"1\":2}")) {
 var root=d.RootElement;
 Check(SaveReadbackApi.TryGetUniqueProperty(root,"x",out var v,out var present)&&present&&v.ValueKind==JsonValueKind.Null,"present null distinguished from missing");
 Check(SaveReadbackApi.TryGetUniqueProperty(root,"absent",out v,out present)&&!present&&v.ValueKind==JsonValueKind.Undefined,"missing success with present false");
 Check(SaveReadbackApi.RequireProperty(root,"X").GetInt32()==2,"property names ordinal case-sensitive");
 foreach(var key in new[]{"dupe","escaped","1"}) {
  Check(!SaveReadbackApi.TryGetUniqueProperty(root,key,out v,out present)&&present&&v.ValueKind==JsonValueKind.Undefined,"duplicates rejected: "+key);
  Reject<InvalidDataException>(()=>SaveReadbackApi.RequireProperty(root,key),"require duplicate rejected: "+key);
 }
 Reject<InvalidDataException>(()=>SaveReadbackApi.RequireProperty(root,"absent"),"require missing rejected");
}
foreach(var text in new[]{"[]","null","1","true","\"x\""}) {
 using var d=SaveReadbackApi.Parse(text);Check(!SaveReadbackApi.TryGetUniqueProperty(d.RootElement,"x",out _,out var present)&&!present,"nonobject property: "+text);
}
Reject<ArgumentNullException>(()=>SaveReadbackApi.Normalize(null!),"normalize null argument");
Reject<ArgumentNullException>(()=>SaveReadbackApi.Parse(null!),"parse null argument");
Reject<ArgumentOutOfRangeException>(()=>SaveReadbackApi.Parse("{}",0),"parse zero depth");
Reject<ArgumentOutOfRangeException>(()=>SaveReadbackApi.Parse("{}",-1),"parse negative depth");
foreach(var (bytes,depth) in new[]{(0,1),(-1,1),(1,0),(1,-1)}) {
 using var result=Read("{}",bytes,depth);Check(result.Status==SaveReadStatus.Invalid,"bad limits: "+bytes+"/"+depth);
}
using(var limit=Read("{}",2))Check(limit.Status==SaveReadStatus.Readable,"exact byte limit allowed");
using(var limit=Read("{}",1))Check(limit.Status==SaveReadStatus.TooLarge,"over byte limit rejected");
using(var limit=Read("\"你\"",4))Check(limit.Status==SaveReadStatus.TooLarge,"byte limit UTF8 not char count");
using(var limit=Read("[[0]]",maxDepth:1))Check(limit.Status==SaveReadStatus.Malformed,"depth rejection");
using(var limit=Read("[[0]]",maxDepth:2))Check(limit.Status==SaveReadStatus.Readable,"exact depth allowed");
var mutable=Encoding.UTF8.GetBytes("{\"value\":1}");using(var result=SaveReadbackApi.ReadBytes(mutable)) {
 Array.Fill(mutable,(byte)'x');Check(result.Root.GetProperty("value").GetInt32()==1,"caller buffer mutation cannot corrupt document");
 var clone=result.Root.Clone();result.Dispose();result.Dispose();Check(clone.GetProperty("value").GetInt32()==1,"explicit clone survives disposal");
 Reject<InvalidOperationException>(()=>{var root=result.Root;},"Root rejects disposed result");
}
// Deterministic I/O boundaries link the real loop with controlled short-read streams; no fake filesystem claim.
using(var ms=new ChunkStream(Encoding.UTF8.GetBytes("{0:1}"),1))using(var result=SaveReadbackApi.ReadStable(ms,5,()=>true,50,10))Check(result.Status==SaveReadStatus.Readable,"short reads assemble complete input");
using(var ms=new MemoryStream(new byte[]{123}))using(var result=SaveReadbackApi.ReadStable(ms,2,()=>true,50,10))Check(result.Status==SaveReadStatus.ChangedDuringRead&&result.ByteCount==1,"truncation detected");
using(var ms=new MemoryStream(Encoding.UTF8.GetBytes("{}x")))using(var result=SaveReadbackApi.ReadStable(ms,2,()=>true,50,10))Check(result.Status==SaveReadStatus.ChangedDuringRead,"growth detected without unbounded read");
using(var ms=new MemoryStream(Encoding.UTF8.GetBytes("{}")))using(var result=SaveReadbackApi.ReadStable(ms,2,()=>false,50,10))Check(result.Status==SaveReadStatus.ChangedDuringRead,"metadata changed before parse");
var sameCalls=0;using(var ms=new MemoryStream(Encoding.UTF8.GetBytes("{}")))using(var result=SaveReadbackApi.ReadStable(ms,2,()=>++sameCalls==1,50,10))Check(result.Status==SaveReadStatus.ChangedDuringRead,"metadata changed during parse releases document");
var folder=Path.Combine(Path.GetTempPath(),"ezapi-save-readback-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
try {
 var p=Path.Combine(folder,"sample.es3");File.WriteAllText(p,"{17:{\"__type\":\"item\",\"value\":\"a\\q\"}}",new UTF8Encoding(false));
 var before=SHA256.HashData(File.ReadAllBytes(p));
 using(var result=SaveReadbackApi.ReadFile(p))Check(result.Status==SaveReadStatus.Readable&&result.Root.GetProperty("17").GetProperty("value").GetString()=="a\\q","real filesystem read ES3 text extensions");
 Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(p))),"real file bytes unchanged after reading");
 using(var result=SaveReadbackApi.ReadFile(p,1))Check(result.Status==SaveReadStatus.TooLarge,"real file over limit");
 using(var writer=new FileStream(p,FileMode.Open,FileAccess.ReadWrite,FileShare.None))using(var result=SaveReadbackApi.ReadFile(p))Check(result.Status==SaveReadStatus.Unavailable,"active writer lock returns unavailable");
 using(var result=SaveReadbackApi.ReadFile(p+".missing"))Check(result.Status==SaveReadStatus.Missing,"real missing file");
 using(var result=SaveReadbackApi.ReadFile("\0bad"))Check(result.Status==SaveReadStatus.Invalid,"invalid file path");
 using(var result=SaveReadbackApi.ReadFile(" "))Check(result.Status==SaveReadStatus.Invalid,"blank file path");
 using(var result=SaveReadbackApi.ReadFile(p,maxDepth:0))Check(result.Status==SaveReadStatus.Invalid,"invalid file depth");
 File.WriteAllBytes(p,Array.Empty<byte>());using(var result=SaveReadbackApi.ReadFile(p))Check(result.Status==SaveReadStatus.Malformed,"empty file");
}finally{foreach(var p in Directory.EnumerateFiles(folder))File.Delete(p);Directory.Delete(folder);}
// Real saved game input is read-only, with SHA before/after. No game runtime or Forge loaded.
var historical=new List<object>();
var paths=args.SelectMany(p=>Directory.Exists(p)?Directory.EnumerateFiles(p,"*.es3").OrderBy(p=>p,StringComparer.Ordinal).AsEnumerable():Enumerable.Repeat(p,1)).ToArray();
foreach(var path in paths) {
 var bytes=File.ReadAllBytes(path);var before=Convert.ToHexString(SHA256.HashData(bytes));
 using var result=SaveReadbackApi.ReadFile(path);
 Check(result.Status==SaveReadStatus.Readable,"actual save readable: "+Path.GetFileName(path));
 int? slot=null;string? run=null;
 if(Path.GetFileName(path).StartsWith("save_",StringComparison.Ordinal)) {
  var state=SaveReadbackApi.RequireProperty(SaveReadbackApi.RequireProperty(result.Root,"playerStore"),"value");
  slot=SaveReadbackApi.RequireProperty(state,"saveSlotId").GetInt32();run=SaveReadbackApi.RequireProperty(state,"runID").GetString();
  Check(slot>=0&&!string.IsNullOrWhiteSpace(run),"actual save identity: "+Path.GetFileName(path));
 }
 Check(before==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),"actual save SHA unchanged: "+Path.GetFileName(path));
 historical.Add(new{file=Path.GetFileName(path),slotId=slot,runId=run,sha256=before,bytes=bytes.Length});
}
Console.WriteLine(JsonSerializer.Serialize(new{scope="real linked SaveReadback source; real temp files and optional historical saves; controlled stream only for deterministic change detection; no game runtime needed",framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,passed=cases.Count,failed=0,cases,historical}));
sealed class ChunkStream:MemoryStream {
 private readonly int _chunk;
 public ChunkStream(byte[] b,int chunk):base(b)=>_chunk=chunk;
 public override int Read(byte[] buffer,int offset,int count)=>base.Read(buffer,offset,Math.Min(count,_chunk));
}

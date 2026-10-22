using System.Text.Json;
using System.Reflection;
namespace EZapi;

/// <summary>严格 JSON 解析。字段与 Schema 一致；未知字段、重复字段、null 均拒绝。</summary>
public static class JsonItems
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>读取整个文件并先校验。最多 2 MiB、500 件。失败不提交任何声明。</summary>
    public static IReadOnlyList<ItemDefinition> Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var data=new MemoryStream();
        var block=new byte[81920]; int read;
        while((read=json.Read(block,0,block.Length))>0)
        {
            if(data.Length+read>2*1024*1024)throw new InvalidDataException("EZAPI_JSON_LIMIT: max 2 MiB.");
            data.Write(block,0,read);
        }
        using var doc=JsonDocument.Parse(data.ToArray(),new JsonDocumentOptions {MaxDepth=16});
        if(doc.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("EZAPI_JSON_ROOT: expected an array.");
        if(doc.RootElement.GetArrayLength()>500)throw new InvalidDataException("EZAPI_JSON_LIMIT: max 500 items.");
        var result=new List<ItemDefinition>(); var ids=new HashSet<string>(StringComparer.Ordinal);
        int index=0;
        foreach(var row in doc.RootElement.EnumerateArray())
        {
            try {
                if(row.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("EZAPI_JSON_ITEM_TYPE: expected an object.");
                ValidateObject(row, typeof(ItemDefinition), "items["+index+"]");
                var item = JsonSerializer.Deserialize<ItemDefinition>(row.GetRawText(), Options)!;
                DefinitionRules.Validate(item);
                if(!ids.Add(item.Id))throw new InvalidDataException("EZAPI_ITEM_DUPLICATE: "+item.Id);
                result.Add(item);
            }
            catch(Exception e)when(e is ArgumentException or InvalidDataException or InvalidOperationException or FormatException or OverflowException or JsonException) {
                throw new InvalidDataException("items["+index+"]: "+e.Message,e);
            }
            index++;
        }
        return result.AsReadOnly();
    }

    /// <summary>主线程、目录初始化前调用。Parse 与整组预检成功后统一入队；检查每个结果。</summary>
    public static IReadOnlyList<ItemRegistrationResult> Register(Stream json)=>ItemRegistry.RegisterBatch(Parse(json));

    // 反射只用于离线/初始化的 DTO 校验；嵌套同样严格，绝不在 tooltip 或 Update 热路径反射。
    internal static void ValidateObject(JsonElement value, Type type, string path)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("EZAPI_JSON_TYPE: "+path+" expected object.");
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p=>p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>()?.Condition != System.Text.Json.Serialization.JsonIgnoreCondition.Always)
            .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in value.EnumerateObject())
        {
            if (!properties.TryGetValue(p.Name, out var property) || !keys.Add(p.Name))
                throw new InvalidDataException("EZAPI_JSON_FIELD: unknown or duplicate field '"+path+"."+p.Name+"'.");
            var fieldPath = path+"."+p.Name;
            if (p.Value.ValueKind == JsonValueKind.Null) throw new InvalidDataException("EZAPI_JSON_NULL: "+fieldPath);
            var t = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                if (p.Value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("EZAPI_JSON_TYPE: "+fieldPath+" expected array.");
                var elementType = t.GetGenericArguments()[0]; int i=0;
                foreach (var element in p.Value.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Null) throw new InvalidDataException("EZAPI_JSON_NULL: "+fieldPath+"["+i+"]");
                    if (elementType != typeof(string)) ValidateObject(element, elementType, fieldPath+"["+i+"]");
                    else if (element.ValueKind != JsonValueKind.String) throw new InvalidDataException("EZAPI_JSON_TYPE: "+fieldPath+" expected string entries.");
                    i++;
                }
            }
            else if (t.IsClass && t != typeof(string)) ValidateObject(p.Value, t, fieldPath);
        }
    }
}

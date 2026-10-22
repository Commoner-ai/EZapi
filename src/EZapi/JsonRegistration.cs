using System.Text.Json;
using EZapi.Registration;
namespace EZapi;

/// <summary>严格通用注册JSON，只解析与验证元数据；不自动调用游戏注册或工厂。</summary>
public static class JsonRegistration
{
    public static RegistrationBatch Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);using var bytes=new MemoryStream();var buffer=new byte[81920];int count;
        while((count=json.Read(buffer,0,buffer.Length))>0){if(bytes.Length+count>2*1024*1024)throw new InvalidDataException("EZAPI_REGISTRATION_LIMIT: JSON max2MiB.");bytes.Write(buffer,0,count);}
        using var document=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions{MaxDepth=12});
        var root=document.RootElement;Fields(root,"batch","ownerId","version","dependencies","definitions");
        var batch=new RegistrationBatch(Text(root,"ownerId"),Text(root,"version"));
        if(root.TryGetProperty("dependencies",out var dependencies)){
            Array(dependencies,"dependencies",64);
            foreach(var dependency in dependencies.EnumerateArray()){
                Fields(dependency,"dependency","ownerId","minimumVersion");
                batch.Require(Text(dependency,"ownerId"),dependency.TryGetProperty("minimumVersion",out var version)?String(version,"minimumVersion"):"0.0.0");
            }
        }
        if(root.TryGetProperty("definitions",out var definitions)){
            Array(definitions,"definitions",500);
            foreach(var definition in definitions.EnumerateArray()){
                Fields(definition,"definition","kind","localId","signature","requiredIds");
                var name=Text(definition,"kind");
                if(!Enum.GetNames(typeof(DefinitionKind)).Any(n=>string.Equals(n,name,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("EZAPI_REGISTRATION_KIND: known kind name required.");
                var required=new List<string>();
                if(definition.TryGetProperty("requiredIds",out var references)){Array(references,"requiredIds",64);foreach(var reference in references.EnumerateArray())required.Add(String(reference,"requiredIds entry"));}
                batch.Define(Enum.Parse<DefinitionKind>(name,true),Text(definition,"localId"),Text(definition,"signature"),required.ToArray());
            }
        }
        batch.Seal();return batch;
    }
    private static void Fields(JsonElement element,string path,params string[] allowed)
    {
        if(element.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("EZAPI_REGISTRATION_JSON: "+path+" expected object.");
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var field in element.EnumerateObject())if(!allowed.Contains(field.Name,StringComparer.Ordinal)||!seen.Add(field.Name)||field.Value.ValueKind==JsonValueKind.Null)
            throw new InvalidDataException("EZAPI_REGISTRATION_JSON: unknown/duplicate/null field "+path+"."+field.Name);
    }
    private static string Text(JsonElement row,string key)=>row.TryGetProperty(key,out var value)?String(value,key):throw new InvalidDataException("EZAPI_REGISTRATION_JSON: missing "+key);
    private static string String(JsonElement value,string key)=>value.ValueKind==JsonValueKind.String?value.GetString()!:throw new InvalidDataException("EZAPI_REGISTRATION_JSON: "+key+" expected string.");
    private static void Array(JsonElement value,string key,int limit){if(value.ValueKind!=JsonValueKind.Array||value.GetArrayLength()>limit)throw new InvalidDataException("EZAPI_REGISTRATION_LIMIT: "+key+" expected array, max"+limit);}
}

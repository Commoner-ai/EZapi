using System.Text;
using System.Text.Json;
namespace EZapi;

/// <summary>有版本、槽位/周目范围和完整图的持久端点记录。这里不持有任何IL2CPP包装对象。</summary>
internal sealed record InventoryEndpointSaved(string OwnerId,string EndpointId,string ItemId,int InstanceId,bool ChargingOnly,string Graph);
internal sealed record InventoryEndpointDocument(int Version,int SlotId,string RunId,IReadOnlyList<InventoryEndpointSaved> Endpoints);

/// <summary>严格且有界的存档JSON编解码器；畸形、重复、未知字段或跨周目数据一律拒绝。</summary>
internal static class InventoryEndpointCodec
{
    internal const int MaxEndpoints=128,MaxBytes=8*1024*1024;
    internal static InventoryEndpointDocument Parse(string json,int slot,string run)
    {
        if(json is null||Encoding.UTF8.GetByteCount(json)>MaxBytes)throw Invalid("LIMIT");
        if(slot<0||string.IsNullOrWhiteSpace(run)||run.Length>160)throw Invalid("SCOPE");
        try {
            using var document=JsonDocument.Parse(json,new(){MaxDepth=16});
            static void Fields(JsonElement element,params string[] names) {
                if(element.ValueKind!=JsonValueKind.Object)throw Invalid("OBJECT");
                var actual=element.EnumerateObject().Select(p=>p.Name).ToArray();
                if(actual.Length!=names.Length||actual.Distinct(StringComparer.Ordinal).Count()!=actual.Length||names.Any(n=>!actual.Contains(n,StringComparer.Ordinal)))throw Invalid("FIELDS");
            }
            var top=document.RootElement;Fields(top,"Version","SlotId","RunId","Endpoints");
            var version=top.GetProperty("Version").GetInt32();var savedSlot=top.GetProperty("SlotId").GetInt32();var savedRun=top.GetProperty("RunId").GetString();
            if(version!=1||savedSlot!=slot||savedRun!=run)throw Invalid("SCOPE");
            var entries=top.GetProperty("Endpoints");if(entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()>MaxEndpoints)throw Invalid("LIMIT");
            var keys=new HashSet<string>(StringComparer.Ordinal);var instances=new HashSet<int>();var result=new List<InventoryEndpointSaved>();
            foreach(var element in entries.EnumerateArray()) {
                Fields(element,"OwnerId","EndpointId","ItemId","InstanceId","ChargingOnly","Graph");
                var owner=element.GetProperty("OwnerId").GetString();var id=element.GetProperty("EndpointId").GetString();var item=element.GetProperty("ItemId").GetString();
                if(!PresentationRules.Owned(owner,id)||!PresentationRules.Owned(owner,item)||!keys.Add(owner+"/"+id))throw Invalid("ID");
                var instance=element.GetProperty("InstanceId").GetInt32();if(instance<=0||!instances.Add(instance))throw Invalid("INSTANCE");
                var graph=element.GetProperty("Graph").GetString();if(string.IsNullOrWhiteSpace(graph))throw Invalid("GRAPH");
                var charging=element.GetProperty("ChargingOnly").GetBoolean();result.Add(new(owner!,id!,item!,instance,charging,graph!));
            }
            return new(version,savedSlot,savedRun!,Array.AsReadOnly(result.ToArray()));
        }catch(InvalidDataException){throw;}
        catch(JsonException e){throw new InvalidDataException("EZAPI_ENDPOINT_DATA_JSON",e);}
        catch(InvalidOperationException e){throw new InvalidDataException("EZAPI_ENDPOINT_DATA_TYPE",e);}
        catch(FormatException e){throw new InvalidDataException("EZAPI_ENDPOINT_DATA_TYPE",e);}
        catch(OverflowException e){throw new InvalidDataException("EZAPI_ENDPOINT_DATA_TYPE",e);}
    }
    internal static string Serialize(int slot,string run,IReadOnlyList<InventoryEndpointSaved> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        if(slot<0||string.IsNullOrWhiteSpace(run)||run.Length>160||endpoints.Count>MaxEndpoints)throw Invalid("SCOPE");
        var json=JsonSerializer.Serialize(new InventoryEndpointDocument(1,slot,run,endpoints));
        Parse(json,slot,run);return json;
    }
    private static InvalidDataException Invalid(string code)=>new("EZAPI_ENDPOINT_DATA_"+code);
}

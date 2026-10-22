using System.Text.Json;
using Il2Cpp;
namespace EZapi;

public static partial class Liquids
{
    /// <summary>包含EZapi显式价值/品质元数据的批次快照。物理数量与已保存元数据不符时抛错，不悄悄丢弃。</summary>
    public static MachineLiquidSnapshot CaptureProduction(GameItem item)=>LiquidMetadata.Capture(item);
    /// <summary>为当前实际成分设置合计价值/品质。只改EZapi元数据，不改体积；需要库存冻结钩子，失败有补偿结果。</summary>
    public static TransactionResult SetProductionMetadata(GameItem item,MachineLiquidSnapshot snapshot)
        =>LiquidTransferApi.SetMetadata(item,snapshot);
}

/// <summary>仅保存EZapi显式液体计价元数据；原生容量/成分仍由游戏拥有。读取不修补、不覆盖损坏记录。</summary>
internal static class LiquidMetadata
{
    internal const string Tag="EZAPI_LIQUID_METADATA_V1";
    private sealed record Payload(int Version,int CapacityParts,MachineLiquidPart[] Contents);
    internal static MachineLiquidSnapshot Capture(GameItem item)
    {
        var physical=MachineLiquidSnapshot.From(Liquids.Capture(item));
        var dict=item.state?.dict;
        if(dict is null||!dict.TryGetValue(Tag,out var tag))return physical;
        var text=tag?.valueString;
        if(string.IsNullOrEmpty(text)||System.Text.Encoding.UTF8.GetByteCount(text)>1024*1024)throw Invalid("empty or oversized saved metadata");
        Payload payload;
        try {
            using var doc=JsonDocument.Parse(text,new JsonDocumentOptions{MaxDepth=8});
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in doc.RootElement.EnumerateObject())if(!keys.Add(p.Name)||p.Name is not("Version" or "CapacityParts" or "Contents"))throw Invalid("unknown or duplicate metadata field");
            if(keys.Count!=3)throw Invalid("incomplete metadata");
            foreach(var row in doc.RootElement.GetProperty("Contents").EnumerateArray()) {
                var fields=new HashSet<string>(StringComparer.Ordinal);
                foreach(var p in row.EnumerateObject())if(!fields.Add(p.Name)||p.Name is not("LiquidId" or "Parts" or "Value" or "QualityBasis"))throw Invalid("unknown or duplicate component field");
                if(!fields.Contains("LiquidId")||!fields.Contains("Parts"))throw Invalid("incomplete component");
            }
            payload=JsonSerializer.Deserialize<Payload>(text)??throw Invalid("missing metadata");
            if(payload.Version!=1||payload.Contents is null)throw Invalid("unsupported metadata version");
        }catch(Exception e)when(e is JsonException or InvalidOperationException){throw Invalid("malformed saved metadata: "+e.Message);}
        var annotated=new MachineLiquidSnapshot(payload.CapacityParts,payload.Contents);
        if(!SamePhysical(physical,annotated))throw Invalid("saved metadata no longer matches physical contents; explicit migration required");
        // 原生Remove按液体目录顺序分配整数余数；JSON条目的顺序不能改变分配结果。
        var byId=annotated.Contents.ToDictionary(p=>p.LiquidId,StringComparer.Ordinal);
        return new(annotated.CapacityParts,physical.Contents.Select(p=>byId[p.LiquidId]).ToArray());
    }
    // 只可在调用方的当前同步事务内调用；写前后都必须用原生数量核对。
    internal static void Store(GameItem item,MachineLiquidSnapshot snapshot)
    {
        if(!SamePhysical(MachineLiquidSnapshot.From(Liquids.Capture(item)),snapshot))throw Invalid("refusing metadata for different native contents");
        var dict=item.state?.dict??throw Invalid("native saved state unavailable");
        if(!snapshot.Contents.Any(p=>p.Value.HasValue||p.QualityBasis.HasValue)) {
            if(!dict.ContainsKey(Tag))return;
            dict.Remove(Tag);
        }
        else {
            var text=JsonSerializer.Serialize(new Payload(1,snapshot.CapacityParts,snapshot.Contents.ToArray()));
            if(System.Text.Encoding.UTF8.GetByteCount(text)>1024*1024)throw Invalid("metadata exceeds1MiB");
            var tag=new TagState(Tag,Tag);tag.Enable().SetString(text);dict[Tag]=tag;
        }
        item.SyncModifiedState();
        if(!Same(snapshot,Capture(item)))throw Invalid("metadata write readback differs");
    }
    internal static bool Same(MachineLiquidSnapshot a,MachineLiquidSnapshot b)=>SamePhysical(a,b)&&
        a.Contents.OrderBy(p=>p.LiquidId,StringComparer.Ordinal).SequenceEqual(b.Contents.OrderBy(p=>p.LiquidId,StringComparer.Ordinal));
    internal static bool SamePhysical(MachineLiquidSnapshot a,MachineLiquidSnapshot b)=>a.CapacityParts==b.CapacityParts&&
        a.Contents.Select(p=>(p.LiquidId,p.Parts)).OrderBy(p=>p.LiquidId,StringComparer.Ordinal).SequenceEqual(b.Contents.Select(p=>(p.LiquidId,p.Parts)).OrderBy(p=>p.LiquidId,StringComparer.Ordinal));
    private static InvalidOperationException Invalid(string error)=>new("EZAPI_LIQUID_METADATA: "+error);
}

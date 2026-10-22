namespace EZapi;

/// <summary>液体成分纯计算。Parts是原生单位，Value/QualityBasis是该条成分合计值，均不重复乘体积。</summary>
public static class LiquidCompositionMath
{
    /// <summary>原生液体基础计价：25000 parts计1价值。显式Value优先；不调用游戏目录。</summary>
    public static decimal BaseValue(IReadOnlyList<MachineLiquidPart> contents)
        => Validate(contents).Sum(p=>p.Value??NativeValue(p.Parts));

    public static IReadOnlyList<MachineLiquidPart> Consumed(MachineLiquidSnapshot before,MachineLiquidSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);ArgumentNullException.ThrowIfNull(after);
        var remaining=after.Contents.ToDictionary(p=>p.LiquidId,StringComparer.Ordinal);
        if(before.CapacityParts!=after.CapacityParts||before.Contents.Count!=remaining.Count)
            throw Invalid("snapshots must describe the same capacity and component IDs");
        var result=new List<MachineLiquidPart>();
        foreach(var p in before.Contents) {
            if(!remaining.TryGetValue(p.LiquidId,out var q)||q.Parts>p.Parts)throw Invalid("component increased or ID changed during consumption");
            var value=Difference(p.Value,q.Value);var quality=Difference(p.QualityBasis,q.QualityBasis);
            if(p.Parts==q.Parts&&(value>0||quality>0))throw Invalid("metadata changed without consumption");
            if(p.Parts>q.Parts)result.Add(new(p.LiquidId,p.Parts-q.Parts){Value=value,QualityBasis=quality});
        }
        return result.AsReadOnly();
    }

    /// <summary>纯成分转换，合并source/target条目并保留合计价值/品质；不修改输入集合或游戏物品。</summary>
    public static IReadOnlyList<MachineLiquidPart> ReplaceComponent(IReadOnlyList<MachineLiquidPart> contents,string sourceId,string targetId)
    {
        ValidateId(sourceId);ValidateId(targetId);
        var copy=Validate(contents);if(sourceId==targetId)throw Invalid("source and target IDs must differ");
        var affected=copy.Where(p=>p.LiquidId==sourceId||p.LiquidId==targetId).ToArray();
        if(!copy.Any(p=>p.LiquidId==sourceId))throw Invalid("source component not present");
        var result=copy.Where(p=>p.LiquidId!=sourceId&&p.LiquidId!=targetId).ToList();
        var total=checked((int)affected.Sum(p=>(long)p.Parts));
        result.Add(new(targetId,total){Value=affected.Any(p=>p.Value.HasValue)?BaseValue(affected):null,
            QualityBasis=affected.Any(p=>p.QualityBasis.HasValue)?Quality(affected):null});
        return result.AsReadOnly();
    }

    /// <summary>最大余数法按比例分配整数parts；余数相同按输入顺序，保证总量恰好等于请求。</summary>
    public static MachineLiquidSnapshot Consume(MachineLiquidSnapshot source,int parts,string? liquidId=null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if(parts<0||parts>source.TotalParts)throw new ArgumentOutOfRangeException(nameof(parts));
        var amounts=new int[source.Contents.Count];
        if(liquidId is not null) {
            ValidateId(liquidId);var index=source.Contents.ToList().FindIndex(p=>p.LiquidId==liquidId);
            if(index<0||parts>source.Contents[index].Parts)throw Invalid("requested component amount unavailable");
            amounts[index]=parts;
        } else if(parts>0) {
            var assigned=0;var fractions=new List<(int Index,long Remainder)>();
            for(var i=0;i<amounts.Length;i++) {
                var product=(long)source.Contents[i].Parts*parts;
                amounts[i]=(int)(product/source.TotalParts);assigned+=amounts[i];
                fractions.Add((i,product%source.TotalParts));
            }
            foreach(var entry in fractions.OrderByDescending(p=>p.Remainder).ThenBy(p=>p.Index).Take(parts-assigned))amounts[entry.Index]++;
        }
        return new(source.CapacityParts,source.Contents.Select((p,i)=>Remainder(p,p.Parts-amounts[i])).ToArray());
    }

    public static MachineLiquidSnapshot Add(MachineLiquidSnapshot target,IReadOnlyList<MachineLiquidPart> contents)
    {
        ArgumentNullException.ThrowIfNull(target);var additions=Validate(contents).ToDictionary(p=>p.LiquidId,StringComparer.Ordinal);
        if(additions.Keys.Except(target.Contents.Select(p=>p.LiquidId),StringComparer.Ordinal).Any())throw Invalid("component absent from target catalog");
        return new(target.CapacityParts,target.Contents.Select(p=>!additions.TryGetValue(p.LiquidId,out var q)?p:new MachineLiquidPart(p.LiquidId,checked(p.Parts+q.Parts)) {
            Value=p.Value.HasValue||q.Value.HasValue?(p.Value??NativeValue(p.Parts))+(q.Value??NativeValue(q.Parts)):null,
            QualityBasis=p.QualityBasis.HasValue||q.QualityBasis.HasValue?Quality(new[]{p,q}):null}).ToArray());
    }

    internal static decimal Quality(IReadOnlyList<MachineLiquidPart> contents)=>contents.Sum(p=>p.QualityBasis??p.Value??NativeValue(p.Parts));
    internal static decimal NativeValue(int parts)=>parts/25000m;
    internal static MachineLiquidPart Remainder(MachineLiquidPart p,int remaining)=>new(p.LiquidId,remaining) {
        Value=Scale(p.Value,p.Parts,remaining),QualityBasis=Scale(p.QualityBasis,p.Parts,remaining)};
    private static decimal? Scale(decimal? value,int before,int after)=>value.HasValue?(after==0?0m:after==before?value.Value:value.Value*(after/(decimal)before)):null;
    private static decimal? Difference(decimal? before,decimal? after)
    {
        if(before.HasValue!=after.HasValue||after>before)throw Invalid("component metadata missing or increased");
        return before-after;
    }
    private static MachineLiquidPart[] Validate(IReadOnlyList<MachineLiquidPart> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if(contents.Count>4096)throw Invalid("max4096 components");
        var copy=contents.ToArray();var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var p in copy) {
            if(p is null)throw Invalid("null component");ValidateId(p.LiquidId);
            if(!ids.Add(p.LiquidId)||p.Parts<0||p.Value<0||p.QualityBasis<0||p.Parts==0&&(p.Value>0||p.QualityBasis>0))throw Invalid("duplicate/negative/nonzero empty component metadata");
        }
        return copy;
    }
    internal static void ValidateId(string id){if(string.IsNullOrWhiteSpace(id)||id.Length>160||id.Any(char.IsControl))throw Invalid("readable component ID <=160 required");}
    private static ArgumentException Invalid(string error)=>new("EZAPI_LIQUID_MATH: "+error);
}

using Il2Cpp;
namespace EZapi;

public sealed record LiquidPart(string Id, int Parts);
/// <summary>纯托管只读快照。Parts 保留原生单位；1000 parts = 1 ml（当前构建对照契约）。</summary>
public sealed class LiquidSnapshot
{
    internal LiquidSnapshot(int capacityParts, IEnumerable<LiquidPart> contents, int purity, string? conditionId)
    {
        CapacityParts = capacityParts; Contents = Array.AsReadOnly(contents.ToArray());
        TotalParts = checked(Contents.Sum(p => p.Parts)); Purity = purity; ConditionId = conditionId;
    }
    public int CapacityParts { get; }
    public IReadOnlyList<LiquidPart> Contents { get; }
    public int TotalParts { get; }
    public int FreeParts => CapacityParts - TotalParts;
    public decimal Millilitres => TotalParts / 1000m;
    /// <summary>原生 WaterHelper 的整数纯度值，不自行解释为百分比。</summary>
    public int Purity { get; }
    public string? ConditionId { get; }
}

/// <summary>原生液体容器观察。快照不保留物品引用，不倒液、不改成分、不初始化全局液体目录。</summary>
public static partial class Liquids
{
    public static bool IsContainer(GameItem item)
    {
        NativeAccess.RequireItem(item);
        return item.unitCount == 1 && item.IsTag("LIQUID_CONTAINER_TAG") &&
            (long)WaterHelper.GetFreeCapacity(item) + WaterHelper.GetTotalVolume(item) > 0;
    }
    public static LiquidSnapshot Capture(GameItem item)
    {
        if (!IsContainer(item)) throw new ArgumentException("EZAPI_LIQUID_CONTAINER: native single liquid container required.");
        var liquids = Liquid.Liquids;
        if (liquids is null || liquids.Count is < 1 or > 4096)
            throw new InvalidOperationException("EZAPI_LIQUID_CATALOG: liquid catalog unavailable or outside bounded scan.");
        var parts = new List<LiquidPart>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var seenTags = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        for (var i = 0; i < liquids.Count; i++) {
            var liquid = liquids[i];
            if (liquid is null || string.IsNullOrWhiteSpace(liquid.identifier) || string.IsNullOrWhiteSpace(liquid.currentPart) ||
                !seen.Add(liquid.identifier) || !seenTags.Add(liquid.currentPart))
                throw new InvalidOperationException("EZAPI_LIQUID_CATALOG: ambiguous liquid IDs or volume tags.");
            var value = item.GetTagReadonly(liquid.currentPart)?.GetInt() ?? 0;
            if (value < 0 || (total += value) > int.MaxValue)
                throw new InvalidOperationException("EZAPI_LIQUID_STATE: invalid component quantity.");
            parts.Add(new(liquid.identifier, value));
        }
        var nativeTotal = WaterHelper.GetTotalVolume(item);
        var free = WaterHelper.GetFreeCapacity(item);
        if (nativeTotal != total || free < 0 || total + free > int.MaxValue)
            throw new InvalidOperationException("EZAPI_LIQUID_STATE: component totals/capacity disagree with native state.");
        var purity = WaterHelper.GetWaterPurity(item);
        return new((int)(total + free), parts, purity, WaterFeatureHelper.GetConditionFromPurity(purity)?.identifier);
    }
    /// <summary>返回原生水质分档：纯水3、高品质2、基础1、其余0；不根据名字/tooltip猜测。</summary>
    public static int WaterQuality(GameItem item)
    {
        if (!IsContainer(item)) throw new ArgumentException("EZAPI_LIQUID_CONTAINER: native single liquid container required.");
        var condition = WaterFeatureHelper.GetConditionFromPurity(WaterHelper.GetWaterPurity(item))?.identifier;
        if (condition == ItemConditionList.CreatePureWater()?.identifier && condition is not null) return 3;
        if (condition == ItemConditionList.CreateHighQualityWater()?.identifier && condition is not null) return 2;
        if (condition == ItemConditionList.CreateBaseWater()?.identifier && condition is not null) return 1;
        return 0;
    }
}

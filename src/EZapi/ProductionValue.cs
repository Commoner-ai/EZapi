using System.Collections.ObjectModel;
using System.Globalization;
using Il2Cpp;
namespace EZapi;

/// <summary>一次批次实际消耗的物品。Value 是 Count 件的合计价值，绝不能再次乘 Count。</summary>
public sealed record MachineItemTake(GameItem Item, int Count)
{
    public decimal Value { get; init; }
}

/// <summary>液体成分的原生 parts；可选 Value/QualityBasis 是该成分的价值与品质计算依据。</summary>
public sealed record MachineLiquidPart(string LiquidId, int Parts)
{
    public decimal? Value { get; init; }
    public decimal? QualityBasis { get; init; }
}

/// <summary>可用于批次前后比较的托管数据；1000 parts = 1 ml，不持有原生对象。</summary>
public sealed record MachineLiquidSnapshot
{
    public MachineLiquidSnapshot(int capacityParts, IReadOnlyList<MachineLiquidPart> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (capacityParts < 0 || contents.Count > 4096) throw new ArgumentOutOfRangeException(nameof(capacityParts));
        var copy = contents.ToArray();
        if (copy.Any(p => p is null || string.IsNullOrWhiteSpace(p.LiquidId) || p.LiquidId.Length > 160 || p.LiquidId.Any(char.IsControl) ||
            p.Parts < 0 || p.Value < 0 || p.QualityBasis < 0 || p.Parts == 0 && (p.Value > 0 || p.QualityBasis > 0)) ||
            copy.Select(p => p.LiquidId).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("EZAPI_PRODUCTION_LIQUID: unique components and nonnegative quantities required.");
        var total = copy.Sum(p => (long)p.Parts);
        if (total > capacityParts) throw new ArgumentException("EZAPI_PRODUCTION_LIQUID: contents exceed capacity.");
        CapacityParts = capacityParts; Contents = Array.AsReadOnly(copy); TotalParts = (int)total;
    }
    public int CapacityParts { get; }
    public IReadOnlyList<MachineLiquidPart> Contents { get; }
    public int TotalParts { get; }
    public decimal Millilitres => TotalParts / 1000m;
    public void Deconstruct(out int capacityParts, out IReadOnlyList<MachineLiquidPart> contents)
        => (capacityParts, contents) = (CapacityParts, Contents);
    public static MachineLiquidSnapshot From(LiquidSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(source.CapacityParts, source.Contents.Select(p => new MachineLiquidPart(p.Id, p.Parts)).ToArray());
    }
}

/// <summary>Value 是本次消耗液体的总价值；Before/After 保留真实成分，不把容器本身价格计入。</summary>
public sealed record MachineLiquidTake(string SlotId, GameItem Container, MachineLiquidSnapshot Before,
    MachineLiquidSnapshot After, decimal Value)
{
    public decimal QualityBasis { get; init; }
}

/// <summary>同步批次数据。集合拷贝后只读；其中的原生物品只可在当前同步加工回调使用，不可跨场景缓存。</summary>
public sealed record MachineBatchContext
{
    private IReadOnlyList<MachineItemTake> _items = Array.Empty<MachineItemTake>();
    private IReadOnlyList<MachineLiquidTake> _liquids = Array.Empty<MachineLiquidTake>();
    private IReadOnlyDictionary<string, GameItem> _containers = new ReadOnlyDictionary<string, GameItem>(new Dictionary<string, GameItem>());
    public MachineBatchContext(GameItem machine, IReadOnlyList<MachineItemTake> items,
        IReadOnlyDictionary<string, GameItem> liquidContainers, int outputCount)
        => (Machine, Items, LiquidContainers, OutputCount) = (machine, items, liquidContainers, outputCount);
    public GameItem Machine { get; init; }
    public IReadOnlyList<MachineItemTake> Items { get => _items; init => _items = Copy(value); }
    public IReadOnlyDictionary<string, GameItem> LiquidContainers
    {
        get => _containers;
        init {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count > 4096) throw new ArgumentException("EZAPI_PRODUCTION_LIMIT: max4096 liquid slots.");
            _containers = new ReadOnlyDictionary<string, GameItem>(value.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        }
    }
    public int OutputCount { get; init; }
    /// <summary>供加工策略读取；Calculate 的 markupPercent 参数优先，不会偷偷叠加此值。</summary>
    public int ProductionMarkupPercent { get; init; }
    public IReadOnlyList<MachineLiquidTake> Liquids { get => _liquids; init => _liquids = Copy(value); }
    public void Deconstruct(out GameItem machine, out IReadOnlyList<MachineItemTake> items,
        out IReadOnlyDictionary<string, GameItem> liquidContainers, out int outputCount)
        => (machine, items, liquidContainers, outputCount) = (Machine, Items, LiquidContainers, OutputCount);
    private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Count > 4096) throw new ArgumentException("EZAPI_PRODUCTION_LIMIT: max4096 takes per collection.");
        var copy = value.ToArray();
        if (copy.Any(p => p is null)) throw new ArgumentException("EZAPI_PRODUCTION_TAKE: null entry.");
        return Array.AsReadOnly(copy);
    }
}

/// <summary>纯计算，不需要游戏就绪。所有中间值为 decimal，最后向上取整到单件整数价值。</summary>
public static class ProductionValueMath
{
    public static long PerOutput(decimal inputValue, int markupPercent, int outputCount = 1)
        => PerOutputWithOverhead(inputValue, markupPercent, 0m, outputCount);
    /// <summary>ceil((投入×(100+加成)/100 + 整批额外成本)/产量)。额外成本不再乘加成。</summary>
    public static long PerOutputWithOverhead(decimal inputValue, int markupPercent, decimal batchOverhead, int outputCount = 1)
    {
        if (inputValue < 0) throw new ArgumentOutOfRangeException(nameof(inputValue));
        if (markupPercent < 0) throw new ArgumentOutOfRangeException(nameof(markupPercent));
        if (batchOverhead < 0) throw new ArgumentOutOfRangeException(nameof(batchOverhead));
        MachinePowerMath.ValidateCount(outputCount);
        return checked((long)decimal.Ceiling((inputValue * (100m + markupPercent) / 100m + batchOverhead) / outputCount));
    }
}

/// <summary>批次电费计算；不扣电，不修改效率，不驱动机器运行。</summary>
public static class MachinePowerMath
{
    public static int CalculateCost(int cost, int outputCount, bool perOutput)
    {
        if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost));
        ValidateCount(outputCount);
        return checked(cost * (perOutput ? outputCount : 1));
    }
    internal static void ValidateCount(int outputCount)
    { if (outputCount is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(outputCount), "EZAPI_PRODUCTION_COUNT: 1..256 outputs required."); }
}

/// <summary>批次合计计价与物品基础价值保存标签。计算不会消耗材料或生产；标签写入不触发保存。</summary>
public static class ProductionValueApi
{
    public const string BasisTag = "EZAPI_PRODUCTION_BASIS_V1";
    private static readonly HashSet<IntPtr> Writing = new();
    public static decimal InputValue(MachineBatchContext context, Func<MachineItemTake, decimal>? itemValue = null,
        Func<MachineLiquidTake, decimal>? liquidValue = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        decimal total = 0;
        foreach (var take in context.Items) total += itemValue is null ? take.Value : itemValue(take);
        foreach (var take in context.Liquids) total += liquidValue is null ? take.Value : liquidValue(take);
        return total;
    }
    public static long Calculate(MachineBatchContext context, int markupPercent,
        Func<MachineItemTake, decimal>? itemValue = null, Func<MachineLiquidTake, decimal>? liquidValue = null)
        => ProductionValueMath.PerOutput(InputValue(context, itemValue, liquidValue), markupPercent, context.OutputCount);

    /// <summary>当前主线程/周目中的活物品。只写本框架标签与unitValue，并严格读回；失败尝试恢复原值。</summary>
    public static void SetBasis(GameItem item, long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        NativeAccess.RequireItem(item);
        var run = GameSession.Identity!; var pointer = item.Pointer; var instance = item.uniqueId;
        if (!Writing.Add(pointer)) throw new InvalidOperationException("EZAPI_PRODUCTION_REENTRANT: basis write already running.");
        try {
            var dict = item.state?.dict ?? throw new InvalidOperationException("EZAPI_PRODUCTION_STATE: native tag dictionary unavailable.");
            var existed = dict.TryGetValue(BasisTag, out var oldTag);
            var oldValue = item.unitValue;
            // 用新标签避免修改旧TagState的其它字段；回滚可放回旧对象，保留它的全部原生状态。
            var newTag = new TagState(BasisTag, BasisTag);
            newTag.Enable().SetString(value.ToString(CultureInfo.InvariantCulture));
            try {
                dict[BasisTag] = newTag;
                item.SyncModifiedState();
                if (!Same() || item.state?.dict?.Pointer != dict.Pointer)
                    throw new InvalidOperationException("item/run/state changed during SyncModifiedState.");
                item.unitValue = value;
                if (!Same() || ReadBasis(item, -1) != value || item.unitValue != value)
                    throw new InvalidOperationException("write readback mismatch.");
            } catch (Exception error) {
                try {
                    if (!Same() || item.state?.dict?.Pointer != dict.Pointer)
                        throw new InvalidOperationException("item/run/state changed; recovery write refused.");
                    if (dict.TryGetValue(BasisTag, out var actualTag) && actualTag?.Pointer != newTag.Pointer)
                        throw new InvalidOperationException("another callback replaced basis tag; recovery write refused.");
                    if (existed) dict[BasisTag] = oldTag!; else dict.Remove(BasisTag);
                    item.SyncModifiedState(); item.unitValue = oldValue;
                    if (!Same() || item.unitValue != oldValue || dict.ContainsKey(BasisTag) != existed ||
                        (existed && dict[BasisTag]?.Pointer != oldTag?.Pointer))
                        throw new InvalidOperationException("recovery readback mismatch.");
                } catch (Exception recovery) {
                    throw new InvalidOperationException("EZAPI_PRODUCTION_RECOVERY_FAILED: " + recovery.Message, error);
                }
                throw new InvalidOperationException("EZAPI_PRODUCTION_WRITE_RESTORED: " + error.Message, error);
            }
        } finally { Writing.Remove(pointer); }
        bool Same() => NativeAccess.SameRun(run) && item.Pointer == pointer && item.uniqueId == instance && item.unitCount > 0;
    }

    /// <summary>只读；不存在/空字符串返回fallback，损坏记录抛异常，绝不自动覆盖。fallback允许负数哨兵。</summary>
    public static long ReadBasis(GameItem item, long fallback = 0)
    {
        NativeAccess.RequireItem(item);
        var text = item.GetTagReadonly(BasisTag)?.valueString;
        if (string.IsNullOrEmpty(text)) return fallback;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidOperationException("EZAPI_PRODUCTION_BASIS_CORRUPT: invalid saved production basis.");
        return value;
    }
}

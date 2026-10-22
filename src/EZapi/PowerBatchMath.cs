namespace EZapi;

/// <summary>一个原生电源实例的稳定 ID 和当前可用整数电量。</summary>
public sealed record PowerSourceAmount(int InstanceId, int Energy);

/// <summary>按稳定实例 ID 排序后，从该电源扣除的计划金额。</summary>
public sealed record PowerSourceDebit(int InstanceId, int Before, int Debit);

/// <summary>
/// 纯托管、确定性的多电源扣款规划。只计算计划，不读取或修改游戏物品。
/// </summary>
public static class PowerBatchMath
{
    /// <summary>
    /// 先验证并按 InstanceId 升序排列快照，再从低 ID 电源开始分配费用。
    /// 费用为零或总电量不足时返回空列表；重复/无效身份、负电量和负费用会明确拒绝。
    /// </summary>
    public static IReadOnlyList<PowerSourceDebit> Plan(IEnumerable<PowerSourceAmount> sources, int cost)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost), "EZAPI_POWER_BATCH_COST: nonnegative native units required.");

        var ordered = new List<PowerSourceAmount>();
        var ids = new HashSet<int>();
        foreach (var source in sources)
        {
            if (source is null) throw new ArgumentException("EZAPI_POWER_BATCH_SOURCE: null source snapshot.", nameof(sources));
            if (source.InstanceId <= 0 || source.Energy < 0)
                throw new ArgumentException("EZAPI_POWER_BATCH_SOURCE: positive instance ID and nonnegative energy required.", nameof(sources));
            if (!ids.Add(source.InstanceId))
                throw new ArgumentException("EZAPI_POWER_BATCH_DUPLICATE: each native instance may appear only once.", nameof(sources));
            ordered.Add(source);
        }

        if (cost == 0 || ordered.Count == 0) return Array.Empty<PowerSourceDebit>();
        ordered.Sort(static (left, right) => left.InstanceId.CompareTo(right.InstanceId));
        var available = ordered.Sum(static source => (long)source.Energy);
        if (available < cost) return Array.Empty<PowerSourceDebit>();

        var remaining = cost;
        var plan = new List<PowerSourceDebit>();
        foreach (var source in ordered)
        {
            if (remaining == 0) break;
            var debit = Math.Min(source.Energy, remaining);
            if (debit <= 0) continue;
            plan.Add(new(source.InstanceId, source.Energy, debit));
            remaining -= debit;
        }
        return remaining == 0 ? plan.AsReadOnly() : Array.Empty<PowerSourceDebit>();
    }
}

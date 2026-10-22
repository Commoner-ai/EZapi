using Il2Cpp;

namespace EZapi.Sample;

/// <summary>计划多个库存电源的确定性分摊；这里只读，不消费电量。</summary>
public static class PowerExamples
{
    public static IReadOnlyList<PowerSourceDebit> Plan(IReadOnlyList<GameItem> currentSources, int cost)
    {
        ArgumentNullException.ThrowIfNull(currentSources);
        var snapshot = currentSources
            .Select(source => new PowerSourceAmount(source.uniqueId, Power.GetEnergy(source)))
            .ToArray();
        return PowerBatchMath.Plan(snapshot, cost);
    }
}

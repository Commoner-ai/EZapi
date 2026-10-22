using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>独立DLL调用范例；不会由默认JSON包自动运行，不增加自动生成/加工。</summary>
public static class ProductionEffectExamples
{
    /// <summary>在OnInitializeMelon提交。工厂仅在目录就绪后执行；每次返回独立效果对象。</summary>
    public static ContentSubmissionResult RegisterNamedEffect()
        => EffectApi.RegisterEffect("ezapi.sample", "ezapi.sample.stable-effect", () => new ModuleEffectHelper.ModuleEffect {
            identifier = "ezapi.sample.stable-effect", displayName = "EZapi 效果注册示例",
            description = "声明与随机排除示例；此示例没有数值回调，不提供额外加成。"
        }, randomEligible: false);

    /// <summary>materials的Value为对应Count的合计；方法只算价，不消耗、不创建产物。</summary>
    public static long Quote(GameItem machine, IReadOnlyList<MachineItemTake> materials, int count)
    {
        var batch = new MachineBatchContext(machine, materials, new Dictionary<string, GameItem>(), count);
        return ProductionValueApi.Calculate(batch, markupPercent: 20);
    }

    /// <summary>只对调用者本次创建且尚未发放的产物设置；应在所属加工事务中处理异常/恢复结果。</summary>
    public static void SetFreshProductValue(GameItem freshProduct, long unitValue)
    {
        ProductionValueApi.SetBasis(freshProduct, unitValue);
        if (ProductionValueApi.ReadBasis(freshProduct, -1) != unitValue)
            throw new InvalidOperationException("基础价值读回不一致，请停止本批。");
    }
}

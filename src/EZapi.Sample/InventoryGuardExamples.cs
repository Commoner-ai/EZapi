using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>调用示例，不自动注册或执行。内容Mod在初始化保存规则句柄，卸载时Dispose；原生对象仅限当前调用。</summary>
public static class InventoryGuardExamples
{
    /// <summary>只限制示例储物箱对应子库存。原生允许后，还要求目标物品ID匹配；不创建自己的拖拽流程。</summary>
    public static IDisposable RegisterMedicalOnlyRule()
        => InventoryAdmissionApi.Register("ezapi.sample", "ezapi.sample.storage_box", "ezapi.sample.storage_box.storage",
            (incoming, _) => incoming.identifier is "ezapi.sample.wound_dressing" or "ezapi.sample.reusable_dressing");

    /// <summary>冻结期间不能调用Move来移动这些物品；应先释放，再重新预检和提交。不代表事务预留。</summary>
    public static bool ReadUnderShortFreeze(GameItem currentItem, Action inspectReadOnly)
    {
        using var lease=InventoryFreezeApi.TryAcquire(new[]{currentItem}, "ezapi.sample.inspect");
        if(lease is null)return false;
        inspectReadOnly();return true; // using即使异常也释放本句柄。
    }
    /// <summary>在原生物品工厂中每次重建都调用，保证保存图恢复有相同的库存键。owner必须未放置且没有窗口。</summary>
    public static GameGridInventory InitializeFactoryList(GameItem detachedOwner)
        => ListInventoryApi.Create(detachedOwner,"ezapi.sample.factory_list.storage");
}

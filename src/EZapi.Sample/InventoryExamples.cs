using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>供Mod作者复制的同步调用函数；框架不会自动调用，不会自动搬运或保存。</summary>
public static class InventoryExamples
{
    /// <summary>从当前已载入周目调用。快照可序列化，原生player不能跨场景保存。</summary>
    public static NativeInventorySnapshot ReadCurrent(PlayerStore player)
        => InventoryApi.CaptureDirect(player.gridInv);

    /// <summary>仅由你自己的主线程按钮调用。Indeterminate必须停止批次并记录，不能自动重试。</summary>
    public static ItemTransferResult MoveFromOwnButton(GameItem currentItem, GameGridInventory currentTarget, int maximum)
        => ItemTransferApi.Move(currentItem, currentTarget, maximum);

    /// <summary>产品由调用者创建并负责交付/失败清理；这里只规划，不预留或接纳，不能缓存到下一帧后直接提交。</summary>
    public static IReadOnlyList<InventoryPlacement> PlanDetachedProducts(GameGridInventory currentTarget, IReadOnlyList<GameItem> products)
        => InventoryPlacementApi.PlanWholeGrid(currentTarget, products);
}

/// <summary>持久隐藏库存示例。必须在游戏主线程和当前周目调用；Open(false)只恢复既有记录，不生成替代库存。</summary>
public static class InventoryEndpointExamples
{
    private const string Owner="ezapi.sample", EndpointId=Owner+".battery_cache", RootItemId=Owner+".storage_box";

    /// <summary>在新目录应用后创建或恢复同一端点。chargingOnly只隔离充电枚举，不限制箱内准入类型。</summary>
    public static InventoryEndpoint? Open(PlayerStore currentStore)
        => InventoryEndpointApi.Open(currentStore,Owner,EndpointId,RootItemId,createIfMissing:true,chargingOnly:true);

    /// <summary>只读端点直接内容。需要坐标/形状时再传includeGeometry=true。</summary>
    public static NativeInventorySnapshot ReadContents(InventoryEndpoint endpoint)
        =>InventoryEndpointApi.CaptureContents(endpoint);

    /// <summary>从当前原生库存存入端点；Indeterminate=true时停止批次并人工核对，绝不自动重试。</summary>
    public static ItemTransferResult Store(InventoryEndpoint endpoint,GameItem currentItem,int maximum)
        =>InventoryEndpointApi.Store(endpoint,currentItem,maximum);

    /// <summary>从端点取出一个直接子项到本次调用提供的当前网格。</summary>
    public static ItemTransferResult Take(InventoryEndpoint endpoint,GameItem directChild,GameGridInventory currentDestination,int maximum)
        =>InventoryEndpointApi.Take(endpoint,directChild,currentDestination,maximum);

    /// <summary>仅删除该作者自己的空端点；失败表示内容、拖拽或原生窗口仍在使用。</summary>
    public static bool RemoveEmptyEndpoint()=>InventoryEndpointApi.Delete(Owner,EndpointId);
}

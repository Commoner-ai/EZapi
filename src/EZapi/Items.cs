using Il2Cpp;
namespace EZapi;

/// <summary>已载入周目中的原生目录访问；支持原版和其它Mod，不替代 EZapi 声明注册。</summary>
public static class Items
{
    public static bool Exists(string itemId)
    {
        NativeAccess.RequireRun(); ValidateId(itemId); return DirectoryMaster.Has<GameItem>(itemId);
    }
    /// <summary>创建未放入库存的新实例。调用者必须通过原生接纳交付，或在失败后 Destroy；不自动保存。</summary>
    public static GameItem Create(string itemId, bool owned = true)
    {
        if (!Exists(itemId)) throw new ArgumentException("EZAPI_ITEM_MISSING: " + itemId);
        var item=DirectoryMaster.Item(itemId, owned);
        if(!NativeFactoryRegistry.HasCreationError(itemId)&&item is not null&&item.Pointer!=IntPtr.Zero&&item.identifier==itemId&&item.unitCount>0&&item.parentInventory is null)return item;
        // 当前游戏在工厂失败时可能返回UNKNOWN占位实例，不能只检查null后报告创建成功。
        // 只清理原生创建流程返回的游离新对象；已入库存的错误结果不属于安全销毁范围。
        if(item is not null&&item.Pointer!=IntPtr.Zero&&item.parentInventory is null)item.Destroy();
        throw new InvalidOperationException("EZAPI_ITEM_FACTORY: factory did not return a valid detached instance of " + itemId);
    }
    public static string GetDisplayName(string itemId)
    {
        if (!Exists(itemId)) throw new ArgumentException("EZAPI_ITEM_MISSING: " + itemId);
        return GeneralHelper.GetDisplayNameFromID(itemId);
    }
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(char.IsControl))
            throw new ArgumentException("EZAPI_ITEM_ID: readable native item ID required.");
    }
}

/// <summary>对当前实例读取游戏自己的价格和提示结果。调用应按需或节流，禁止每帧创建检查物品。</summary>
public static class ItemInspection
{
    /// <summary>当前实例的一件价值（含原生子项/事件规则），支持堆叠；不是整叠合计或最终成交价。</summary>
    public static long CurrentUnitValue(GameItem item)
    {
        NativeAccess.RequireItem(item);
        return item.GetCurrentValue(false, true, true, false, 0);
    }
    /// <summary>由游戏完成 RichText 构建；不调用 builder.Build，不安装 tooltip 补丁。</summary>
    public static RichText BuildTooltip(GameItem item, bool advanced = false)
    {
        NativeAccess.RequireItem(item);
        var element = item.TryCast<GameItemElement>() ?? throw new InvalidOperationException("EZAPI_INSPECTION_ELEMENT: native tooltip element unavailable.");
        return advanced ? element.GetTooltipAdvanced() : element.GetTooltip();
    }
}

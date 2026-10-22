using Il2Cpp;
namespace EZapi;

/// <summary>
/// 官方窗口/网格结构的单点适配。仅在物品工厂中创建，不持有静态窗口/库存/玩家引用。
/// SetContentWindow 建立原生存储图；空网格交给游戏恢复内容，不发物、不搬运、不保存。
/// </summary>
internal static class NativeContainerAdapter
{
    internal static void Initialize(GameItem item, ItemDefinition definition)
    {
        if (definition.Container is not { } spec) return;
        var grid = spec.ShapeRows is { } rows
            // 声明1=可用格；GameGridInventory字符串构造器1=禁止格，与物品形状正好相反。
            ? new GameGridInventory(string.Concat(rows).Replace('1', 'x').Replace('0', '1').Replace('x', '0'), spec.Width)
            : new GameGridInventory(spec.Width, spec.Height);
        // 稳定子库存键，重建时一致。同类物品窗口/网格仍是独立实例。
        grid.identifier = definition.Id + ".storage";
        // 明确采用游戏公开构造参数，避免更改全局 PixelWindow 或交易窗口的布局。
        var locale=NativeItemLocalization.CurrentLocaleCode;
        var title=ItemText.Resolve(definition,spec.LocalizedTitle,spec.Title??ItemText.Name(definition,locale),locale);
        var window = new PixelWindow(spec.Draggable, title);
        if(!NativeWindowTitles.Matches(window,title))NativeWindowTitles.Set(window,title);
        item.SetContentWindow(window);
        var element = grid.TryCast<PixelElement>()
            ?? throw new InvalidOperationException("EZAPI_CONTAINER_GRAPH: grid has no native PixelElement interface.");
        if (!window.Attach(element))
            throw new InvalidOperationException("EZAPI_CONTAINER_GRAPH: native window refused grid attachment.");
        // 原生两个过滤集合按OR组合；存在过滤时，另一集合必须为空集合，null会开放该分支。
        var filtered=spec.AllowedTags.Count>0||spec.AllowedIds.Count>0;
        ContainerHelper.InitContainerItem(grid, item, NativeList(spec.AllowedTags,filtered), NativeList(spec.AllowedIds,filtered));
        // The native helper can validate the window after it gains its final grid size.
        // Reapply the localized title against that final geometry, without resizing the grid.
        if(!NativeWindowTitles.Matches(window,title))NativeWindowTitles.Set(window,title);
    }

    private static Il2CppSystem.Collections.Generic.List<string>? NativeList(IReadOnlyList<string> values,bool filtered)
    {
        if (values.Count == 0&&!filtered) return null;
        var result = new Il2CppSystem.Collections.Generic.List<string>();
        foreach (var value in values) result.Add(value);
        return result;
    }
}

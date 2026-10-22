using Il2Cpp;
namespace EZapi;

/// <summary>每次查询真实可见店铺库存。Unity包装对象非C# null并不代表场景对象仍存活；不缓存跨场景引用。</summary>
internal static class VisibleStoreInventory
{
    internal static bool TryGet(out GameGridInventory inventory)
    {
        inventory=null!;
        var entry=EmporiumEntry.Instance;
        var ui=StoreUIManager.Instance;
        if(entry==null||ui==null)return false;
        var canvas=ui.inventoryCanvas;
        if(canvas==null||!canvas.activeInHierarchy)return false;
        var current=entry.invElement;
        if(current is null||current.Pointer==IntPtr.Zero)return false;
        inventory=current;return true;
    }
    internal static bool IsReady=>TryGet(out _);
}

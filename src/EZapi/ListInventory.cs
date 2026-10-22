using Il2Cpp;
namespace EZapi;

/// <summary>具有稳定子库存键、按需增大的原生网格。名称沿用列表库存能力，不替换游戏UI为文本列表。</summary>
public static class ListInventoryApi
{
    internal const string WidthTag="EZAPI_LIST_WIDTH",HeightTag="EZAPI_LIST_HEIGHT";
    private static bool _moving;
    public static bool IsAvailable=>GameSession.OnThread&&InventoryRuntime.CatalogReady&&InventoryRuntime.VerifyLists();

    /// <summary>在物品原生工厂中为未放置单件owner创建独立存储图，必须每次重建均调用；不覆盖已有窗口。</summary>
    public static GameGridInventory Create(GameItem owner,string inventoryId)
    {
        GameSession.RequireThread();ValidateId(inventoryId);
        if(!IsAvailable)throw new InvalidOperationException("EZAPI_LIST_NOT_READY: native list restore hook unavailable.");
        if(owner is null||owner.Pointer==IntPtr.Zero||owner.unitCount!=1||owner.parentInventory is not null||owner.contentWindow is not null)
            throw new ArgumentException("EZAPI_LIST_OWNER: detached single owner without a content window required.");
        var grid=new GameGridInventory(16,16){identifier=inventoryId};
        var window=new PixelWindow(true,owner.name);
        if(!window.Attach(grid.Cast<PixelElement>()))throw new InvalidOperationException("EZAPI_LIST_ATTACH: native window refused grid.");
        owner.SetContentWindow(window);
        if(grid.GetParentItem()?.Pointer!=owner.Pointer)throw new InvalidOperationException("EZAPI_LIST_ATTACH: native parent graph differs.");
        ContainerHelper.InitContainerItemTagOnly(grid,owner);
        // 工厂可能收到已恢复状态的owner，不能用初始尺寸覆盖存档标签。
        if(StoredTag(owner,WidthTag) is null&&StoredTag(owner,HeightTag) is null)WriteDimensions(owner,16,16);
        else RestoreDimensions(grid);
        return grid;
    }
    public static GameGridInventory? Find(GameItem owner,string inventoryId)
    {
        GameSession.RequireThread();ValidateId(inventoryId);
        if(owner is null||owner.Pointer==IntPtr.Zero)return null;
        var grid=owner.contentWindow?.childElement?.TryCast<GameGridInventory>();
        return grid is not null&&grid.identifier==inventoryId&&grid.GetParentItem()?.Pointer==owner.Pointer&&IsList(grid)?grid:null;
    }
    public static bool IsList(GameGridInventory inventory)
    {
        GameSession.RequireThread();
        if(inventory is null||inventory.Pointer==IntPtr.Zero)return false;
        var owner=inventory.GetParentItem();
        return owner is not null&&owner.Pointer!=IntPtr.Zero&&StoredTag(owner,WidthTag) is not null&&StoredTag(owner,HeightTag) is not null;
    }

    /// <summary>只接纳EZapi列表库存。可选资格回调在扩容前执行；空间不足有限扩至64x64，然后复用同一单次审计搬运。</summary>
    public static ItemTransferResult Move(GameItem item,GameInventory destination,int maximum,Func<GameItem,int,bool>? accepts=null)
    {
        NativeAccess.RequireRun();NativeAccess.RequireItem(item);
        if(_moving)return new(0,false,"Reentrant",Error:"EZAPI_LIST_REENTRANT");
        _moving=true;
        try{return MoveCore(item,destination,maximum,accepts);}finally{_moving=false;}
    }
    private static ItemTransferResult MoveCore(GameItem item,GameInventory destination,int maximum,Func<GameItem,int,bool>? accepts)
    {
        var run=NativeAccess.RequireRun();NativeAccess.RequireItem(item);
        if(maximum<1)throw new ArgumentOutOfRangeException(nameof(maximum));
        var grid=destination?.TryCast<GameGridInventory>();
        if(!IsAvailable||grid is null||!IsList(grid))return new(0,false,"Rejected",Error:"EZAPI_LIST_TARGET: EZapi list grid required.");
        var source=item.parentInventory;
        if(source is null||!InventoryChecks.CanMove(item,source,grid))return new(0,false,"Rejected",Error:"EZAPI_LIST_REJECTED");
        var amount=Math.Min(maximum,Math.Min(item.unitCount,item.MaxNumRemove()));
        if(amount<1)return new(0,false,"Rejected");
        var beforeItem=InventoryChecks.Item(item);
        var beforeSource=InventoryChecks.Read(source,run);var beforeTarget=InventoryChecks.Read(grid,run);
        try {
            var allowed=accepts?.Invoke(item,amount)??true;
            if(!NativeAccess.SameRun(run)||InventoryChecks.Item(item)!=beforeItem||
                !InventoryChecks.Equal(beforeSource,InventoryChecks.Read(source,run))||!InventoryChecks.Equal(beforeTarget,InventoryChecks.Read(grid,run)))
                return new(0,true,"Indeterminate",Error:"EZAPI_INVENTORY_STALE: predicate changed observed state.");
            if(!allowed)return new(0,false,"Rejected",Error:"EZAPI_LIST_FILTER");
            if(!grid.MayHaveValidInventorySlot(item))return new(0,false,"Rejected",Error:"EZAPI_LIST_ADMISSION");
            RestoreDimensions(grid);
            // 原生TryFind仍校验真实准入；不手改形状来忽略物品规则。
            if(grid.TryFindOneValidInventorySlot(item,false) is null) {
                var shape=grid.inventoryShape;
                var width=shape.globalWidth;var height=shape.globalHeight;
                if(width>=64&&height>=64)return new(0,false,"NoSlot");
                var newWidth=Math.Min(64,Math.Max(width*2,item.modifiedShape.globalWidth));
                var newHeight=Math.Min(64,Math.Max(height*2,item.modifiedShape.globalHeight));
                var owner=grid.GetParentItem()??throw new InvalidOperationException("EZAPI_LIST_OWNER");
                // 扩容不会改变已有物品。两步均读回；失败不自动缩容，以免将已接纳内容排除网格。
                grid.SetShape(newWidth,newHeight);WriteDimensions(owner,newWidth,newHeight);
                if(grid.inventoryShape.globalWidth!=newWidth||grid.inventoryShape.globalHeight!=newHeight)
                    return new(0,true,"Indeterminate",Error:"EZAPI_LIST_RESIZE: native dimensions differ; inspect list.");
            }
            return ItemTransferApi.Move(item,grid,maximum);
        }catch(Exception e){return new(0,true,"Indeterminate",Error:"EZAPI_LIST_OPERATION: "+e.GetType().Name+": "+e.Message);}
    }
    internal static void RestoreDimensions(GameGridInventory grid)
    {
        var owner=grid.GetParentItem();if(owner is null)return;
        var width=StoredTag(owner,WidthTag);var height=StoredTag(owner,HeightTag);
        if(width is null&&height is null)return; // 精确标记，不接管其它Mod普通库存。
        if(width is null||height is null)throw new InvalidOperationException("EZAPI_LIST_DIMENSIONS: incomplete list dimension tags.");
        var w=width.GetInt();var h=height.GetInt();
        if(w<1||h<1||w>64||h>64)throw new InvalidOperationException("EZAPI_LIST_DIMENSIONS: saved dimensions outside1..64.");
        if(grid.inventoryShape.globalWidth==w&&grid.inventoryShape.globalHeight==h)return;
        // 恢复只允许扩展；缩小可能排除已有存档物品，必须由调用者专门迁移。
        if(w<grid.inventoryShape.globalWidth||h<grid.inventoryShape.globalHeight)
            throw new InvalidOperationException("EZAPI_LIST_DIMENSIONS: refusing to shrink a native list during restore.");
        grid.SetShape(w,h);
        if(grid.inventoryShape.globalWidth!=w||grid.inventoryShape.globalHeight!=h)throw new InvalidOperationException("EZAPI_LIST_DIMENSIONS: native shape restore differs.");
    }
    private static void WriteDimensions(GameItem owner,int width,int height)
    {
        owner.state??=new TagSystem();
        owner.state.GetTag(WidthTag).Enable().SetInt(width);
        owner.state.GetTag(HeightTag).Enable().SetInt(height);
        owner.SyncModifiedState();
        if(StoredTag(owner,WidthTag)?.GetInt()!=width||StoredTag(owner,HeightTag)?.GetInt()!=height)
            throw new InvalidOperationException("EZAPI_LIST_DIMENSIONS: native tags did not retain dimensions.");
    }
    // GetTagReadonly 返回的是有效状态，不可用返回对象是否为null判断保存标签是否存在。
    // 列表尺寸是本框架自己保存的元数据，只认state.dict里的精确键，不接管普通容器。
    private static TagState? StoredTag(GameItem owner,string id)
    {
        var dict=owner.state?.dict;
        if(dict is null||!dict.TryGetValue(id,out var tag))return null;
        return tag??throw new InvalidOperationException("EZAPI_LIST_DIMENSIONS: stored list tag is null.");
    }
    private static void ValidateId(string id)
    {
        if(string.IsNullOrWhiteSpace(id)||id.Length>160||id.Any(char.IsControl))throw new ArgumentException("EZAPI_LIST_ID: readable stable ID <=160 required.");
    }
}

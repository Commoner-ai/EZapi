using Il2Cpp;
namespace EZapi;

/// <summary>按需只读库存与整件搬运预检；不生成、拆分、移动物品或保存。只缓存托管快照。</summary>
internal static class DebugInventory
{
    internal static object? LastReport { get; private set; }
    internal static object? LastFreezeReport {get;private set;}
    private static IDisposable? _testLease;
    private static GameRunIdentity? _testRun;
    private static long _testUntil;
    internal static void Clear() {ReleaseTest(false);LastReport=null;LastFreezeReport=null;}
    internal static void Update()
    {
        if(_testLease is not null&&(Environment.TickCount64>=_testUntil||_testRun is null||!NativeAccess.SameRun(_testRun)))ReleaseTest(true);
    }
    internal static string ReleaseTest(bool record=true)
    {
        _testLease?.Dispose();_testLease=null;_testRun=null;
        if(record){LastFreezeReport=new{at=DateTimeOffset.Now,state="released",scope="manual or next main-thread update after5seconds; no save"};EZapiBootstrap.WriteDiagnostics();}
        return "已解除EZapi调试冻结；其它Mod持有的冻结句柄仍由其自己释放。";
    }
    internal static string FreezeFirst(GameRunIdentity? expected)
    {
        if(expected is null||!NativeAccess.SameRun(expected))return "场景或周目已变化，请在店铺重新点击。";
        if(_testLease is not null)return "5秒冻结测试仍在进行，可点“解除测试冻结”。";
        try {
            var grid=CurrentMainGrid(GameSession.Resolve(expected));
            var items=grid?.childItems;
            if(items is null||items.Count==0)return "主库存没有可测试物品。";
            GameItem? first=null;
            for(var i=0;i<Math.Min(items.Count,4096);i++)if(items[i] is {} item&&item.Pointer!=IntPtr.Zero&&item.unitCount>0&&GeneralHelper.IsItemOwned(item)) {first=item;break;}
            if(first is null)return "主库存没有玩家拥有的可测试物品。";
            var lease=InventoryFreezeApi.TryAcquire(new[]{first},"ezapi.debug.freeze");
            if(lease is null)return "冻结未取得：请结束拖动，并导出inventoryGuards检查原生钩子或其它冻结。";
            _testLease=lease;_testRun=expected;_testUntil=Environment.TickCount64+5000;
            LastFreezeReport=new{at=DateTimeOffset.Now,state="held",run=expected,id=first.identifier,instanceId=first.uniqueId,name=first.name,seconds=5};
            EZapiBootstrap.WriteDiagnostics();
            return $"已冻结“{first.name}”5秒（含现有内容）；现在尝试拖出，之后自动释放。";
        } catch(Exception e) {ReleaseTest(false);return "冻结测试失败："+e.GetType().Name+": "+e.Message;}
    }
    internal static string Run(GameRunIdentity? expected)
    {
        if (expected is null || !NativeAccess.SameRun(expected)) return "场景或周目已变化，请在店铺重新点击。";
        try {
            var player = GameSession.Resolve(expected);
            var grid = CurrentMainGrid(player);
            var roots = InventoryApi.CaptureRunItems(player);
            var counts = InventoryApi.CaptureOwnedItemCounts(player);
            var snapshots = new List<NativeInventorySnapshot>();
            var seen = new HashSet<IntPtr>();
            if (grid is not null) { snapshots.Add(InventoryApi.CaptureDirect(grid,true)); seen.Add(grid.Pointer); }
            foreach (var item in roots) {
                var containerGrid=item.contentWindow?.childElement?.TryCast<GameGridInventory>();
                if(containerGrid is not null&&snapshots.Count<16&&seen.Add(containerGrid.Pointer))
                    snapshots.Add(InventoryApi.CaptureDirect(containerGrid,true));
                if (snapshots.Count >= 16) break;
                if(item.parentInventory is {} parent&&seen.Add(parent.Pointer))snapshots.Add(InventoryApi.CaptureDirect(parent,true));
                var children = item.children;
                if (children is null) continue;
                foreach (var child in children) {
                    if (snapshots.Count >= 16) break;
                    var inventory = child?.TryCast<GameInventory>();
                    if (inventory is not null && seen.Add(inventory.Pointer)) snapshots.Add(InventoryApi.CaptureDirect(inventory,true));
                }
            }
            LastReport = new { at = DateTimeOffset.Now, run = expected, dragging = InventoryApi.IsItemDragActive,
                hooks=HookApi.GetRuntimeStatus(),
                itemCount = roots.Count, ownedCounts = counts, directInventories = snapshots,
                // 按需读取现有渲染节点；用原生物品作对照，不重建或强制刷新 UI。
                visibleItemRenders = roots.Take(64).Select(CaptureRender).ToArray(),
                scope = "read only; at most16 direct inventories; no movement, creation, planning or save" };
            EZapiBootstrap.WriteDiagnostics();
            return $"库存只读：{roots.Count} 个物品实例、{counts.Count} 种拥有物品，{snapshots.Count} 个直接库存；详情已导出。";
        } catch (Exception e) {
            LastReport = new { at = DateTimeOffset.Now, run = expected, error = e.GetType().Name + ": " + e.Message };
            EZapiBootstrap.WriteDiagnostics(); return "库存读取失败，已写入 inventoryDebug；请查看诊断。";
        }
    }
    private static object CaptureRender(GameItem item)
    {
        object? Node(TreeNodeRender? node)
        {
            if(node is null)return null;
            var rect=node.rectTransform;var image=node.image;var sprite=image?.sprite;
            return new { active=rect?.gameObject?.activeInHierarchy, position=rect is null?null:new {x=rect.position.x,y=rect.position.y,z=rect.position.z},
                local=rect is null?null:new {x=rect.anchoredPosition.x,y=rect.anchoredPosition.y},
                size=rect is null?null:new {width=rect.rect.width,height=rect.rect.height},
                sprite=sprite is null?null:new {name=sprite.name,width=sprite.rect.width,height=sprite.rect.height,pixelsPerUnit=sprite.pixelsPerUnit},
                alpha=image?.color.a };
        }
        try {
            var element=item.TryCast<GameItemElement>();
            var images=element?.middle?.rectTransform?.gameObject?.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            var imageDetails=new List<object>();
            if(images is not null)foreach(var image in images.Take(8)) {
                var rect=image.rectTransform;var sprite=image.sprite;
                imageDetails.Add(new {name=image.name,active=image.gameObject.activeInHierarchy,alpha=image.color.a,
                    position=new{x=rect.position.x,y=rect.position.y},size=new{width=rect.rect.width,height=rect.rect.height},
                    sprite=sprite is null?null:new{name=sprite.name,width=sprite.rect.width,height=sprite.rect.height,pixelsPerUnit=sprite.pixelsPerUnit}});
            }
            return new {id=item.identifier,instance=item.uniqueId,handler=Node(element?.handler),inner=Node(element?.inner),middle=Node(element?.middle),rotator=Node(element?.rotator),images=imageDetails};
        }catch(Exception e){return new {id=item.identifier,error=e.GetType().Name+": "+e.Message};}
    }
    internal static string ExportFirstGraph(GameRunIdentity? expected)
    {
        if(expected is null||!NativeAccess.SameRun(expected))return "场景或周目已变化，请在店铺重新点击。";
        try {
            var grid=CurrentMainGrid(GameSession.Resolve(expected));
            GameItem? item=null;var children=grid?.childItems;
            if(children is not null)for(var i=0;i<Math.Min(children.Count,4096);i++)if(children[i] is {} candidate&&candidate.Pointer!=IntPtr.Zero&&candidate.unitCount>0&&GeneralHelper.IsItemOwned(candidate)){item=candidate;break;}
            if(item is null)return "主库存没有玩家拥有的可导出物品。";
            var json=SaveGraphApi.Encode(item);
            var folder=Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory,"EZapi","save-graph");Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,$"graph-{expected.SaveSlotId}-{expected.Epoch}-{item.uniqueId}.json");File.WriteAllText(path,json);
            LastReport=new{at=DateTimeOffset.Now,run=expected,exported=item.identifier,instanceId=item.uniqueId,bytes=System.Text.Encoding.UTF8.GetByteCount(json),path,scope="read-only native SaveState graph; no decode/no save"};
            EZapiBootstrap.WriteDiagnostics();return $"已导出首件物品图（{json.Length}字符）：{path}";
        }catch(Exception e){LastReport=new{at=DateTimeOffset.Now,run=expected,error=e.GetType().Name+": "+e.Message};EZapiBootstrap.WriteDiagnostics();return "物品图导出失败，已写入 inventoryDebug。";}
    }
    // 店铺可见主库存不是 PlayerStore.gridInv；每次重新解析，不跨场景保存引用。
    private static GameInventory? CurrentMainGrid(PlayerStore player)=>
        VisibleStoreInventory.TryGet(out var visible)?visible:player.gridInv;
}

using Il2Cpp;
namespace EZapi;

internal static class DebugItemStock
{
    private static bool _enabled;
    internal static object? LastReport{get;private set;}
    internal static void Clear(){LastReport=null;_enabled=false;}
    internal static void Register()
    {
        foreach(var suffix in new[]{"repeat","unique","unavailable","throws"}) {
            var id="ezapi.debug.stock_"+suffix;
            var definition=new ItemDefinition {Id=id,Name="EZapi补货临时物品",Value=17};
            LegacySpriteAdapter.Commit(definition,LegacySpriteAdapter.Prepare(definition,null));
            var result=ItemApi.RegisterItem("ezapi.debug",id,()=>NativeItemFactory.Create(definition),
                new(){NightShop=suffix=="unique"?NightShopStockPolicy.Unique:NightShopStockPolicy.Repeatable,
                    IsNightShopAvailable=()=>_enabled && (suffix=="throws"?throw new InvalidOperationException("intentional stock predicate fixture"):suffix!="unavailable")});
            if(!result.Accepted)throw new InvalidOperationException(result.Reason);
        }
    }
    internal static string Run()
    {
        var run=NativeAccess.RequireRun();
        var list=new Il2CppSystem.Collections.Generic.List<GameItem>();var checks=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        var table=EmporiumEntry.Instance?.frontInvinvElement;
        var before=table?.childItems.ToArray().Select(i=>i.Pointer).ToHashSet()??new HashSet<IntPtr>();
        const string prefix="ezapi.debug.stock_";
        GameItem[] Goods()=>table!.childItems.ToArray().Where(i=>i.identifier.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
        try {
            if(run.SaveSlotId!=534 || run.RunId!="e39e848c-36b7-40e9-ab7e-b6adcb9881b8" || table is null || InventoryApi.IsItemDragActive)
                throw new InvalidOperationException("Exact disposable test slot/run, native sale table and no drag required.");
            if(Goods().Length!=0)throw new InvalidOperationException("Remove unfinished prior debug stock before testing.");
            var ownedUnique=GameSession.Resolve(run).FindAllItem(true).ToArray().Any(i=>i.identifier==prefix+"unique"&&GeneralHelper.IsItemOwned(i));
            var expectedStock=ownedUnique?1:2;
            Check(ItemStockRuntime.IsAvailable,"exact scope hook and official late event installed");
            _enabled=true;
            ModHook.FireOnPlaceInventorInventoryItemLate(list);
            Check(Goods().Length==0,"unscoped official event does not stock native sale table");
            ItemStockRuntime.ProbeEvent(list,true);
            Check(Goods().Length==0,"visiting inventor scope does not add night stock");
            ItemStockRuntime.ProbeEvent(list,false);
            Check(Goods().Length==expectedStock,"repeatable supplied; unique respects current owned state in native sale table");
            Check(list.Count==0,"official owned snapshot remains unchanged after supply");
            Check(Goods().All(i=>i.parentInventory?.Pointer==table.Pointer && !GeneralHelper.IsItemOwned(i)),"sale goods have real parent inventory and remain unowned");
            var pointers=Goods().Select(i=>i.Pointer).ToArray();
            ItemStockRuntime.ProbeEvent(list,false);
            Check(Goods().Select(i=>i.Pointer).SequenceEqual(pointers),"repeated callback does not duplicate actual sale goods");
            Check(ItemApi.StockSnapshot().Single(v=>v.ItemId==prefix+"throws").LastError?.Contains("intentional")==true,"predicate failure isolated and reported");
            Check(ItemApi.StockSnapshot().Single(v=>v.ItemId==prefix+"unavailable").Added==0,"false predicate prevents factory creation");
            Check(Goods().All(i=>ItemApi.StockSnapshot().Single(v=>v.ItemId==i.identifier).Added>0),"Added counters require actual sale inventory readback");
            var unique=Goods().SingleOrDefault(i=>i.identifier==prefix+"unique");
            if(unique is not null)GeneralHelper.SetItemOwned(unique,true);
            ItemStockRuntime.ProbeEvent(list,false);
            Check(Goods().Length==expectedStock && (unique is null?ownedUnique:GeneralHelper.IsItemOwned(unique)),"current owned unique blocks repeat supply without touching prior purchased items");
            if(unique is not null)GeneralHelper.SetItemOwned(unique,false);
            _enabled=false;
            foreach(var item in Goods()) {
                if(before.Contains(item.Pointer))throw new InvalidOperationException("Refusing to remove preexisting sale item.");
                if(item.parentInventory is {} parent && !parent.Expel(item))throw new InvalidOperationException("Native debug stock expel failed.");
                item.Destroy();
            }
            ItemStockRuntime.ProbeEvent(list,false);Check(Goods().Length==0,"disabled fixture has no normal game supply");
            Check(table.childItems.ToArray().Select(i=>i.Pointer).ToHashSet().SetEquals(before),"all preexisting sale items retained and fixture removed");
        } catch(Exception ex){Check(false,"execution: "+ex);}
        finally {
            _enabled=false;
            if(NativeAccess.SameRun(run) && table is not null)foreach(var item in Goods())try {
                if(before.Contains(item.Pointer))continue;
                if(item.parentInventory is {} parent && !parent.Expel(item))throw new InvalidOperationException("Native debug stock cleanup expel failed.");
                item.Destroy();
            }catch(Exception ex){Check(false,"cleanup: "+ex.Message);}
        }
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,scope="official Late event within isolated night scope calls actual native sale placement and verifies parent/ownership/dedup; owned snapshot unchanged and fixture removed; full native generation, payment and disk persistence not covered"};
        EZapiBootstrap.WriteDiagnostics();return $"补货检查：{passed}通过，{failed}失败。";
    }
}

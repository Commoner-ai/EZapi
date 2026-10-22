using Il2Cpp;
using Il2CppInterop.Runtime;
namespace EZapi;
internal static class DebugNpcStock
{
    private const string Id="ezapi.debug.npc_stock";
    private const string FixedId="ezapi.debug.npc_fixed";
    internal static bool Running{get;private set;}
    internal static object? LastReport{get;private set;}
    internal static void Clear(){LastReport=null;Running=false;}
    internal static void Register()
    {
        var d=new ItemDefinition{Id=Id,Name="EZapi NPC货源检查",Value=25};
        LegacySpriteAdapter.Commit(d,LegacySpriteAdapter.Prepare(d,null));
        var r=ItemApi.RegisterItem("ezapi.debug",Id,()=>NativeItemFactory.Create(d),new(){NpcTrade=new(NpcTradeStockCategory.Material|NpcTradeStockCategory.Ore,1000000){Suppliers=NpcTradeStockSupplier.Inventor,MinerWeight=1000000,PreserveLootTableWeight=false}});
        if(!r.Accepted)throw new InvalidOperationException(r.Reason);
        var f=new ItemDefinition{Id=FixedId,Name="EZapi 分类货源临时物品",Value=25};
        LegacySpriteAdapter.Commit(f,LegacySpriteAdapter.Prepare(f,null));
        r=ItemApi.RegisterItem("ezapi.debug",FixedId,()=>NativeItemFactory.Create(f),new(){NpcTrade=new(NpcTradeStockCategory.Machine|NpcTradeStockCategory.Food|NpcTradeStockCategory.Medical|NpcTradeStockCategory.Household,1000000)});
        if(!r.Accepted)throw new InvalidOperationException(r.Reason);
    }
    internal static string Run()
    {
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        Il2CppSystem.Action<LootRollContext>? handler=null;
        var events=new List<(string Table,string Group,string Id)>();bool rewrite=false,reenter=false,reentryRejected=false;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try {
            NativeAccess.RequireRun();
            Running=true;
            Check(NpcStockRuntime.IsAvailable,"all eleven exact NPC scope/roll/spawn/sale hooks installed");
            var household=Items.Create("water_filter_adv",false);
            try {Check(household.IsGameItemType("HOUSEHOLD_GOOD")&&NpcStockRuntime.Classify(household)==NpcTradeStockCategory.Household,"actual native household type HOUSEHOLD_GOOD participates in fixed stock");}
            finally {household.Destroy();}
            var before=LootTables.GetSnapshot("materialTable");
            var inventor=NpcStockApi.BuildPool("materialTable",NpcTradeStockCategory.Material,NpcTradeStockSupplier.Inventor);
            Check(inventor.Entries.Single(e=>e.ItemId==Id).Weight==1000000,"applied DLL factory contributes requested weight");
            var thief=NpcStockApi.BuildPool("materialTable",NpcTradeStockCategory.Material,NpcTradeStockSupplier.Thief);
            Check(thief.Entries.All(e=>e.ItemId!=Id||e.Weight==0),"supplier filter excludes inventor-only item from thief pool");
            var miner=NpcStockApi.BuildPool("materialTable",NpcTradeStockCategory.Material,NpcTradeStockSupplier.Any,miner:true);
            Check(miner.Entries.Single(e=>e.ItemId==Id).Weight==1000000,"miner uses explicit independent weight");
            handler=DelegateSupport.ConvertDelegate<Il2CppSystem.Action<LootRollContext>>((Action<LootRollContext>)(context=>{
                events.Add((context.tableId,context.groupId,context.itemId));
                if(rewrite)context.itemId="ezapi.debug.npc_event_override";
                if(reenter)try{NpcStockApi.TryRoll("materialTable",NpcTradeStockCategory.Material,NpcTradeStockSupplier.Inventor,out _);}
                    catch(InvalidOperationException ex)when(ex.Message.StartsWith("EZAPI_NPC_STOCK_REENTRY:",StringComparison.Ordinal)){reentryRejected=true;}
            }))??throw new InvalidOperationException("loot event callback conversion failed");
            ModHook.add_OnLootRolled(handler);
            var selected=0;
            for(var i=0;i<16;i++) {
                Check(NpcStockApi.TryRoll("materialTable",NpcTradeStockCategory.Material,NpcTradeStockSupplier.Inventor,out var id),"native ID roll succeeds "+i);
                if(id==Id)selected++;
            }
            Check(selected>0,"native RNG actually selects added custom ID");
            var autoSelected=0;
            for(var i=0;i<16;i++)if(NpcStockRuntime.Probe("materialTable",NpcTradeStockSupplier.Inventor)==Id)autoSelected++;
            Check(autoSelected>0,"patched native Roll automatically routes within inventor scope");
            var outside=LootRegistry.Roll("materialTable");
            Check(outside!=Id,"unscoped native roll uses original table without custom candidate");
            Check(events.Count==33,"exactly one official event per explicit/scoped/unscoped native roll");
            Check(events.All(e=>e.Table=="materialTable"&&string.IsNullOrEmpty(e.Group)),"native event retains table and empty direct-roll group");
            var count=events.Count;
            NpcStockRuntime.Probe("materialTable",NpcTradeStockSupplier.Inventor,groupId:"ezapi.debug.npc_group");
            Check(events.Count==count+1&&events[^1].Group=="ezapi.debug.npc_group","patched route publishes once and preserves native group ID");
            rewrite=true;
            Check(NpcStockApi.TryRoll("materialTable",NpcTradeStockCategory.Material,NpcTradeStockSupplier.Inventor,out var rewritten)&&rewritten=="ezapi.debug.npc_event_override","official loot callback can replace selected ID");
            rewrite=false;reenter=true;
            count=events.Count;
            Check(NpcStockApi.TryRoll("materialTable",NpcTradeStockCategory.Material,NpcTradeStockSupplier.Inventor,out _)&&reentryRejected&&events.Count==count+1,"nested callback roll rejected without another RNG/event");
            reenter=false;
            count=events.Count;
            Check(NpcStockApi.TryRoll("materialTable",NpcTradeStockCategory.Material,NpcTradeStockSupplier.Inventor,out _)&&events.Count==count+1,"roll guard released after callback reentry rejection");
            count=events.Count;
            var minerIds=new List<string>();
            for(var i=0;i<4;i++)minerIds.AddRange(NpcStockRuntime.ProbeMinerStock());
            Check(minerIds.Count>0&&minerIds.Contains(Id),"real native miner stock callback selects and creates custom Ore candidate");
            Check(events.Count==count+minerIds.Count&&events.Skip(count).All(e=>e.Table=="ezapi.npc.miner"),"one official event for each intercepted native miner slot");
            var playerHasFixture=false;foreach(var item in GameSession.Resolve(GameSession.Identity!).FindAllItem(true))if(item.identifier==Id)playerHasFixture=true;
            Check(!playerHasFixture,"isolated native miner capture never delivers fixture to player");
            details.Add(new{minerIds});
            var fixedRows=new List<object>();
            foreach(var kind in new[]{"inventor-night","inventor-visit","thief","petty-thief","food-thief"}) {
                var rows=NpcStockRuntime.ProbeFixedStock(kind);
                fixedRows.Add(new{kind,rows});
                Check(rows.Count>0,"native stock callback produced captured sale slots: "+kind);
            }
            var fixedJson=System.Text.Json.JsonSerializer.Serialize(fixedRows);
            using(var doc=System.Text.Json.JsonDocument.Parse(fixedJson)) {
                foreach(var kind in new[]{"inventor-night","inventor-visit","thief","food-thief"}) {
                    var rows=doc.RootElement.EnumerateArray().Single(x=>x.GetProperty("kind").GetString()==kind).GetProperty("rows").EnumerateArray().ToArray();
                    // 原生小偷可能只抽到钥匙卡/违禁品/化学品。此时保留原货物是正确行为，不得强制替换。
                    var eligible=rows.Any(x=>!x.GetProperty("tableItem").GetBoolean()&&x.GetProperty("category").GetString() is "Machine" or "Food" or "Medical" or "Household");
                    Check(eligible?rows.Any(x=>x.GetProperty("routed").GetBoolean()&&x.GetProperty("selected").GetString()==FixedId):
                        rows.All(x=>!x.GetProperty("routed").GetBoolean()&&x.GetProperty("selected").GetString()==x.GetProperty("original").GetString()),
                        "actual fixed stock routes eligible typed slots and preserves special-only stock: "+kind);
                    if(kind.StartsWith("inventor",StringComparison.Ordinal))Check(rows.Any(x=>x.GetProperty("category").GetString()=="Machine"&&x.GetProperty("routed").GetBoolean()),"native prebuilt machine slot participates: "+kind);
                }
                var all=doc.RootElement.EnumerateArray().SelectMany(x=>x.GetProperty("rows").EnumerateArray()).ToArray();
                Check(all.Where(x=>x.GetProperty("tableItem").GetBoolean()).All(x=>!x.GetProperty("routed").GetBoolean()),"named-table spawned results are not rolled again at native sale");
            }
            details.Add(new{fixedRows});
            var fixedDelivered=false;foreach(var item in GameSession.Resolve(GameSession.Identity!).FindAllItem(true))if(item.identifier==FixedId)fixedDelivered=true;
            Check(!fixedDelivered,"fixed stock capture never delivers new fixture to player");
            var after=LootTables.GetSnapshot("materialTable");
            Check(before.TotalWeight==after.TotalWeight && before.Entries.SequenceEqual(after.Entries),"all native global table entries remain unchanged");
            Check(!after.Entries.Any(e=>e.Id==Id),"custom candidate never inserted in global loot journal");
            var tableIds=new List<string>();foreach(var key in LootRegistry.tables.Keys)tableIds.Add(key);
            details.Add(new{baseline=before.Entries.Count,pool=inventor.Entries.Count,selected,tableIds});
        } catch(Exception ex){Check(false,"execution: "+ex);}
        finally{
            if(handler is not null)try{ModHook.remove_OnLootRolled(handler);}catch(Exception ex){Check(false,"loot event unsubscribe: "+ex.Message);}
            Running=false;
        }
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details,scope="explicit/scoped rolls and actual native miner/inventor/thief generation with captured temporary sale items; includes fixed typed and prebuilt Machine routing; no dialogue selection, real shelf placement, payment or save"};
        EZapiBootstrap.WriteDiagnostics();return $"NPC货源：{passed}通过，{failed}失败。";
    }
}

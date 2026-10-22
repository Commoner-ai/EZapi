using Il2Cpp;
using MelonLoader;
namespace EZapi;

internal static class DebugMachineAutomation
{
    internal static object? LastReport {get;private set;}
    internal static void Clear()=>LastReport=null;
    private const string Id="ezapi.debug.machine_layout_probe";
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.IsReady)return "请进入测试档店铺并放下拖拽物品。";
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        foreach(var mode in new[]{"normal","select","conflict","block","missing","nomatch","order","dispose","subscribe","nested","before-error","after-error","stale","mutate","mutate-tag","mutate-machine-tag"}) {
            var made=new List<GameItem>();var destroyed=new HashSet<IntPtr>();var leases=new List<IDisposable>();MachineAutomationContext? saved=null;
            try {
                var m=ItemApi.Create(Id,true);made.Add(m);MachineLayoutApi.TryGetInventory(m,out var slots);
                var input=ItemApi.Create("ezapi.debug.factory_item",true);made.Add(input);var inputPointer=input.Pointer;
                var marker=slots.ItemInput.TryFindOneValidInventorySlot(input);if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("automation fixture input");
                var first=new MachineRecipe("ezapi.debug.automation_first",new[]{new MachineIngredient(mode=="nomatch"?"ezapi.debug.missing":"ezapi.debug.factory_item")},new("ezapi.debug.factory_item"));
                var second=first with {RecipeId="ezapi.debug.automation_second",Output=new("ezapi.debug.factory_item",2)};
                var processor=MachineRecipeApi.CreateBatchProcessor("ezapi.debug",Id,new[]{first,second},new(0));
                var order=new List<string>();var before=0;var after=0;var late=0;bool afterControl=false,readResult=false;MachineBatchResult? nested=null;IDisposable? disposed=null;
                void Sub(string owner,MachineAutomationPhase phase,Action<MachineAutomationContext> cb,int priority=0)=>leases.Add(MachineAutomationApi.Subscribe(owner,Id,phase,cb,priority));
                Sub("ezapi.automation",MachineAutomationPhase.BeforeBatch,ctx=>{
                    before++;saved=ctx;order.Add("first");
                    if(mode=="select")ctx.SelectRecipe(second.RecipeId);
                    if(mode=="conflict")ctx.SelectRecipe(first.RecipeId);
                    if(mode=="block")ctx.Block();
                    if(mode=="missing")ctx.SelectRecipe("ezapi.debug.no_such_recipe");
                    if(mode=="dispose")disposed?.Dispose();
                    if(mode=="subscribe")Sub("ezapi.late",MachineAutomationPhase.BeforeBatch,_=>late++);
                    if(mode=="nested")nested=processor.Process(m);
                    if(mode=="before-error")throw new InvalidOperationException("expected native before automation error");
                    if(mode=="mutate")input.SetValue(input.unitValue+1);if(mode=="mutate-tag"){input.state.GetTag("EZAPI_DEBUG_AUTOMATION_READONLY").Enable().SetString("changed");input.SyncModifiedState();}if(mode=="mutate-machine-tag"){m.state.GetTag("EZAPI_DEBUG_AUTOMATION_READONLY").Enable().SetString("changed");m.SyncModifiedState();}
                },-10);
                Sub("ezapi.second",MachineAutomationPhase.BeforeBatch,ctx=>{order.Add("second");if(mode=="conflict")ctx.SelectRecipe(second.RecipeId);},0);
                if(mode=="dispose"){disposed=MachineAutomationApi.Subscribe("ezapi.disposed",Id,MachineAutomationPhase.BeforeBatch,_=>late++);leases.Add(disposed);}
                Sub("ezapi.automation",MachineAutomationPhase.AfterBatch,ctx=>{
                    after++;readResult=ctx.BatchResult?.Committed==true&&ctx.Result?.StartsWith("produced:",StringComparison.Ordinal)==true&&ctx.RecipeId==(mode=="select"?second.RecipeId:first.RecipeId);
                    try{ctx.Block();}catch(InvalidOperationException){afterControl=true;}
                    if(mode=="after-error")throw new InvalidOperationException("expected native after automation error");
                });
                var result=processor.Process(m);var success=mode is not ("conflict" or "block" or "missing" or "nomatch" or "before-error" or "mutate" or "mutate-tag" or "mutate-machine-tag");
                if(result.Committed)destroyed.Add(inputPointer);
                details.Add(new{mode,result,before,after,late,order,nested});
                Check(result.Committed==success,mode+": committed state");
                Check(before==1,mode+": exactly one BeforeBatch");
                Check(after==(success?1:0),mode+": AfterBatch only for executed transaction");
                Check(slots.Output.childItems.Count==(success?(mode=="select"?2:1):0),mode+": actual output count");
                Check(slots.ItemInput.childItems.Count==(success?0:1),mode+": actual material consumption or untouched input");
                Check(saved?.IsCurrent==false,mode+": context closed after batch");
                if(success)Check(readResult&&afterControl,mode+": after receipt readable and controls rejected");
                if(mode=="select")Check(result.RecipeId==second.RecipeId,"selection: second recipe chosen before first eligible one");
                if(mode=="conflict"||mode=="block")Check(result.Error=="EZAPI_MACHINE_AUTOMATION_BLOCKED",mode+": one-way block is enforced");
                if(mode=="missing"||mode=="nomatch")Check(result.Error?.StartsWith("EZAPI_RECIPE_NO_MATCH",StringComparison.Ordinal)==true,mode+": no fallback for unavailable selected recipe");
                if(mode=="nested")Check(nested?.Error=="EZAPI_RECIPE_REENTRANT_OR_STALE","nested: batch lease blocks recursion");
                if(mode=="dispose"||mode=="subscribe")Check(late==0,mode+": snapshot dispatch respects disposal and defers new subscriber");
                if(mode=="order")Check(order.SequenceEqual(new[]{"first","second"}),"order: ascending explicit priority");
                if(mode=="stale") {bool denied=false;try{saved!.SelectRecipe(first.RecipeId);}catch(InvalidOperationException){denied=true;}Check(denied,"stale: retained context cannot control later batch");}
                if(mode=="before-error") {
                    var retry=processor.Process(m);if(retry.Committed)destroyed.Add(inputPointer);
                    Check(retry.Committed&&before==1,"before error: failing subscriber disabled, explicit later attempt succeeds once");
                }
                if(mode=="after-error")Check(result.Transaction?.CompletionError is null&&result.Committed,"after error: notification failure cannot erase committed receipt");
                if(mode is "mutate" or "mutate-tag" or "mutate-machine-tag")Check(result.Error?.StartsWith("EZAPI_MACHINE_AUTOMATION_STALE",StringComparison.Ordinal)==true,"mutation: resource change blocks before consumption");
                Check(!InventoryFreezeApi.IsFrozen(m),mode+": no freeze leaks");
            }catch(Exception e){Check(false,mode+": "+e);MelonLogger.Error("[EZapi] Automation probe: "+e);}
            finally {
                foreach(var lease in leases)lease.Dispose();
                foreach(var item in made.AsEnumerable().Reverse()){if(destroyed.Contains(item.Pointer))continue;if(item.parentInventory is {} parent&&!parent.Expel(item))throw new InvalidOperationException("automation fixture expel");item.Destroy();}
            }
        }
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details,scope="temporary native before/after automation, conflict/block/stale/mutation and real default transactions; no player delivery/save"};
        EZapiBootstrap.WriteDiagnostics();return $"机器自动化检查：{passed}通过，{failed}失败，已导出。";
    }
}

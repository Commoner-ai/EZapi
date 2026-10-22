using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>显式测试档夹具：使用真实现金和临时源/目标库存，完成后归还确切测试差额，不保存。</summary>
internal static class DebugRunPurchase
{
    internal static object? LastReport{get;private set;}
    internal static void Clear()=>LastReport=null;
    internal static string Run()
    {
        if(!VisibleStoreInventory.IsReady||!GameSession.TryResolve(null,out var player)||InventoryApi.IsItemDragActive)return "请进入测试档并放下拖拽物品。";
        var identity=GameSession.Identity!;var initial=player.GetCash();
        if(initial<7||initial==int.MaxValue)return "测试档现金需要7..2147483646。";
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try {
            foreach(var mode in new[]{"reject","preflight","commit","zero","partial","throw","recheck","restore-false","cash-interference","nested","cash-only"}) {
                var before=player.GetCash();var cost=mode=="reject"?before+1:mode=="zero"?0:7;
                GameItem? machine=null,item=null;var apply=0;var restore=0;var queries=0;bool balanceKnown=false;int expected=before;
                try {
                    machine=ItemApi.Create("ezapi.debug.machine_layout_probe",true);item=ItemApi.Create("ezapi.debug.factory_item",true);
                    if(!MachineLayoutApi.TryGetInventory(machine,out var slots))throw new InvalidOperationException("purchase fixture layout");
                    var source=slots.ItemInput.TryCast<GameGridInventory>()!;var target=slots.Output.TryCast<GameGridInventory>()!;
                    var marker=source.TryFindOneValidInventorySlot(item);
                    if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1||item.parentInventory?.Pointer!=source.Pointer)throw new InvalidOperationException("purchase source admission");
                    var id=item.uniqueId;var pointer=item.Pointer;
                    var steps=mode=="cash-only"?Array.Empty<TransactionStep>():new[]{new TransactionStep("delivery",()=>{
                        queries++;return mode!="preflight"&&(mode!="recheck"||queries==1)&&item.parentInventory?.Pointer==source.Pointer;
                    },()=>{
                        apply++;
                        if(mode=="nested")Check(RunPurchaseApi.Purchase(1,Array.Empty<TransactionStep>()).Status==TransactionStatus.Reentrant,"nested: second payment denied");
                        var moved=ItemTransferApi.Move(item,target,1);
                        if(moved.Moved!=1||moved.Indeterminate||item.parentInventory?.Pointer!=target.Pointer)return false;
                        if(mode=="cash-interference")player.ModCash(1,false);
                        if(mode=="throw")throw new InvalidOperationException("expected partial purchase delivery");
                        return mode is not ("partial" or "restore-false");
                    },()=>{
                        restore++;
                        if(mode=="restore-false")return false;
                        if(item.parentInventory?.Pointer==source.Pointer)return true;
                        var moved=ItemTransferApi.Move(item,source,1);
                        return moved.Moved==1&&!moved.Indeterminate&&item.parentInventory?.Pointer==source.Pointer;
                    })};
                    var result=RunPurchaseApi.Purchase(player,cost,steps);
                    var committed=mode is "commit"or"zero"or"nested"or"cash-only";
                    var partial=mode is "partial"or"throw"or"recheck";
                    var status=committed?TransactionStatus.Committed:partial?TransactionStatus.Restored:mode is "restore-false"or"cash-interference"?TransactionStatus.RecoveryFailed:TransactionStatus.Rejected;
                    expected=before-(committed?cost:mode is "restore-false"or"cash-interference"?7:0)+(mode=="cash-interference"?1:0);
                    var after=player.GetCash();balanceKnown=after==expected;
                    var targetOwns=mode is "commit"or"zero"or"nested"or"restore-false";
                    Check(result.Status==status,mode+": exact transaction status");
                    Check(balanceKnown&&player.playerCash==after,mode+": exact native cash readback");
                    Check(item.parentInventory?.Pointer==(targetOwns?target:source).Pointer&&source.childItems.Count==(targetOwns?0:1)&&target.childItems.Count==(targetOwns?1:0),mode+": real source target delivery graph");
                    Check(item.uniqueId==id&&item.Pointer==pointer&&item.unitCount==1&&GeneralHelper.IsItemOwned(item),mode+": same owned item identity preserved");
                    var attempted=mode is not ("reject"or"preflight"or"recheck"or"cash-only");
                    Check(apply==(attempted?1:0)&&restore==(mode is "partial"or"throw"or"restore-false"or"cash-interference"?1:0),mode+": bounded business apply and restore counts");
                    Check(result.Committed==committed&&result.Restored==(status is TransactionStatus.Rejected or TransactionStatus.Restored),mode+": receipt does not promise uncertain recovery");
                    details.Add(new{mode,cost,before,after,result,apply,restore,queries,sourceItems=source.childItems.Count,targetItems=target.childItems.Count,itemId=id});
                }finally {
                    foreach(var made in new[]{item,machine})if(made is not null) {
                        if(made.parentInventory is { } parent&&!parent.Expel(made))throw new InvalidOperationException("purchase fixture cleanup expel");made.Destroy();
                    }
                    // 仅测试夹具归还自己刚核对的现金差额；产品API没有此自动补偿绕过。
                    if(GameSession.Identity==identity&&balanceKnown&&player.GetCash()==expected&&before!=expected)player.ModCash(before-expected,false);
                }
                Check(GameSession.Identity==identity&&player.GetCash()==before,mode+": fixture restores original cash after recorded result");
            }
            Check(player.GetCash()==initial,"all purchase cases leave original cash unchanged");
        }catch(Exception e){Check(false,"execution: "+e);MelonLogger.Error("[EZapi] Purchase fixture: "+e);}
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details,identity,cashBefore=initial,cashAfter=player.GetCash(),
            scope="real PlayerStore.GetCash/PayMoney/ModCash and temporary native inventory transfer; explicit test-only balance cleanup; no player inventory delivery or automatic save"};
        EZapiBootstrap.WriteDiagnostics();return $"购买检查：{passed}通过，{failed}失败。";
    }
}

using System.Text.Json;
using EZapi;
using Il2Cpp;
var cases=new List<object>();int failed=0,goods=0,checks=0;var order=new List<string>();
void Check(bool ok,string name){cases.Add(new{name,passed=ok});if(!ok){failed++;Console.Error.WriteLine(name);}}
void Reject(Action action,string name){try{action();Check(false,name);}catch(ArgumentException){Check(true,name);}}
PlayerStore Fresh(int balance=100){GameSession.Identity=new(532,"test",1);GameSession.Player=new(){playerCash=balance};goods=checks=0;order.Clear();return GameSession.Player;}
TransactionStep Goods(string name="goods",Func<bool>? apply=null,Func<bool>? restore=null,Func<bool>? check=null)=>new(name,check??(()=>true),apply??(()=>{goods++;return true;}),restore??(()=>{order.Add(name);goods--;return true;}));
TransactionResult Buy(int cost,params TransactionStep[] steps)=>RunPurchaseApi.Purchase(cost,steps);
var p=Fresh();var result=Buy(7,Goods());Check(result.Committed&&p.playerCash==93&&goods==1&&p.Debits==1&&p.Refunds==0,"payment and delivery commit once");
Check(result.AppliedSteps.SequenceEqual(new[]{"ezapi.purchase.cash","goods","ezapi.purchase.verify"}),"receipt includes payment delivery final readback");
p=Fresh();result=Buy(0,Goods());Check(result.Committed&&goods==1&&p.playerCash==100&&p.Debits==0,"zero cost delivery avoids native debit");
p=Fresh();result=Buy(7);Check(result.Committed&&p.playerCash==93,"cash-only purchase accepted");
p=Fresh();result=Buy(101,Goods());Check(result.Status==TransactionStatus.Rejected&&goods==0&&p.playerCash==100&&p.Debits==0,"insufficient cash before author checks");
p=Fresh(-1);result=Buy(0,Goods());Check(result.Status==TransactionStatus.Rejected&&p.Debits==0,"negative native balance rejected");
p=Fresh(int.MaxValue);result=Buy(int.MaxValue,Goods());Check(result.Committed&&p.playerCash==0,"maximum integer cost has exact arithmetic");
p=Fresh();result=Buy(100,Goods());Check(result.Committed&&p.playerCash==0,"exact balance accepted");
foreach(var throws in new[]{false,true}){
 p=Fresh();var restores=0;
 result=Buy(7,Goods(apply:()=>{goods=1;if(throws)throw new Exception("partial delivery");return false;},restore:()=>{restores++;goods=0;return true;}));
 Check(result.Status==TransactionStatus.Restored&&p.playerCash==100&&p.Debits==p.Refunds&&p.Refunds==1&&goods==0&&restores==1,"partial delivery restored including current step: "+throws);
}
p=Fresh();result=Buy(7,Goods("first"),Goods("second",()=>{goods++;return false;}));Check(result.Restored&&p.playerCash==100&&goods==0&&order.SequenceEqual(new[]{"second","first"}),"reverse delivery restores before cash refund");
foreach(var throws in new[]{false,true}){
 p=Fresh();result=Buy(7,Goods(apply:()=>{goods=1;return false;},restore:()=>{if(throws)throw new Exception("restore");return false;}));
 Check(result.Status==TransactionStatus.RecoveryFailed&&!result.Restored&&p.playerCash==93&&p.Refunds==0&&goods==1&&result.RecoveryFailures.Count==2,"uncertain goods restore withholds cash refund: "+throws);
}
p=Fresh();result=Buy(7,Goods(check:()=>{checks++;return false;}));Check(result.Status==TransactionStatus.Rejected&&checks==1&&p.Debits==0&&goods==0,"qualification rejects before cash write");
p=Fresh();result=Buy(7,Goods(check:()=>++checks==1));Check(result.Status==TransactionStatus.Restored&&p.playerCash==100&&goods==0&&p.Refunds==1,"qualification rechecked after debit");
p=Fresh();result=Buy(7,Goods(check:()=>throw new Exception("qualification")));Check(result.Status==TransactionStatus.Rejected&&p.Debits==0&&result.Error!.Contains("qualification"),"qualification exception before mutation");
p=Fresh();result=Buy(7,Goods(apply:()=>{Check(Buy(1,Goods()).Status==TransactionStatus.Reentrant,"nested purchase rejected");goods++;return true;}));Check(result.Committed&&p.playerCash==93&&goods==1&&p.Debits==1,"outer purchase remains valid after nested rejection");
p=Fresh();result=Transactions.Run(new[]{Goods(apply:()=>Buy(7,Goods()).Status==TransactionStatus.Reentrant)});Check(result.Committed&&p.Debits==0,"purchase inside generic transaction rejected before debit");
p=Fresh();result=Buy(7,Goods(apply:()=>Transactions.Run(new[]{Goods()}).Status==TransactionStatus.Reentrant));Check(result.Committed&&p.Debits==1,"generic transaction inside purchase rejected");
foreach(var stage in new[]{"check","apply","restore"}){
 p=Fresh();var old=p;var restored=0;
 bool Switch(){GameSession.Identity=new(533,"other",2);GameSession.Player=new(){Pointer=new(2),playerCash=1000};return true;}
 result=Buy(7,Goods(check:()=>{if(stage=="check")Switch();return true;},apply:()=>{goods=1;if(stage=="apply")Switch();return stage!="restore";},restore:()=>{restored++;if(stage=="restore")Switch();goods=0;return true;}));
 Check(result.Status==TransactionStatus.RecoveryFailed&&GameSession.Player!.playerCash==1000&&old.Refunds==0,"changed epoch never refunds new or stale store: "+stage);
 if(stage=="apply")Check(restored==0,"stale business restore callback never invoked");
}
foreach(var stage in new[]{"check","apply","restore"}){
 p=Fresh();result=Buy(7,Goods(check:()=>{if(stage=="check")p.playerCash++;return true;},apply:()=>{goods=1;if(stage=="apply")p.playerCash++;return stage!="restore";},restore:()=>{goods=0;if(stage=="restore")p.playerCash++;return true;}));
 Check(result.Status==TransactionStatus.RecoveryFailed&&p.Refunds==0,"author cash interference never overwritten: "+stage);
}
p=Fresh();var list=new List<TransactionStep>();list.Add(Goods(check:()=>{list.Clear();return true;}));result=RunPurchaseApi.Purchase(p,7,list);Check(result.Committed&&goods==1,"author list mutation cannot change captured steps");
p=Fresh();result=RunPurchaseApi.Purchase(new(){Pointer=new(55)},7,new[]{Goods()});Check(result.Status==TransactionStatus.Rejected&&p.Debits==0,"foreign store pointer rejected");
p=Fresh();GameSession.Player=null;result=RunPurchaseApi.Purchase(p,7,new[]{Goods()});Check(result.Status==TransactionStatus.Rejected&&p.Debits==0,"explicit store rejected outside current run");
try{RunPurchaseApi.Purchase(7,new[]{Goods()});Check(false,"implicit missing run throws");}catch(InvalidOperationException){Check(true,"implicit missing run throws");}
p=Fresh();p.Read=_=>99;result=Buy(7,Goods());Check(result.Status==TransactionStatus.Rejected&&p.Debits==0,"patched GetCash disagrees with raw balance rejected");
foreach(var mode in new[]{"no-write","throw-before","throw-after","partial","over-debit","new-run"}){
 p=Fresh();p.Debit=(store,cost)=>{if(mode=="throw-before")throw new Exception("debit before");if(mode=="no-write")return;if(mode=="new-run"){GameSession.Identity=new(533,"new",2);GameSession.Player=new(){Pointer=new(2),playerCash=1000};return;}store.playerCash-=mode=="partial"?2:mode=="over-debit"?8:cost;if(mode=="throw-after")throw new Exception("debit after");};
 result=Buy(7,Goods());var normal=mode is "no-write"or"throw-before"or"throw-after";
 Check(result.Status==(normal?TransactionStatus.Restored:TransactionStatus.RecoveryFailed)&&goods==0,"native debit boundary status: "+mode);
 Check(p.Refunds==(mode=="throw-after"?1:0),"only exact native debit refunded: "+mode);
}
foreach(var mode in new[]{"no-write","throw-before","throw-after","partial"}){
 p=Fresh();p.Refund=(store,cost)=>{if(mode=="throw-before")throw new Exception("refund before");if(mode=="no-write")return;store.playerCash+=mode=="partial"?2:cost;if(mode=="throw-after")throw new Exception("refund after");};
 result=Buy(7,Goods(apply:()=>false,restore:()=>true));Check(result.Status==TransactionStatus.RecoveryFailed&&p.Refunds==1&&!result.Restored,"native refund never retried or falsely restored: "+mode);
}
foreach(var count in new[]{0,1,127,128}){
 p=Fresh();var steps=Enumerable.Range(0,count).Select(i=>Goods("s"+i)).ToArray();result=RunPurchaseApi.Purchase(1,steps);Check(result.Committed&&goods==count&&p.playerCash==99,"full author capacity plus envelope: "+count);
}
p=Fresh();Reject(()=>Buy(-1,Goods()),"negative cost rejected");Reject(()=>RunPurchaseApi.Purchase(1,null!),"null list rejected");Reject(()=>Buy(1,null!),"null steps list rejected");Reject(()=>Buy(1,Goods(),Goods()),"duplicate names rejected");Reject(()=>Buy(1,Goods("ezapi.purchase.cash")),"reserved cash name rejected");Reject(()=>Buy(1,Goods("ezapi.purchase.verify")),"reserved verification name rejected");Reject(()=>Buy(1,Goods("bad\n")),"control character name rejected");Reject(()=>Buy(1,Goods(new string('x',161))),"oversized name rejected");Reject(()=>Buy(1,new TransactionStep("missing",null!,()=>true,()=>true)),"missing check rejected");Reject(()=>Buy(1,new TransactionStep("missing",()=>true,null!,()=>true)),"missing apply rejected");Reject(()=>Buy(1,new TransactionStep("missing",()=>true,()=>true,null!)),"missing restore rejected");Reject(()=>RunPurchaseApi.Purchase(1,Enumerable.Range(0,129).Select(i=>Goods("s"+i)).ToArray()),"129 author steps rejected");
var background=Task.Run(()=>{try{Buy(1,Goods());return false;}catch(InvalidOperationException){return true;}}).Result;Check(background,"background thread blocked");
p=Fresh();result=Buy(1,Goods());Check(result.Committed,"failure paths clear purchase reentry gate");
// 恢复失败仍继续恢复更早的商品，但不在商品可能保留时自动退款。
p=Fresh();result=Buy(7,Goods("first"),Goods("last",()=>{goods++;return false;},()=>false));Check(result.Status==TransactionStatus.RecoveryFailed&&order.SequenceEqual(new[]{"first"})&&p.Refunds==0&&goods==1,"earlier delivery restores still run after later restore failure");
p=Fresh();result=Buy(0,Goods(apply:()=>false,restore:()=>true));Check(result.Status==TransactionStatus.Restored&&p.Debits==p.Refunds&&p.Refunds==0,"zero cost failure performs no cash mutation");
p=Fresh();result=Buy(7,Goods(apply:()=>{p.playerCash++;return true;}));Check(result.Error=="EZAPI_PURCHASE_CASH_INTERFERENCE"&&result.Status==TransactionStatus.RecoveryFailed,"cash interference reason preserved in uncertain receipt");
p=Fresh();p.Read=store=>{GameSession.Identity=new(533,"other",2);GameSession.Player=new(){Pointer=new(2),playerCash=1000};return store.playerCash;};result=Buy(7,Goods());Check(result.Status==TransactionStatus.Rejected&&p.Debits==0&&GameSession.Player!.playerCash==1000,"native GetCash changes run before payment refused");
p=Fresh();result=Buy(7,Goods(check:()=>{GameSession.Player=new(){Pointer=new(77),playerCash=900};return true;}));Check(result.Status==TransactionStatus.RecoveryFailed&&p.Debits==0&&GameSession.Player!.playerCash==900,"store pointer replacement in same epoch detected");
p=Fresh();result=Buy(7,Goods(apply:()=>{p.playerCash=-1;return false;}));Check(result.Status==TransactionStatus.RecoveryFailed&&p.Refunds==0&&p.playerCash==-1,"negative unexpected cash never clamped or overwritten");
p=Fresh();result=Buy(7,Goods(apply:()=>{p.playerCash=int.MaxValue;return false;}));Check(result.Status==TransactionStatus.RecoveryFailed&&p.Refunds==0&&p.playerCash==int.MaxValue,"unexpected maximum cash never overflow refunded");
p=Fresh();Reject(()=>Transactions.Run(Enumerable.Range(0,129).Select(i=>Goods("s"+i)).ToArray()),"generic transaction public capacity remains128");
Console.WriteLine(JsonSerializer.Serialize(new{passed=cases.Count-failed,failed,cases,nativeRuntimeVerified=false,scope="real RunPurchase.cs and Transactions.cs; instrumented PlayerStore/GameSession boundaries; native payment and inventory evidence separate"},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;

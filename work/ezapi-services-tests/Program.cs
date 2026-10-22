using EZapi;
using Il2Cpp;
using System.Text.Json;
var cases=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add(name);}
void Reject(Action action,string name){try{action();throw new Exception("accepted: "+name);}catch(ArgumentException){cases.Add(name);}catch(InvalidOperationException){cases.Add(name);}}
var initial=GameSession.Identity!;
int balance=10,goods=0;
var log=new List<string>();
TransactionStep Cash()=>new("cash",()=>balance>=3,()=>{balance-=3;return true;},()=>{log.Add("cash");balance=10;return true;});
TransactionStep Goods(Func<bool> apply,Func<bool>? restore=null)=>new("goods",()=>true,apply,restore??(()=>{log.Add("goods");goods=0;return true;}));
var result=Transactions.Run(new[]{Cash(),Goods(()=>{goods++;return true;})});
Check(result.Committed&&balance==7&&goods==1,"successful payment and delivery commit once");
balance=10;goods=0;
result=Transactions.Run(new[]{new TransactionStep("denied",()=>false,()=>{goods++;return true;},()=>true)});
Check(result.Status==TransactionStatus.Rejected&&goods==0,"failed preflight prevents any apply");
foreach(var throws in new[]{false,true}) {
    balance=10;goods=0;log.Clear();
    result=Transactions.Run(new[]{Cash(),Goods(()=>{goods=1;if(throws)throw new Exception("partial");return false;})});
    Check(result.Status==TransactionStatus.Restored&&balance==10&&goods==0,"partially written current step restored: "+throws);
    Check(log.SequenceEqual(new[]{"goods","cash"}),"reverse restoration order includes failing step: "+throws);
}
balance=10;log.Clear();
result=Transactions.Run(new[]{Cash(),Goods(()=>false,()=>throw new Exception("restore fault"))});
Check(result.Status==TransactionStatus.RecoveryFailed&&balance==10&&result.RecoveryFailures.Count==1,"restore exception reported while earlier restores continue");
balance=10;
result=Transactions.Run(new[]{Cash(),Goods(()=>false,()=>false)});
Check(!result.Restored&&result.Status==TransactionStatus.RecoveryFailed,"failed restore never reported successful");
var checks=0;balance=10;
result=Transactions.Run(new[]{Cash(),new TransactionStep("eligibility",()=>++checks==1,()=>true,()=>true)});
Check(result.Restored&&balance==10,"eligibility rechecked after preceding write");
result=Transactions.Run(new[]{new TransactionStep("query exception",()=>throw new Exception("query"),()=>true,()=>true)});
Check(result.Status==TransactionStatus.Rejected&&result.Error!.Contains("query"),"query exception contained before writes");
result=Transactions.Run(new[]{new TransactionStep("outer",()=>true,()=>Transactions.Run(new[]{Cash()}).Status==TransactionStatus.Reentrant,()=>true)});
Check(result.Committed,"nested transaction rejected without corrupting outer transaction");
Reject(()=>Transactions.Run(Array.Empty<TransactionStep>()),"empty transaction rejected");
Reject(()=>Transactions.Run(new[]{Cash(),Cash()}),"duplicate transaction names rejected");
var changing=new List<TransactionStep>();
changing.Add(new("snapshot",()=>{changing.Clear();return true;},()=>true,()=>true));
Check(Transactions.Run(changing).Committed,"caller mutation cannot alter captured step list");

PowerHelper.Reset();var source=new GameItem();
Check(Power.GetMachineCost(source)==7&&Power.GetEnergy(source)==100,"native power reads use current helper and ignore bandwidth limit");
Check(Power.TryDebit(source,30).Debited&&source.Energy==70&&PowerHelper.Draws==1,"native debit verified against exact requested difference");
Check(Power.TryDebit(source,71).Status==EnergyDebitStatus.Insufficient&&source.Energy==70&&PowerHelper.Draws==1,"insufficient power leaves energy unchanged");
Reject(()=>Power.TryDebit(source,-1),"negative debit rejected");
Check(Power.TryDebit(source,0).Debited&&PowerHelper.Draws==1,"zero cost performs no debit write");
source.Owned=false;Check(Power.TryDebit(source,1).Status==EnergyDebitStatus.Rejected,"unowned power source rejected");source.Owned=true;
source.Energy=100;source.unitCount=2;PowerHelper.Reset();Check(Power.TryDebit(source,1).Debited&&source.Energy==99&&source.unitCount==2&&PowerHelper.Draws==1,"stacked source debits once without splitting or scaling");source.Energy=100;source.unitCount=1;
source.parentInventory=null;Check(Power.TryDebit(source,1).Status==EnergyDebitStatus.Rejected,"unplaced source rejected");source.parentInventory=new();
PowerHelper.Allow=false;Check(Power.TryDebit(source,1).Status==EnergyDebitStatus.Rejected,"native can-draw rejection honored");PowerHelper.Allow=true;
foreach(var mode in new[]{"partial","false","throw"}) {
    source.Energy=100;PowerHelper.Reset();
    PowerHelper.Draw=(item,amount)=>{item.Energy-=2;if(mode=="throw")throw new Exception("draw");return mode!="false";};
    var debit=Power.TryDebit(source,10);
    Check(debit.Status==EnergyDebitStatus.Restored&&source.Energy==100&&PowerHelper.Restores==1,"native partial debit restored and verified: "+mode);
}
PowerHelper.Restore=(item,amount)=>{};
Check(Power.TryDebit(source,10).Status==EnergyDebitStatus.RecoveryFailed,"failed energy restoration reported");
PowerHelper.Reset();source.Energy=100;
PowerHelper.Draw=(item,amount)=>{item.Energy-=amount;item.parentInventory=new(){Pointer=new(50)};return true;};
Check(Power.TryDebit(source,10).Status==EnergyDebitStatus.RecoveryFailed&&PowerHelper.Restores==0,"changed parent prevents restore into a different inventory context");
PowerHelper.Reset();source.Energy=100;
PowerHelper.Draw=(item,amount)=>{item.Energy-=amount;GameSession.Identity=initial with{Epoch=2};return true;};
Check(Power.TryDebit(source,10).Status==EnergyDebitStatus.RecoveryFailed&&PowerHelper.Restores==0,"changed run invalidates debit context without stale restore");
GameSession.Identity=initial;PowerHelper.Reset();source.Energy=100;
PowerHelper.Draw=(item,amount)=>{Check(Power.TryDebit(item,1).Status==EnergyDebitStatus.Reentrant,"same source recursive debit rejected");item.Energy-=amount;return true;};
Check(Power.TryDebit(source,10).Debited,"outer debit succeeds after reentry rejection");
PowerHelper.Reset();

var water=new GameItem{LiquidContainer=true,Total=1500,Free=500,Purity=3};
water.Tags["water_volume"]=new(){Value=1000};water.Tags["salt_volume"]=new(){Value=500};
var capture=Liquids.Capture(water);
Check(capture.TotalParts==1500&&capture.CapacityParts==2000&&capture.Millilitres==1.5m,"liquid snapshot preserves parts capacity and decimal unit conversion");
water.Tags["water_volume"].Value=700;
Check(capture.Contents[0].Parts==1000,"liquid snapshot detached from later native mutation");water.Tags["water_volume"].Value=1000;
Check(Liquids.WaterQuality(water)==3,"water quality uses native condition pure");
foreach(var grade in new[]{0,1,2}) {water.Purity=grade;Check(Liquids.WaterQuality(water)==grade,"water quality native grade "+grade);}
water.Tags["salt_volume"].Value=-1;Reject(()=>Liquids.Capture(water),"negative component rejected");water.Tags["salt_volume"].Value=500;
water.Total=1499;Reject(()=>Liquids.Capture(water),"native component total mismatch rejected");water.Total=1500;
water.Free=-1;Reject(()=>Liquids.Capture(water),"negative free capacity rejected");water.Free=500;
water.Free=int.MaxValue;Reject(()=>Liquids.Capture(water),"capacity overflow rejected");water.Free=500;
var catalog=Liquid.Liquids;Liquid.Liquids=null;Reject(()=>Liquids.Capture(water),"missing liquid catalog rejected without initializing global state");Liquid.Liquids=catalog;
catalog!.Add(new(){identifier="water",currentPart="other"});Reject(()=>Liquids.Capture(water),"duplicate liquid ID rejected");catalog.RemoveAt(2);
catalog.Add(new(){identifier="other",currentPart="water_volume"});Reject(()=>Liquids.Capture(water),"alias volume tag rejected to prevent double counting");catalog.RemoveAt(2);
water.LiquidContainer=false;Check(!Liquids.IsContainer(water),"ordinary item is not liquid container");Reject(()=>Liquids.Capture(water),"ordinary item cannot produce liquid snapshot");
water.LiquidContainer=true;water.unitCount=2;Check(!Liquids.IsContainer(water),"stacked liquid container not treated as one");water.unitCount=1;

DirectoryMaster.Catalog["native_item"]=()=>new GameItemElement();
Check(Items.Exists("native_item")&&!Items.Exists("missing"),"item existence queries native catalog");
Check(!Items.Create("native_item",false).Owned,"native item creation forwards ownership");
Check(Items.GetDisplayName("native_item")=="Name:native_item","display name delegates to native directory helper");
Reject(()=>Items.Create("missing"),"missing factory rejected before creation");
Reject(()=>Items.Exists(" \n"),"invalid item ID rejected");
var element=Items.Create("native_item");
Check(ItemInspection.CurrentUnitValue(element)==123,"valuation uses native current-value flags");
Check(ItemInspection.BuildTooltip(element).Text=="normal"&&ItemInspection.BuildTooltip(element,true).Text=="advanced","tooltip uses native completed paths without manual builder");
Reject(()=>ItemInspection.BuildTooltip(new GameItem()),"data-only item lacks tooltip element");
foreach(var units in new[]{1,2,5,int.MaxValue}) {
 element.unitCount=units;element.Value=10000000001;
 var before=(element.Pointer,element.uniqueId,element.unitCount,element.Value,element.parentInventory);
 Check(ItemInspection.CurrentUnitValue(element)==10000000001,"stack reads exact native Int64 unit value without multiply/divide: "+units);
 Check(before==(element.Pointer,element.uniqueId,element.unitCount,element.Value,element.parentInventory),"inspection does not modify or detach stack: "+units);
}
element.unitCount=0;Reject(()=>ItemInspection.CurrentUnitValue(element),"zero-count item rejected");
element.unitCount=1;
Reject(()=>ItemInspection.CurrentUnitValue(null!),"null inspection rejected");
Reject(()=>ItemInspection.CurrentUnitValue(new GameItem{Pointer=IntPtr.Zero}),"dead inspection rejected");
GameSession.Identity=null;Reject(()=>ItemInspection.CurrentUnitValue(element),"inspection rejects unloaded run");GameSession.Identity=initial;
Check(Task.Run(()=>{try{ItemInspection.CurrentUnitValue(element);return false;}catch(InvalidOperationException){return true;}}).Result,"inspection rejects background thread");
GameSession.Identity=null;Reject(()=>Items.Exists("native_item"),"runtime item access blocked outside current run");
Reject(()=>Power.GetEnergy(source),"runtime power access blocked outside current run");
Reject(()=>Liquids.IsContainer(water),"runtime liquid access blocked outside current run");GameSession.Identity=initial;
var background=Task.Run(()=>{try{Power.GetEnergy(source);return false;}catch(InvalidOperationException){return true;}}).Result;
Check(background,"native resource access rejects background thread");
var report=new{passed=cases.Count,cases,nativeRuntimeTest=false,scope="real Transactions/NativeAccess/Power/Liquids/Items sources with instrumented native and session stubs; no native gameplay or persistence proof"};
Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-services-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.nativeRuntimeTest}));

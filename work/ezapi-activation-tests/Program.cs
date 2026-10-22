using EZapi;
using Il2Cpp;
using System.Text.Json;

var cases=new List<string>();
void Check(bool ok,string name) { if(!ok)throw new Exception(name);cases.Add(name); }
var inventory=new GameInventory();
ItemDefinition Definition(string id,ItemActivationDefinition? activation=null) {
    var d=new ItemDefinition{Id=id,Name=id,Activation=activation};
    ItemRegistry.Definitions.Add(id,d);return d;
}
GameItem Instance(ItemDefinition d) {
    var item=new GameItem{identifier=d.Id,parentInventory=inventory};
    ItemActivation.Bind(item,d);return item;
}
bool Can(GameItem item)=>item.canActivateSlotItemFunc!.Invoke(item,inventory,null!);
void Activate(GameItem item)=>item.onActivateSlotItemFunc!.Invoke(item,inventory,null!);
void Wound() { PlayerStore.instance=new();PlayerStore.instance.healthData!.woundState=2;PlayerStore.instance.healthData.isWoundStable=false;PlayerStore.instance.healthData.isWoundedFresh=true; }

Check(!ItemActivation.Register("test.unknown",_=>true,_=>{}).Accepted,"unknown item cannot receive callbacks");
var custom=Definition("test.custom");int queries=0,actions=0;
Check(ItemActivation.Register(custom.Id,_=>{queries++;return true;},_=>actions++).Accepted,"custom callbacks accepted");
Check(!ItemActivation.Register(custom.Id,_=>true,_=>{}).Accepted,"duplicate callbacks rejected");
ItemRegistry.Ready=false;
Check(!ItemActivation.Register(Definition("test.late").Id,_=>true,_=>{}).Accepted,"late callback registration rejected");
ItemRegistry.Ready=true;
var item=Instance(custom);var other=Instance(custom);
Check(ReferenceEquals(item.onActivateSlotItemFunc,other.onActivateSlotItemFunc),"instances share delegates without sharing item state");
Check(Can(item)&&actions==0&&queries==1,"query does not run action");
Activate(item);Check(actions==1&&item.DestroyCount==0&&item.UseCalls==0,"custom action has no automatic consumption");
var before=queries;item.Owned=false;Activate(item);
Check(!Can(item)&&actions==1&&queries==before,"unowned item blocks both query and action");item.Owned=true;
item.parentInventory=new GameInventory{Pointer=new(11)};Activate(item);
Check(!Can(item)&&actions==1,"wrong inventory blocks stale callback");item.parentInventory=inventory;
item.forceDisableActivate=true;Check(!Can(item),"forceDisableActivate gate");item.forceDisableActivate=false;
item.forceDisableUse=true;Check(!Can(item),"forceDisableUse gate");item.forceDisableUse=false;
item.unitCount=2;Check(!Can(item),"unexpected stack size blocks activation");item.unitCount=1;
PlayerStore.instance=null;Check(!Can(item),"missing current player blocks activation");PlayerStore.instance=new(){runID=""};
Check(!Can(item),"missing run identity blocks activation");PlayerStore.instance=new();
Check(Can(item),"callback eligibility can recover after scene state ready");
item.Owned=false;Activate(item);Check(actions==1,"action rechecks after previously successful query");item.Owned=true;

var heal=Definition("test.heal",new(){Kind="healWounds"});
Check(!ItemActivation.Register(heal.Id,_=>true,_=>{}).Accepted,"JSON and C# behavior conflict rejected");
var medical=Instance(heal);Wound();
Check(Can(medical)&&medical.DestroyCount==0&&PlayerStore.instance!.healthData!.woundState==2,"healing query preserves wound and item");
Activate(medical);
var health=PlayerStore.instance!.healthData!;
Check(health.woundState==0&&health.isWoundStable&&!health.isWoundedFresh&&medical.DestroyCount==1,"official healing fields and destroy consumption");
Activate(medical);Check(medical.DestroyCount==1,"destroyed item cannot execute again");
var healthy=Instance(heal);Activate(healthy);Check(!Can(healthy)&&healthy.DestroyCount==0,"healthy player does not consume item");
health.isWoundStable=false;Check(Can(healthy),"unstable wound is eligible even at zero woundState");health.isWoundStable=true;
health.isWoundedFresh=true;Check(Can(healthy),"fresh wound is eligible");health.isWoundedFresh=false;
PlayerStore.instance.healthData=null;Check(!Can(healthy),"missing health data blocks healing");Wound();
var charged=Instance(Definition("test.charged",new(){Kind="healWounds",Consumption="useCount",Charge=2}));
Activate(charged);Check(charged.Charges==1&&charged.UseCalls==1&&charged.DestroyCount==0,"configured native use-count charge applied");
Wound();Activate(charged);Check(!Can(charged)&&charged.UseCalls==1&&PlayerStore.instance!.healthData!.woundState==2,"insufficient charges preserve wound and item");

var reentrant=Definition("test.reentrant");int reentries=0;
ItemActivation.Register(reentrant.Id,_=>true,c=>{reentries++;Activate(c.Item);});
Activate(Instance(reentrant));Check(reentries==1,"recursive activation is blocked");
var recursiveQuery=Definition("test.recursivequery");int queryEntries=0;
ItemActivation.Register(recursiveQuery.Id,c=>{queryEntries++;return !Can(c.Item);},_=>{});
Check(Can(Instance(recursiveQuery))&&queryEntries==1,"recursive predicate query is blocked");
var bad=Definition("test.bad");int attempted=0;
ItemActivation.Register(bad.Id,_=>true,_=>{attempted++;throw new Exception("action failure");});
var badItem=Instance(bad);Activate(badItem);Activate(badItem);
Check(attempted==1&&!Can(badItem),"throwing action disables ID and is not retried");
Check(Can(item),"failed ID leaves other item behaviors available");
var badQuery=Definition("test.badquery");
ItemActivation.Register(badQuery.Id,_=>throw new Exception("query failure"),_=>throw new Exception("must not run"));
Check(!Can(Instance(badQuery)),"predicate exception stays inside callback boundary");
var failedCharge=Instance(Definition("test.failedcharge",new(){Kind="healWounds",Consumption="useCount"}));
failedCharge.ThrowOnUse=true;Wound();Activate(failedCharge);Wound();Activate(failedCharge);
Check(!Can(failedCharge)&&PlayerStore.instance!.healthData!.woundState==2,"native consumption failure disables behavior without retrying partial effect");
Check(MelonLoader.MelonLogger.Errors.Count==3,"callback failures recorded with no repeated flood");
ItemActivation.Clear();
Check(!Can(item),"shutdown leaves old delegates inert");
Check(ItemActivation.Register(custom.Id,_=>true,_=>{}).Accepted,"clear resets behavior registry");

var report=new{passed=cases.Count,cases,nativeRuntimeTest=false,scope="real ItemActivation source; callback delegates, game objects and native helpers replaced by stubs"};
Directory.CreateDirectory("work");
File.WriteAllText("work/ezapi-activation-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));

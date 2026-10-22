using EZapi;
using Il2Cpp;
using System.Text.Json;
var checks=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
void Reject(Action call,string name){try{call();throw new Exception("accepted: "+name);}catch(ArgumentException){checks.Add(name);}catch(InvalidOperationException){checks.Add(name);}}
void Reset(){ItemStockRuntime.Stop();ItemStockRuntime.Start();ItemStockRuntime.DirectoryReady();GameSession.Identity=new(533);GameSession.Player=new();EmporiumEntry.Instance=new();NativeFactoryRegistry.Declarations.Clear();ItemRegistry.Definitions.Clear();ItemRegistry.Unapplied.Clear();Items.Created.Clear();Items.OnCreate=null;Items.Fail=false;NativeHookGroup.Live=true;}
Il2CppSystem.Collections.Generic.List<GameItem> Fresh()=>new();
void Define(string id,NightShopStockPolicy policy=NightShopStockPolicy.Repeatable,Func<bool>? available=null)=>NativeFactoryRegistry.Declarations.Add((id,policy,available));
GameInventory Table()=>EmporiumEntry.Instance!.frontInvinvElement!;
void Supply()=>ItemStockRuntime.ProbeEvent(Fresh(),false);
foreach(var policy in Enum.GetValues<NightShopStockPolicy>())foreach(var owned in new[]{false,true})foreach(var stocked in new[]{false,true})foreach(var available in new[]{false,true}) {
 var calls=0;var result=ItemStockRules.Eligible(policy,owned,stocked,()=>{calls++;return available;});
 var pre=policy!=NightShopStockPolicy.None&&!stocked&&(policy!=NightShopStockPolicy.Unique||!owned);
 Check(result==(pre&&available)&&calls==(pre?1:0),$"policy eligibility and predicate count {policy}/{owned}/{stocked}/{available}");
}
Reject(()=>ItemStockRules.Eligible((NightShopStockPolicy)99,false,false,null),"unknown policy rejected");
Reset();Check(ItemStockRuntime.IsAvailable,"hooks ready");
Define("a.repeat");Define("a.unique",NightShopStockPolicy.Unique);var list=Fresh();var ownedItem=new GameItem{identifier="owned",Owned=true};list.Add(ownedItem);
ModHook.FireOnPlaceInventorInventoryItemLate(list);Check(Table().childItems.Count==0,"unscoped official late event inert");
ItemStockRuntime.ProbeEvent(list,true);Check(Table().childItems.Count==0,"visiting inventor excluded");
ItemStockRuntime.ProbeEvent(list,false);Check(Table().childItems.Count==2&&Table().childItems.All(i=>!i.Owned&&i.parentInventory==Table()),"night scope actually places unowned native sale goods");
Check(list.Count==1&&ReferenceEquals(list[0],ownedItem),"official owned snapshot is never mutated");
var created=Items.Created.Count;Supply();Check(Table().childItems.Count==2&&Items.Created.Count==created,"existing shelf goods prevent repeated creation");
Check(ItemStockRuntime.Snapshot().All(v=>v.Added==1&&v.Skipped==1),"counters reflect verified sale stock and skip");
Reset();Define("a.unique",NightShopStockPolicy.Unique);Define("a.repeat");GameSession.Player.Items.Add(new(){identifier="a.unique",Owned=true});GameSession.Player.Items.Add(new(){identifier="a.repeat",Owned=true});Supply();Check(Table().childItems.Count==1&&Table().childItems[0].identifier=="a.repeat","owned unique excluded; owned repeat remains eligible");
GameSession.Player.Items.Clear();Table().childItems.Clear();Supply();Check(Table().childItems.Count==2,"unique eligible again after no longer owned");
Reset();Define("a.unique",NightShopStockPolicy.Unique);GameSession.Player.Items.Add(new(){identifier="a.unique",Owned=false});Supply();Check(Table().childItems.Count==1,"unowned items do not block unique stock");
Reset();Define("a.skip",available:()=>false);Define("a.throws",available:()=>throw new Exception("predicate"));Define("a.ok");Supply();Check(Table().childItems.Count==1&&Table().childItems[0].identifier=="a.ok","false/throw predicates isolated from later goods");Check(ItemStockRuntime.Snapshot().Single(v=>v.ItemId=="a.throws").LastError!.Contains("predicate"),"predicate error visible");
Reset();Define("a.failed");ItemRegistry.Unapplied.Add("a.failed");Supply();Check(Table().childItems.Count==0&&Items.Created.Count==0,"unapplied directory does not run factory");
Reset();ItemRegistry.Definitions.Add(new("json.repeat","repeatable"));ItemRegistry.Definitions.Add(new("json.unique","unique"));ItemRegistry.Definitions.Add(new("json.none","none"));Supply();Check(Table().childItems.Count==2,"JSON policy routes to same actual sale supplier");
Reset();Define("a.factory");Items.Fail=true;Supply();Check(ItemStockRuntime.Snapshot().Single().LastError!.Contains("factory"),"factory fault reported");
Reset();Define("a.reject");GameSession.Player.Reject=true;Supply();Check(Items.Created.Single().Destroyed&&Table().childItems.Count==0,"native rejected goods destroyed exactly once by native owner");Check(ItemStockRuntime.Snapshot().Single().Added==0&&ItemStockRuntime.Snapshot().Single().LastError!.Contains("PLACEMENT"),"void return cannot falsely report accepted placement");
foreach(var after in new[]{false,true}){
 Reset();Define("a.throw");GameSession.Player.ThrowBefore=!after;GameSession.Player.ThrowAfter=after;Supply();
 Check(ItemStockRuntime.Snapshot().Single().Added==0&&ItemStockRuntime.Snapshot().Single().LastError!.Contains("placement"),"native exception never counted as successful supply "+after);
 Check(!Items.Created.Single().Destroyed&&Table().childItems.Count==(after?1:0),"caller never double-destroys handed-off goods after native exception "+after);
}
Reset();Define("a.stale",available:()=>{GameSession.Identity=new(600);return true;});Supply();Check(Table().childItems.Count==0&&Items.Created.Count==0,"predicate changing run blocks creation");
Reset();Define("a.stop",available:()=>{ItemStockRuntime.Stop();return true;});Supply();Check(Table().childItems.Count==0&&ItemStockRuntime.Snapshot().Count==0,"predicate stopping framework leaves no stale diagnostics or supply");
Reset();Define("a.stale");Items.OnCreate=()=>GameSession.Identity=new(600);Supply();Check(Table().childItems.Count==0&&Items.Created.Single().Destroyed,"factory changing run destroys candidate before native handoff");
Reset();Define("a.reentry",available:()=>{ItemStockRuntime.Supply(Fresh());return true;});Supply();Check(Table().childItems.Count==0&&ItemStockRuntime.Snapshot().Single().LastError!.Contains("REENTRY"),"nested supplier operation rejected");
Reset();Define("a.event",available:()=>{ModHook.FireOnPlaceInventorInventoryItemLate(Fresh());return true;});Supply();Check(Items.Created.Count==1&&Table().childItems.Count==1,"reentrant official event inert while busy");
Reset();Define("a.same");Define("a.same");GameSession.Player.Reject=true;Supply();Check(Items.Created.Count==1,"same scope rejected candidate not retried from duplicate declaration");
Reset();Define("a.table",available:()=>{EmporiumEntry.Instance=new();return true;});Supply();Check(Items.Created.Count==0,"changing native sale table blocks factory");
Reset();Define("a.table");Items.OnCreate=()=>EmporiumEntry.Instance=new();Supply();Check(Items.Created.Single().Destroyed&&Table().childItems.Count==0,"factory replacing native sale table destroys unhanded candidate");
Reset();Define("a.stale");GameSession.Player.OnPlace=()=>GameSession.Identity=new(600);Supply();Check(ItemStockRuntime.Snapshot().Single().Added==0&&Table().childItems.Count==1,"post-placement run change not counted or destroyed");
Reset();Define("a.units");Items.OnCreate=()=>{};Supply();Check(ItemStockRuntime.Snapshot().Single().Added==1,"readback confirms complete unit delta");
Reset();Define("a.limit");for(var i=0;i<4096;i++)Table().childItems.Add(new(){identifier="base."+i});Supply();Check(Table().childItems.Count==4096&&Items.Created.Count==0,"sale table bound checked before factory");
Reset();Define("a.limit");list=Fresh();for(var i=0;i<16385;i++)list.Add(new());ItemStockRuntime.ProbeEvent(list,false);Check(Items.Created.Count==0,"oversized owned snapshot rejected without creation");
Reset();Define("a.limit");for(var i=0;i<16385;i++)GameSession.Player.Items.Add(new());Supply();Check(Items.Created.Count==0,"oversized live owned graph rejected without creation");
Reset();EmporiumEntry.Instance=null;Supply();Check(Items.Created.Count==0,"missing sale table rejected before factory");
Reset();var old=ModHook.OnPlaceInventorInventoryItemLate;ModHook.OnPlaceInventorInventoryItemLate=null;Check(!ItemStockRuntime.IsAvailable,"lost official subscription disables capability");ModHook.OnPlaceInventorInventoryItemLate=old;NativeHookGroup.Live=false;Check(!ItemStockRuntime.IsAvailable,"lost scoped hook disables capability");
Reset();Check(Task.Run(()=>!ItemStockRuntime.IsAvailable).Result,"capability off-thread fails closed");Check(Task.Run(()=>{try{ItemStockRuntime.Supply(Fresh());return false;}catch(InvalidOperationException){return true;}}).Result,"off-thread supplier rejected");
Reject(()=>ItemStockRuntime.Supply(Fresh()),"unscoped direct supply rejected");GameSession.Identity=null;Reject(()=>ItemStockRuntime.Supply(Fresh()),"unloaded run rejected");ItemStockRuntime.Stop();Check(!ItemStockRuntime.IsAvailable&&ModHook.OnPlaceInventorInventoryItemLate is null,"stop removes same official late delegate");
var report=new{passed=checks.Count,failed=0,checks,nativeRuntimeVerified=false,scope="real stock policy and runtime sources with native/session/hook stand-ins; actual native shelf/payment/save require game evidence"};Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-stock-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed}));

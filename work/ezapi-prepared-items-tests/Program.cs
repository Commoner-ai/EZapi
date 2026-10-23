using EZapi;
using Il2Cpp;
using System.Text.Json;
var passed=0;
void Check(bool value,string label){if(!value)throw new Exception(label);passed++;}
void Reset()
{
    GameSession.OnThread=true;GameSession.Run=new(560,"test",1);GameSession.Player=new();GameBuildFingerprint.IsKnown=true;
    InventoryFreezeApi.IsAvailable=true;InventoryFreezeApi.OnFreeze=null;InventoryApi.Items=new();InventoryApi.IsItemDragActive=false;InventoryApi.OnCapture=null;
    DirectoryMaster.HasItem=true;PreBuiltItemHelper.Calls=0;PreBuiltItemHelper.Last=null;PreBuiltItemHelper.Override=null;PreBuiltItemHelper.Created=null;
    LockHelper.Loads=0;LockHelper.Ignore=false;LockHelper.OnLoad=null;GeneralHelper.IgnoreOwnership=false;
}
GameItem Plain(string id="system_module_performance")=>new(){identifier=id};
Check(PreparedItemApi.Describe().Count==12&&PreBuiltItemHelper.Calls==0,"pure managed catalog does not execute factories");
foreach(var d in PreparedItemApi.Describe())foreach(var own in new[]{true,false})
{
    Reset();var created=PreparedItemApi.Create(d.Kind,own);
    Check(created.Status==PreparedItemStatus.Created&&created.Item?.identifier==d.ItemId&&created.Snapshot?.Definition==d,"all twelve explicit native routes return exact IDs");
    Check(PreBuiltItemHelper.Calls==1&&PreBuiltItemHelper.Last==d.NativeFactory&&created.FactoryAttempted,"exactly one native factory");
    Check(created.Item!.parentInventory is null&&created.Item.Owned==own&&created.Item.DestroyCalls==0,"detached caller created and requested root ownership");
    Check(created.Snapshot!.NestedItems==(d.LootRecipe is null?0:1)&&LockHelper.Loads==(d.LootRecipe is null?0:1),"filled crate graph observed and native lock initialized without refilling");
}
Reset();Check(PreparedItemApi.Create((PreparedItemKind)99).Status==PreparedItemStatus.Invalid&&PreBuiltItemHelper.Calls==0,"invalid kind no native factory");
foreach(var gate in new Action[]{()=>GameSession.OnThread=false,()=>GameSession.Run=null,()=>GameBuildFingerprint.IsKnown=false,()=>InventoryFreezeApi.IsAvailable=false,()=>DirectoryMaster.HasItem=false,()=>InventoryApi.IsItemDragActive=true})
{Reset();gate();var r=PreparedItemApi.Create(PreparedItemKind.SecurityCrate);Check(r.Status!=PreparedItemStatus.Created&&!r.FactoryAttempted&&PreBuiltItemHelper.Calls==0,"not-ready preflight never rolls native RNG");}
foreach(var bad in new Func<GameItem>[] {()=>Plain("wrong"),()=>new(){identifier="system_module_performance",unitCount=2},()=>new(){identifier="system_module_performance",state=null}})
{
 Reset();var item=bad();PreBuiltItemHelper.Override=_=>item;var r=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);
 Check(r.Status==PreparedItemStatus.Indeterminate&&r.FactoryAttempted&&r.Item is null&&item.DestroyCalls==1&&r.Cleanup==PreparedItemCleanup.DestroyReturned,"failed detached result cleaned once without reroll");
}
Reset();PreBuiltItemHelper.Override=_=>throw new Exception("factory secret");var result=PreparedItemApi.Create(PreparedItemKind.SecurityCrate);
Check(result.Status==PreparedItemStatus.Indeterminate&&result.Cleanup==PreparedItemCleanup.FactoryDidNotReturn&&PreBuiltItemHelper.Calls==1&&!result.Reason!.Contains("secret"),"throwing factory uncertain, no random retry or private exception message leak");
Reset();var borrowed=Plain();borrowed.Owned=true;InventoryApi.Items.Add(borrowed);PreBuiltItemHelper.Override=_=>borrowed;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&borrowed.DestroyCalls==0,"pre-existing run item never destroyed");
Reset();var attached=Plain();GameSession.Player.gridInv!.Attach(attached);PreBuiltItemHelper.Override=_=>attached;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&attached.DestroyCalls==0,"attached result never destroyed even when initial run item discovery omitted it");
Reset();var saved=Plain();saved.Owned=true;GameSession.Player.saveBags!.Add("old",new());GameSession.Player.saveBags["old"].Attach(saved);PreBuiltItemHelper.Override=_=>saved;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&saved.DestroyCalls==0,"saved bag provenance included");
Reset();var alien=Plain("base.child");alien.Owned=true;InventoryApi.Items.Add(alien);var root=Plain();root.Attach(alien);PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0&&alien.DestroyCalls==0,"borrowed descendant prevents destructive root cleanup");
Reset();root=Plain();var external=new GameInventory();external.Attach(root);root.parentInventory=null;PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"external graph parent blocks cleanup even if parentInventory forged null");
Reset();root=Plain();root.Attach(root);PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"cyclic graph never recursively destroyed");
Reset();root=Plain();root.children!.Add(new GameInventory());PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"nonreciprocal graph rejected");
Reset();root=Plain();root.Attach(new GameInventory());root.children!.Add(root.children[0]);PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"duplicate child edges rejected");
Reset();root=Plain();for(var i=0;i<8193;i++)root.Attach(new GameInventory());PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"bounded graph never truncates or destroys unknown graph");
Reset();root=Plain();PreBuiltItemHelper.Override=_=>{GameSession.Run=new(560,"test",2);return root;};
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&result.Cleanup==PreparedItemCleanup.SkippedStaleRun&&root.DestroyCalls==0,"reload skips old native wrapper cleanup");
Reset();root=Plain();root.OnSync=()=>GameSession.Run=new(560,"other",2);PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"sync callback run switch halts creation");
Reset();root=Plain();root.OnSync=()=>GameSession.Player.gridInv!.Attach(root);PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"sync callback admission prevents old-wrapper cleanup");
Reset();root=Plain();root.OnSync=()=>root.unitCount=2;PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==1,"sync unit mutation not silently accepted");
Reset();root=Plain();root.Frozen=true;PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"frozen native result preserved");
Reset();root=Plain();GeneralHelper.IgnoreOwnership=true;PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==1,"ownership write requires readback");
Reset();root=Plain();root.OnSync=()=>throw new Exception();root.OnDestroy=()=>throw new Exception();PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==1&&result.Cleanup==PreparedItemCleanup.Failed,"cleanup failure explicit and no second destroy");
Reset();root=Plain();root.OnSync=()=>throw new Exception();var freezeReads=0;InventoryFreezeApi.OnFreeze=i=>{if(++freezeReads==2)GameSession.Player.gridInv!.Attach(i);};PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"cleanup freeze callback cannot authorize destruction of newly attached item");
Reset();PreparedItemResult? recursive=null;root=Plain();root.OnSync=()=>recursive=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Created&&recursive?.Status==PreparedItemStatus.Conflict&&PreBuiltItemHelper.Calls==1,"recursive creation refuses second random roll");
Reset();var old=Plain("base.old");old.Owned=true;InventoryApi.Items.Add(old);root=Plain();PreBuiltItemHelper.Override=_=>{old.unitCount++;return root;};
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==1,"unrelated run mutation never reported successful or rolled back");
Reset();root=Plain("sec_box");root.contentWindow=new();root.Tags.Add("LOOT_CRATE_ID_STRING",new(){Value="loot_crate_security"});PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.SecurityCrate);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==1&&LockHelper.Loads==0,"empty crate template rejected without calling fill/refill");
Reset();root=Plain("sec_box");root.contentWindow=new();root.Tags.Add("LOOT_CRATE_ID_STRING",new(){Value="wrong"});root.Attach(Plain("loot"));PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.SecurityCrate);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==1,"wrong loot recipe never accepted");
Reset();root=Plain("sec_box");root.contentWindow=new(){isLocked=false};root.Tags.Add("LOOT_CRATE_ID_STRING",new(){Value="loot_crate_security"});root.Attach(Plain("loot"));LockHelper.Ignore=true;PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.SecurityCrate);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==1&&LockHelper.Loads==1,"native lock has to read back locked");
Reset();root=Plain();root.OnSetOwned=()=>root.children!.Add(Plain("extra"));PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==0,"ownership callback cannot add unverified graph");
Reset();Check(PreparedItemApi.Create(PreparedItemKind.GenuineInjector).Status==PreparedItemStatus.Created,"all paths release reentry gate");
Reset();root=Plain();root.Attach(new GameItem{identifier="base.one",uniqueId=0});root.Attach(new GameItem{identifier="base.two",uniqueId=0});PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Created&&result.Snapshot?.NestedItems==2,"unowned detached child instances may both have native ID0");
Reset();root=Plain();root.Attach(new GameItem{identifier="base.one",uniqueId=0,Owned=true});root.Attach(new GameItem{identifier="base.two",uniqueId=0,Owned=true});PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule);Check(result.Status==PreparedItemStatus.Indeterminate&&root.DestroyCalls==1,"duplicate player-owned identities refused");
Reset();root=Plain();root.uniqueId=0;PreBuiltItemHelper.Override=_=>root;
result=PreparedItemApi.Create(PreparedItemKind.PerformanceModule,owned:false);Check(result.Status==PreparedItemStatus.Created&&result.Snapshot?.InstanceId==0,"unowned root0 preserved without forced ID rewrite");
Console.WriteLine(JsonSerializer.Serialize(new{passed,failed=0,productionSources=true,scope="Real PreparedItems.cs; native factory/graph/ownership/lock boundary stand-ins, not gameplay/save acceptance"}));

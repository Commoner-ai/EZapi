using EZapi;
using Il2Cpp;
using HarmonyLib;
using System.Reflection;
using System.Text.Json;
var cases=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);cases.Add(name);}
void Throws<T>(Action action,string name)where T:Exception {try{action();throw new Exception("missing exception "+name);}catch(T){Check(true,name);}}
MethodInfo Target(string name)=>((name.StartsWith("Fire"))?typeof(ModHook):typeof(PlayerStore)).GetMethod(name)!;
void Fire(string name,Action? body=null){Sim.Run(Target(name),PlayerStore.instance!,Array.Empty<object?>(),()=>{body?.Invoke();return 1;});}
void Restart(){LifecycleApi.Stop();GameSession.Start();LifecycleApi.Start();LifecycleApi.DirectoryReady();}
GameSession.Start();
Throws<InvalidOperationException>(()=>LifecycleApi.Subscribe("test.mod","early",LifecyclePhase.BeforeLoad,_=>{}),"subscription blocked before initialize");
LifecycleApi.Start();
var delayed=LifecycleApi.Subscribe("test.mod","delayed",LifecyclePhase.BeforeLoad,_=>{});
Check(!LifecycleApi.GetRuntimeStatus().Ready,"subscriptions may stage before directory without fake ready");
LifecycleApi.DirectoryReady();delayed.Dispose();
Check(LifecycleApi.GetRuntimeStatus().Ready&&LifecycleApi.GetRuntimeStatus().Targets.Count==7,"seven exact native targets installed");
Check(Harmony.Installed.Values.SelectMany(x=>x).Count(x=>x.Owner=="ezapi.lifecycle")==7,"independent lifecycle owner installed once");
LifecycleApi.DirectoryReady();Check(Harmony.Installed.Values.SelectMany(x=>x).Count(x=>x.Owner=="ezapi.lifecycle")==7,"repeated directory installation idempotent");
Throws<ArgumentOutOfRangeException>(()=>LifecycleApi.Subscribe("test.mod","phase",(LifecyclePhase)99,_=>{}),"unknown phase rejected");
Throws<ArgumentException>(()=>LifecycleApi.Subscribe("test.mod","\n",LifecyclePhase.BeforeLoad,_=>{}),"control callback ID rejected");
Throws<ArgumentNullException>(()=>LifecycleApi.Subscribe("test.mod","null",LifecyclePhase.BeforeLoad,null!),"null callback rejected");
Throws<InvalidOperationException>(()=>Task.Run(()=>LifecycleApi.Subscribe("test.mod","thread",LifecyclePhase.BeforeLoad,_=>{})).GetAwaiter().GetResult(),"worker thread rejected");
var phases=new List<string>();var leases=new List<IDisposable>();
foreach(var phase in Enum.GetValues<LifecyclePhase>()){var captured=phase;leases.Add(LifecycleApi.Subscribe("test.mod",phase.ToString(),phase,_=>phases.Add(captured.ToString())));}
Throws<ArgumentException>(()=>LifecycleApi.Subscribe("test.mod","BeforeLoad",LifecyclePhase.AfterNight,_=>{}),"duplicate ID rejected even for different phase");
Fire("LoadGame",()=>phases.Add("native load"));
Check(phases.SequenceEqual(new[]{"BeforeLoad","native load","AfterLoad"}),"load prefix and original-only postfix ordered");phases.Clear();
Fire("EndNight",()=>phases.Add("native night"));
Check(phases.SequenceEqual(new[]{"BeforeNight","native night","AfterNight","NightAttemptFinished"}),"night before after and attempt ordered");phases.Clear();
foreach(var entry in new[]{("FireOnHandlingNightlyServicesEarly","BeforeNightServices",true),("FireOnHandlingNightlyServicesLate","AfterNightServices",false),("FireOnGoingSleepLate","AfterSleep",false),("FireOnGameLoadedLate","AfterGameLoadedLate",false),("EndDay","AfterEndDay",false)}){
 Fire(entry.Item1,()=>phases.Add("original"));Check(phases.SequenceEqual(entry.Item3?new[]{entry.Item2,"original"}:new[]{"original",entry.Item2}),"exact timing "+entry.Item2);phases.Clear();
}
void Skip(string name){new Harmony("other.skip").Patch(Target(name),prefix:new HarmonyMethod(typeof(OtherPatch).GetMethod(nameof(OtherPatch.Skip))!));}
Skip("EndNight");Fire("EndNight",()=>throw new Exception("must skip"));
Check(phases.SequenceEqual(new[]{"BeforeNight","NightAttemptFinished"}),"skipped original emits attempt but no AfterNight");phases.Clear();
Skip("LoadGame");Fire("LoadGame");Check(phases.SequenceEqual(new[]{"BeforeLoad"}),"skipped load has no AfterLoad");phases.Clear();
foreach(var name in new[]{"FireOnHandlingNightlyServicesLate","FireOnGoingSleepLate","FireOnGameLoadedLate","EndDay"}){Skip(name);Fire(name);Check(phases.Count==0,"skipped postfix phase "+name);}
new Harmony("other.skip").UnpatchSelf();
var nativeException=new InvalidOperationException("native failure");Exception? caught=null;
try{Fire("EndNight",()=>throw nativeException);}catch(Exception e){caught=e;}
Check(ReferenceEquals(caught,nativeException),"night finalizer preserves original exception identity");
Check(phases.SequenceEqual(new[]{"BeforeNight"}),"exception does not claim after-night or finished attempt");
foreach(var lease in leases)lease.Dispose();phases.Clear();
// 前一订阅者读取快照后移除物品，后续订阅者必须得到新的完整库存。
PlayerStore.instance!.Found.Add(new GameItem{uniqueId=451});
using(var writer=LifecycleApi.Subscribe("a.owner","remove-material",LifecyclePhase.BeforeNight,ctx=>{
 Check(ctx.Items.Count==1&&ReferenceEquals(ctx.Items,ctx.Items),"one callback reuses its own lazy snapshot");
 PlayerStore.instance.Found.Clear();
}))
using(var reader=LifecycleApi.Subscribe("b.owner","inspect-current",LifecyclePhase.BeforeNight,ctx=>{
 Check(ctx.ItemsComplete&&ctx.Items.Count==0&&ctx.ItemsError is null,"later subscriber cannot receive removed material from earlier callback snapshot");
}))Fire("EndNight");
// 同一回调中扫描失败也不能污染后续回调的重试机会。
PlayerStore.instance.Found.Add(null!);
using(var repair=LifecycleApi.Subscribe("a.owner","repair-graph",LifecyclePhase.BeforeNight,ctx=>{
 Check(!ctx.ItemsComplete,"first callback observes incomplete graph");PlayerStore.instance.Found.Clear();
}))
using(var reader=LifecycleApi.Subscribe("b.owner","read-repaired",LifecyclePhase.BeforeNight,ctx=>{
 Check(ctx.ItemsComplete&&ctx.Items.Count==0&&ctx.ItemsError is null,"later callback recaptures after earlier incomplete graph");
}))Fire("EndNight");
LifecycleContext? saved=null;PlayerStore.instance!.Found.Add(new GameItem{uniqueId=10});
using var scope=LifecycleApi.Subscribe("test.mod","scope",LifecyclePhase.BeforeLoad,ctx=>{
 saved=ctx;Check(ctx.IsCurrent,"context current during callback");Check(ctx.Run.Day==5&&ctx.Run.SlotId==1&&ctx.Run.RunId=="test","run day slot and ID captured");
 Check(ctx.ItemsComplete&&ctx.Items.Count==1&&ctx.ItemsError is null,"before-load inventory captured without binding loaded GameSession");
});
Fire("LoadGame");Check(saved is not null&&!saved.IsCurrent,"context expires on synchronous callback return");Throws<InvalidOperationException>(()=>_ =saved!.Store,"expired native store inaccessible");scope.Dispose();
StoreStation.FailDay=true;
using(var unavailable=LifecycleApi.Subscribe("test.mod","day",LifecyclePhase.BeforeLoad,ctx=>Check(ctx.Run.Day==-1,"unavailable day explicit minus one")))Fire("LoadGame");StoreStation.FailDay=false;
PlayerStore.instance.Found.Add(new GameItem{uniqueId=10});
using(var invalidItems=LifecycleApi.Subscribe("test.mod","items",LifecyclePhase.BeforeLoad,ctx=>Check(!ctx.ItemsComplete&&ctx.Items.Count==0&&ctx.ItemsError is not null,"invalid graph reports incomplete instead of truncated success")))Fire("LoadGame");PlayerStore.instance.Found.Clear();
var order=new List<int>();
using(var last=LifecycleApi.Subscribe("test.mod","last",LifecyclePhase.BeforeLoad,_=>order.Add(3),int.MaxValue))
using(var middle=LifecycleApi.Subscribe("test.mod","middle",LifecyclePhase.BeforeLoad,_=>order.Add(2)))
using(var first=LifecycleApi.Subscribe("test.mod","first",LifecyclePhase.BeforeLoad,_=>order.Add(1),int.MinValue))Fire("LoadGame");
Check(order.SequenceEqual(new[]{1,2,3}),"full signed int order supported");
order.Clear();
using(var z=LifecycleApi.Subscribe("z.owner","z",LifecyclePhase.BeforeLoad,_=>order.Add(3)))
using(var b=LifecycleApi.Subscribe("a.owner","b",LifecyclePhase.BeforeLoad,_=>order.Add(2)))
using(var a=LifecycleApi.Subscribe("a.owner","a",LifecyclePhase.BeforeLoad,_=>order.Add(1)))Fire("LoadGame");
Check(order.SequenceEqual(new[]{1,2,3}),"ties deterministic by owner and ID");
var recursive=0;
using(var r=LifecycleApi.Subscribe("test.mod","reentrant",LifecyclePhase.BeforeLoad,_=>{recursive++;Fire("LoadGame");}))Fire("LoadGame");
Check(recursive==1,"same phase recursive dispatch suppressed");
var failures=0;var survivors=0;
using(var bad=LifecycleApi.Subscribe("a.owner","bad",LifecyclePhase.BeforeLoad,_=>{failures++;throw new Exception("subscriber");}))
using(var good=LifecycleApi.Subscribe("b.owner","good",LifecyclePhase.BeforeLoad,_=>survivors++)){Fire("LoadGame");Fire("LoadGame");}
Check(failures==1&&survivors==2,"exception removes bad subscriber and preserves others");
var staleCalls=0;
using(var invalidate=LifecycleApi.Subscribe("a.owner","invalidate",LifecyclePhase.BeforeLoad,_=>GameSession.Invalidate()))
using(var stale=LifecycleApi.Subscribe("b.owner","stale",LifecyclePhase.BeforeLoad,_=>staleCalls++))Fire("LoadGame");
Check(staleCalls==0,"session revision invalidation aborts current phase");
using(var change=LifecycleApi.Subscribe("a.owner","change",LifecyclePhase.BeforeLoad,_=>PlayerStore.instance!.runID="changed"))
using(var stale=LifecycleApi.Subscribe("b.owner","stale",LifecyclePhase.BeforeLoad,_=>staleCalls++))Fire("LoadGame");Check(staleCalls==0,"run identity mutation aborts remaining callbacks");
var victim=LifecycleApi.Subscribe("b.owner","victim",LifecyclePhase.BeforeLoad,_=>staleCalls++);
using(var dispose=LifecycleApi.Subscribe("a.owner","dispose",LifecyclePhase.BeforeLoad,_=>victim.Dispose()))Fire("LoadGame");Check(staleCalls==0,"disposed subscriber skipped within snapshot");
var old=LifecycleApi.Subscribe("test.mod","replace",LifecyclePhase.BeforeLoad,_=>staleCalls+=100);Restart();
using(var current=LifecycleApi.Subscribe("test.mod","replace",LifecyclePhase.BeforeLoad,_=>staleCalls++)){old.Dispose();Fire("LoadGame");}Check(staleCalls==1,"old lease cannot remove replacement generation");
using(var stop=LifecycleApi.Subscribe("a.owner","stop",LifecyclePhase.BeforeLoad,_=>LifecycleApi.Stop()))
using(var stale=LifecycleApi.Subscribe("b.owner","stale",LifecyclePhase.BeforeLoad,_=>staleCalls++))Fire("LoadGame");Check(staleCalls==1&&!LifecycleApi.GetRuntimeStatus().Ready,"stop aborts snapshot and unpatches owner");
Restart();
new Harmony("other.owner").Patch(Target("LoadGame"));LifecycleApi.Stop();Check(Harmony.GetPatchInfo(Target("LoadGame"))!.Owners.Contains("other.owner"),"shutdown preserves other Harmony owner");
new Harmony("other.owner").UnpatchSelf();
LifecycleApi.Start();Harmony.FailPatch=true;LifecycleApi.DirectoryReady();Harmony.FailPatch=false;Check(!LifecycleApi.GetRuntimeStatus().Ready,"patch failure disables whole lifecycle group");
LifecycleApi.DirectoryReady();Check(LifecycleApi.GetRuntimeStatus().Ready,"failed group installation can retry");
Harmony.Installed[Target("EndDay")].RemoveAll(p=>p.Owner=="ezapi.lifecycle");Check(!LifecycleApi.GetRuntimeStatus().Ready,"owner loss closes readiness gate");
LifecycleApi.DirectoryReady();Check(Harmony.Installed.Values.SelectMany(x=>x).Count(x=>x.Owner=="ezapi.lifecycle")==7,"partial owner loss retry removes old targets before reinstall");
LifecycleApi.Stop();LifecycleApi.Start();Harmony.FailTarget="EndDay";LifecycleApi.DirectoryReady();Harmony.FailTarget=null;
Check(!LifecycleApi.GetRuntimeStatus().Ready&&!Harmony.Installed.Values.SelectMany(x=>x).Any(x=>x.Owner=="ezapi.lifecycle"),"mid-install failure rolls back already installed targets");LifecycleApi.DirectoryReady();
var attemptsAfterInvalidation=0;
using(var changeNight=LifecycleApi.Subscribe("test.mod","change-night",LifecyclePhase.AfterNight,_=>PlayerStore.instance!.runID="night-changed"))
using(var attempt=LifecycleApi.Subscribe("test.mod","attempt",LifecyclePhase.NightAttemptFinished,_=>attemptsAfterInvalidation++))Fire("EndNight");
Check(attemptsAfterInvalidation==0,"after-night identity change aborts subsequent finished phase");

Restart();Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Alias=true;LifecycleApi.Stop();LifecycleApi.Start();LifecycleApi.DirectoryReady();Check(!LifecycleApi.GetRuntimeStatus().Ready,"shared native entry disables group");Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Alias=false;
LifecycleApi.DirectoryReady();Check(LifecycleApi.GetRuntimeStatus().Ready,"alias failure recoverable before gameplay");
LifecycleApi.Stop();Il2CppInterop.Common.Il2CppInteropUtils.Missing=true;LifecycleApi.Start();LifecycleApi.DirectoryReady();Check(!LifecycleApi.GetRuntimeStatus().Ready,"missing generated metadata disables group");Il2CppInterop.Common.Il2CppInteropUtils.Missing=false;
LifecycleApi.DirectoryReady();
var limits=Enumerable.Range(0,256).Select(i=>LifecycleApi.Subscribe("test.mod","limit"+i,LifecyclePhase.BeforeLoad,_=>{})).ToArray();Throws<InvalidOperationException>(()=>LifecycleApi.Subscribe("test.mod","overflow",LifecyclePhase.BeforeLoad,_=>{}),"bounded subscription count");foreach(var lease in limits)lease.Dispose();
LifecycleApi.Stop();GameSession.Stop();
var report=new{passed=cases.Count,cases,nativeRuntimeTest=false,scope="real Lifecycle, GameSession, NativeHooks and InventoryServices; simulated Harmony and IL2CPP targets"};
File.WriteAllText("work/ezapi-lifecycle-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));
public static class OtherPatch {public static bool Skip()=>false;}

using EZapi;
using Il2Cpp;
using System.Globalization;
using System.Text.Json;

var cases=new List<string>();
void Check(bool ok,string name) { if(!ok)throw new Exception(name);cases.Add(name); }
bool Throws(Action action,string code) {
    try { action();return false; }catch(Exception e) { return e.Message.Contains(code,StringComparison.Ordinal); }
}
void Fire(GameEvent phase)=>ModHook.Fire(phase.ToString());
int HookCount()=>ModHook.Handlers.Values.Sum(v=>v.Count);

Check(!RunData.IsAvailable&&!RunData.TryOpen("test.mod",out _),"run unavailable before initialization");
GameEvents.Start();GameEvents.Start();
Check(HookCount()==18,"official event hooks installed once");
Check(!RunData.IsAvailable&&Throws(()=>RunData.Open("test.mod"),"EZAPI_RUN_NOT_READY"),"main menu cannot open run data");
Check(Throws(()=>GameEvents.Subscribe("../mod",GameEvent.GameLoadedInit,_=>{}),"EZAPI_RUN_MOD_ID"),"event owner validation");
Check(Throws(()=>GameEvents.Subscribe("test.mod",(GameEvent)999,_=>{}),"phase"),"unknown event enum rejected");
Check(Task.Run(()=>Throws(()=>GameEvents.Subscribe("test.mod",GameEvent.GameLoadedInit,_=>{}),"EZAPI_RUN_THREAD")).Result,"subscriptions require main thread");
var order=new List<int>();
using var first=GameEvents.Subscribe("test.mod",GameEvent.GameLoadedNormal,_=>order.Add(1));
using var throwing=GameEvents.Subscribe("test.other",GameEvent.GameLoadedNormal,_=>throw new Exception("subscriber failed"));
using var last=GameEvents.Subscribe("test.mod",GameEvent.GameLoadedNormal,_=>order.Add(2));
Check(!GameSession.ObserveStoreReady(),"scene ready cannot bind absent player");
PlayerStore.instance=new();
Check(GameSession.ObserveStoreReady()&&RunData.IsAvailable&&GameEvents.GetRecentEvents().Count==0,"new game scene establishes identity without invented loaded event");
var readyIdentity=GameSession.Identity;
Check(GameSession.ObserveStoreReady()&&GameSession.Identity==readyIdentity,"scene readiness is idempotent and preserves valid epoch");
Fire(GameEvent.GameLoadedInit);Fire(GameEvent.GameLoadedNormal);
Check(RunData.IsAvailable&&order.SequenceEqual(new[]{1,2}),"loaded event activates run and isolates subscribers");
Fire(GameEvent.GameLoadedNormal);
Check(order.SequenceEqual(new[]{1,2,1,2})&&MelonLoader.MelonLogger.Errors.Count==1,"failed subscriber removed without repeating errors");
first.Dispose();Fire(GameEvent.GameLoadedNormal);
Check(order.SequenceEqual(new[]{1,2,1,2,2}),"disposed subscription skipped");
Check(GameEvents.GetRecentEvents().Count==4&&GameEvents.GetRecentEvents().Last().Sequence>0,"event history contains managed sequence snapshots");
using var recursive=GameEvents.Subscribe("test.recursive",GameEvent.ShutterOpenedEarly,_=>Fire(GameEvent.ShutterOpenedEarly));
Fire(GameEvent.ShutterOpenedEarly);
Check(GameEvents.GetRecentEvents().Count==5,"same-phase recursive dispatch blocked");
for(var i=0;i<70;i++)Fire(GameEvent.WakingUpLate);
Check(GameEvents.GetRecentEvents().Count==64,"event history bounded to 64 snapshots");

var data=RunData.Open("test.mod");var other=RunData.Open("test.mod2");
Check(!data.Contains("missing")&&data.GetString("missing","fallback")=="fallback"&&PlayerStore.instance.modData is null,"missing reads do not create native dictionary");
data.SetString("title","中文标题");other.SetString("title","other");
Check(data.GetString("title")=="中文标题"&&other.GetString("title")=="other","mod namespaces isolated");
PlayerStore.instance.modData!["foreign:entry"]="preserve";
Check(data.GetKeys().SequenceEqual(new[]{"title"}),"key listing excludes foreign entries");
data.SetInt("number",123);data.SetLong("large",9007199254740993);data.SetBool("flag",false);
Check(data.GetInt("number")==123&&data.GetLong("large")==9007199254740993&&!data.GetBool("flag",true),"int long and false bool values round trip");
var culture=CultureInfo.CurrentCulture;
try { CultureInfo.CurrentCulture=new("fr-FR");data.SetDouble("double",1.25);data.SetFloat("float",2.5f);
    Check(data.GetString("double")=="1.25"&&data.GetDouble("double")==1.25&&data.GetFloat("float")==2.5f,"numbers use invariant culture");
} finally { CultureInfo.CurrentCulture=culture; }
Check(Throws(()=>data.SetDouble("double",double.PositiveInfinity),"EZAPI_RUN_NUMBER")&&data.GetDouble("double")==1.25,"nonfinite write rejected before changing old value");
data.SetString("badNumber","NaN");Check(data.GetDouble("badNumber",99)==99,"invalid numeric read returns explicit fallback");
Check(Throws(()=>data.SetString("../escape","x"),"EZAPI_RUN_KEY"),"keys cannot escape namespace");
data.SetString("old","old");
Check(Throws(()=>data.SetString("old",new string('中',6000)),"EZAPI_RUN_LIMIT")&&data.GetString("old")=="old","UTF8 value quota preserves old value");
data.SetJson("state",new State{Count=7,Name="状态"},2);
Check(data.TryGetJson<State>("state",2,out var state)&&state!.Count==7&&state.Name=="状态","versioned managed JSON round trip");
var oldJson=data.GetString("state");
Check(!data.TryGetJson<State>("state",3,out _)&&data.GetString("state")==oldJson,"future or different JSON version is preserved");
data.SetString("corrupt","{bad");Check(!data.TryGetJson<State>("corrupt",1,out _),"corrupt JSON returns false");
data.SetString("duplicate","{\"version\":1,\"version\":2,\"data\":{}}");
Check(!data.TryGetJson<State>("duplicate",2,out _),"duplicate JSON envelope field rejected");
data.SetString("wrong","{\"version\":1,\"data\":{\"Count\":\"oops\"}}");
Check(!data.TryGetJson<State>("wrong",1,out _),"incompatible JSON data rejected");
Check(Task.Run(()=>Throws(()=>data.SetInt("number",4),"EZAPI_RUN_THREAD")).Result&&data.GetInt("number")==123,"off-thread access cannot mutate run data");
var limited=RunData.Open("test.limited");
for(int i=0;i<128;i++)limited.SetInt("k"+i,i);
Check(Throws(()=>limited.SetInt("overflow",1),"EZAPI_RUN_LIMIT")&&limited.GetKeys().Count==128,"per-mod key quota is atomic");
limited.Clear();for(int i=0;i<4;i++)limited.SetString("k"+i,new string('x',16000));
Check(Throws(()=>limited.SetString("overflow",new string('x',2000)),"EZAPI_RUN_LIMIT")&&!limited.Contains("overflow"),"per-mod aggregate byte quota is atomic");
var removed=data.Clear();
Check(removed>0&&data.GetKeys().Count==0&&other.GetString("title")=="other"&&PlayerStore.instance.modData["foreign:entry"]=="preserve","clear removes only exact owner namespace");
Check(!data.Remove("missing"),"removing absent key is harmless");

data.SetInt("counter",5);var firstIdentity=data.Identity;
Fire(GameEvent.GameLoadedInit);
Check(!data.IsCurrent&&Throws(()=>data.SetInt("counter",99),"EZAPI_RUN_STALE"),"same player and run reload invalidates old handle");
data=RunData.Open("test.mod");
Check(data.GetInt("counter")==5&&data.Identity.Epoch!=firstIdentity.Epoch,"new handle resolves current native data without managed value cache");
GameSession.Invalidate();
Check(!RunData.TryOpen("test.mod",out _)&&!data.IsCurrent,"scene invalidation blocks access until next official gameplay event");
Fire(GameEvent.ReturningStoreLate);var returned=RunData.Open("test.mod");
Check(returned.GetInt("counter")==5,"return event reestablishes identity from current player");
var oldPlayer=PlayerStore.instance;
PlayerStore.instance=new(){saveSlotId=2,runID="run2"};
Check(!returned.IsCurrent&&Throws(()=>returned.SetInt("counter",99),"EZAPI_RUN_STALE"),"player replacement prevents cross-slot write");
Fire(GameEvent.GameLoadedInit);var slot2=RunData.Open("test.mod");
Check(slot2.GetInt("counter")==0&&oldPlayer.modData!["EZapi.RunData/v1/test.mod/counter"]=="5","new slot is empty and previous run retained");
slot2.SetInt("counter",2);PlayerStore.instance.runID="run3";
Check(!slot2.IsCurrent,"run identity change invalidates same-pointer handle");
Fire(GameEvent.GameLoadedInit);PlayerStore.instance!.modData=new();
Check(RunData.Open("test.mod").GetInt("counter")==0,"reset native modData does not revive cached values");

int lateCalls=0;
using var invalidator=GameEvents.Subscribe("test.scene",GameEvent.LeavingStoreEarly,_=>GameSession.Invalidate());
using var later=GameEvents.Subscribe("test.scene",GameEvent.LeavingStoreEarly,_=>lateCalls++);
Fire(GameEvent.LeavingStoreEarly);
Check(lateCalls==0,"context invalidation stops remaining subscribers from receiving stale run");
GameEvents.Stop();
Check(HookCount()==0&&!RunData.IsAvailable,"shutdown pairs all official delegates and clears run gate");
ModHook.ThrowAddAt="WakingUpLate";
Check(Throws(()=>GameEvents.Start(),"partial add failure")&&HookCount()==0,"partial native subscription failure rolls back all same delegates");
ModHook.ThrowAddAt=null;GameEvents.Start();
Check(HookCount()==18,"event startup recovers after partial failure");
ModHook.ThrowRemoveAt="GameLoadedLate";GameEvents.Stop();
Check(HookCount()==1&&!RunData.IsAvailable,"failed removal retains one inert native callback");
Fire(GameEvent.GameLoadedLate);Check(!RunData.IsAvailable,"retained callback cannot reopen stopped session");
ModHook.ThrowRemoveAt=null;GameEvents.Stop();GameEvents.Start();
Check(HookCount()==18,"failed unsubscription can be cleaned up and restarted without duplicates");
GameEvents.Stop();

var report=new{passed=cases.Count,cases,nativeRuntimeTest=false,
    scope="real lifecycle subscriptions/session/RunData sources; native delegates, PlayerStore and modData replaced by stubs; no save file IO"};
Directory.CreateDirectory("work");
File.WriteAllText("work/ezapi-session-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));
public sealed class State { public int Count {get;set;}public string Name {get;set;}=""; }

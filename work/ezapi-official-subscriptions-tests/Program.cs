using EZapi;
using Il2Cpp;
using MelonLoader;
using System.Text.Json;

var checks=new List<object>();
void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(!ok)throw new Exception(name);}
var required=new[]{"inventory","endpoint","power","lifecycle","reading","dialogue","phone","module","items","item-locale","effects","presentation","item-stock","npc-stock"};
void Dispatch()
{
    Calls.Trace.Clear();ModHook.FireDirectory();
    foreach(var name in required)Check(Calls.Trace.Count(call=>call==name)==1,"official directory dispatch invokes exactly one "+name+" readiness/apply callback");
    Check(Calls.Trace.IndexOf("items")>Calls.Trace.IndexOf("module"),"module host is ready before item factories apply");
    Check(Calls.Trace.IndexOf("item-locale")>Calls.Trace.IndexOf("items"),"display refresh is installed after item factories apply");
}
Check(!OfficialHookSubscriptions.IsReady,"unstarted official event host is unavailable");
OfficialHookSubscriptions.Start();OfficialHookSubscriptions.Start();
Check(OfficialHookSubscriptions.IsReady&&OfficialHookSubscriptions.ClientVisitReady,"duplicate start preserves required and optional subscriptions");
Dispatch();ModHook.FireEarly();ModHook.FireLate();ModHook.OnGenerateCustomerVeryLate!.Invoke(new());
Check(Calls.Trace.TakeLast(3).SequenceEqual(new[]{"early","late","visit"}),"real subscriptions dispatch tooltip and customer callbacks");
Calls.ThrowOnEarly=true;ModHook.FireEarly();ModHook.FireLate();Calls.ThrowOnEarly=false;
Check(MelonLogger.Errors.Count==1&&Calls.Trace.Last()=="late","one failing tooltip callback is logged and later events still dispatch");
OfficialHookSubscriptions.Stop();OfficialHookSubscriptions.Stop();Calls.Trace.Clear();ModHook.FireDirectory();ModHook.FireEarly();ModHook.FireLate();
Check(Calls.Trace.Count==0&&!OfficialHookSubscriptions.IsReady&&!OfficialHookSubscriptions.ClientVisitReady,"repeated stop removes exact subscribed delegates");
ModHook.RefuseCustomerHook=true;OfficialHookSubscriptions.Start();
Check(OfficialHookSubscriptions.IsReady&&!OfficialHookSubscriptions.ClientVisitReady,"optional customer hook failure preserves required event host");
Dispatch();
ModHook.RefuseCustomerHook=false;OfficialHookSubscriptions.Start();
Check(OfficialHookSubscriptions.ClientVisitReady,"later start can recover optional subscription without duplicating required ones");
Dispatch();OfficialHookSubscriptions.Stop();
Console.WriteLine(JsonSerializer.Serialize(new{suite="official-subscriptions",passed=checks.Count,failed=0,checks,nativeRuntimeVerified=false,
    scope="production OfficialHookSubscriptions linked; native event/delegate and downstream API boundaries stubbed; covers actual startup wiring, required reading readiness, idempotent subscribe/unsubscribe, callback isolation and optional recovery"}));

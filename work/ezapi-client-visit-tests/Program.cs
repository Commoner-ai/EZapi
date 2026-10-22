using EZapi;
using Il2Cpp;
using System.Reflection;
using System.Text.Json;

var results = new List<object>();
var failures = 0;
var checks = 0;
void Check(bool passed, string name)
{
    checks++;
    results.Add(new { name, passed });
    if (!passed) { failures++; Console.Error.WriteLine("FAILED: " + name); }
}
void Fresh()
{
    GameSession.OnThread = true;
    GameSession.Identity = new(12, "run-a", 1);
    GameBuildFingerprint.IsKnown = true;
    OfficialHookSubscriptions.ClientVisitReady = true;
    DialogueApi.Reset();
    MelonLoader.MelonLogger.Warnings.Clear();
    StoreClientListDict.Factory = actor => new() { identifier = actor };
    ClientVisitApi.Stop();
    ClientVisitApi.Start();
}
DialogueScript Script(string owner = "author") => DialogueContract.Freeze(owner,
    new DialogueDefinition(owner + ".dialogue", "Speaker", "Root", new[]
    {
        new DialogueChoice(owner + ".choice", "Continue", "Preview", _ => "Done")
    }));
PresentationResult Queue(string request = "author.visit", string actor = "known.actor", string sprite = "portrait") =>
    ClientVisitApi.Queue("author", request, "run-a", 12, actor, sprite, Script());

Fresh();
GameBuildFingerprint.IsKnown = false;
Check(!ClientVisitApi.IsAvailable && Queue().Status == PresentationStatus.Unavailable, "unknown game build fails closed");
Fresh();
OfficialHookSubscriptions.ClientVisitReady = false;
Check(!ClientVisitApi.IsAvailable && Queue().Status == PresentationStatus.Unavailable, "missing official customer hook fails closed");

Fresh();
Check(Queue("bad").Status == PresentationStatus.Invalid, "request ID must belong to owner");
Check(ClientVisitApi.Queue("author", "author.null", "run-a", 12, "known.actor", "portrait", null!).Status == PresentationStatus.Invalid, "null script refused");
Check(ClientVisitApi.Queue("author", "author.path", "run-a", 12, "known.actor", "../portrait", Script()).Status == PresentationStatus.Invalid, "sprite paths refused");
Check(ClientVisitApi.Queue("author", "author.owner", "run-a", 12, "known.actor", "portrait", Script("other")).Status == PresentationStatus.Invalid, "foreign script owner refused");
Check(ClientVisitApi.Queue("author", "author.slot", "run-a", 13, "known.actor", "portrait", Script()).Status == PresentationStatus.Conflict, "wrong save slot refused");

Fresh();
Check(Queue().Status == PresentationStatus.Queued, "queue accepts valid request");
Check(Queue().Status == PresentationStatus.Conflict, "duplicate owner and request is rejected");
Check(ClientVisitApi.Status("author", "author.visit").Status == PresentationStatus.Queued, "queued status is observable");
var unknown = ClientVisitApi.Status("author", "author.unknown");
Check(unknown.Status == PresentationStatus.Closed && unknown.Reason == "EZAPI_CLIENT_VISIT_UNKNOWN", "unknown status is explicit");

Fresh();
StoreClientListDict.Factory = _ => null;
Queue();
ClientVisitApi.OnGenerateCustomer(new());
Check(ClientVisitApi.Status("author", "author.visit").Status == PresentationStatus.Invalid, "unknown actor factory is rejected without native insertion");

Fresh();
var originalArrival = 0;
var originalDismiss = 0;
StoreClient? created = null;
StoreClientListDict.Factory = actor => created = new StoreClient
{
    identifier = actor,
    OnArrival = new Il2CppSystem.Action(() => originalArrival++),
    OnDismiss = new Il2CppSystem.Action(() => originalDismiss++)
};
Queue();
var manager = new StoreClientManager();
ClientVisitApi.OnGenerateCustomer(manager);
Check(manager.clientStack.Count == 1 && manager.clientStack[0] == created, "official hook inserts one native blueprint through AddNextClient");
Check(created?.spriteName == "portrait", "sprite key is applied and read back");
Check(ClientVisitApi.Status("author", "author.visit").Status == PresentationStatus.Queued, "native stack readback keeps queued status");
created!.OnArrival!.Invoke();
Check(originalArrival == 1 && ClientVisitApi.Status("author", "author.visit").Status == PresentationStatus.Arrived, "arrival callback is composed and status advances");
DialogueApi.Opens.Enqueue(new(PresentationStatus.Conflict, "EZAPI_DIALOGUE_OCCUPIED"));
ClientVisitApi.Maintain();
Check(DialogueApi.OpenCalls == 1, "occupied dialogue is deferred without duplicate queueing");
Thread.Sleep(410);
ClientVisitApi.Maintain();
Check(DialogueApi.OpenCalls == 2 && ClientVisitApi.Status("author", "author.visit").Status == PresentationStatus.Arrived, "dialogue opens after the UI becomes available");
created.OnDismiss!.Invoke();
ClientVisitApi.Maintain();
Check(originalDismiss == 1 && DialogueApi.CloseCalls == 1 && ClientVisitApi.Status("author", "author.visit").Status == PresentationStatus.Dismissed, "dismiss callback is preserved and closes only the visit dialogue");
Check(created.OnArrival?.GetInvocationList().Length == 1 && created.OnDismiss?.GetInvocationList().Length == 1, "visit callbacks are detached after dismissal");

Fresh();
var managedFactoryCalls = 0;
var managedOpenedCalls = 0;
var managedDismissCalls = 0;
var managedNativeArrivalCalls = 0;
var managedNativeDismissCalls = 0;
var managedClient = new StoreClient
{
    identifier = "author.vendor",
    OnArrival = new Il2CppSystem.Action(() => managedNativeArrivalCalls++),
    OnDismiss = new Il2CppSystem.Action(() => managedNativeDismissCalls++)
};
StoreClientListDict.Factory = _ => throw new InvalidOperationException("managed visit must use its factory");
var managedResult = ClientVisitApi.QueueManaged("author", "author.managed", "run-a", 12,
    "author.vendor", "stockist", Script(),
    () => { managedFactoryCalls++; return managedClient; },
    () => managedDismissCalls++,
    () => managedOpenedCalls++);
Check(managedResult.Status == PresentationStatus.Queued, "managed visit accepts scoped factory and lifecycle callbacks");
manager = new();
ClientVisitApi.OnGenerateCustomer(manager);
Check(managedFactoryCalls == 1 && manager.clientStack.Count == 1 && manager.clientStack[0] == managedClient,
    "managed visit factory bypasses native blueprint lookup and queues its client once");
managedClient.OnArrival!.Invoke();
Check(managedNativeArrivalCalls == 1 && ClientVisitApi.Status("author", "author.managed").Status == PresentationStatus.Arrived,
    "managed visit preserves the client factory arrival callback");
ClientVisitApi.Maintain();
Check(managedOpenedCalls == 1, "managed visit invokes opened callback after dialogue opens");
managedClient.OnDismiss!.Invoke();
ClientVisitApi.Maintain();
Check(managedNativeDismissCalls == 1 && managedDismissCalls == 1,
    "managed visit preserves native dismiss callback and invokes owner cleanup once");
Check(managedClient.OnArrival?.GetInvocationList().Length == 1 && managedClient.OnDismiss?.GetInvocationList().Length == 1,
    "managed visit detaches only EZapi callbacks after dismissal");

Fresh();
Check(ClientVisitApi.QueueManaged("author", "author.null-managed", "run-a", 12,
    "author.vendor", "stockist", Script(), null!, () => { }, () => { }).Status == PresentationStatus.Invalid,
    "managed visit refuses incomplete callback ownership");
var managedFactoryReference = new Func<StoreClient>(() => null!);
ClientVisitApi.QueueManaged("author", "author.failed-managed", "run-a", 12,
    "author.vendor", "stockist", Script(), managedFactoryReference, () => { }, () => { });
StoreClientListDict.Factory = _ => null;
ClientVisitApi.OnGenerateCustomer(new());
ClientVisitApi.Maintain();
var managedVisit = typeof(ClientVisitApi).GetField("Visits", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
var failedManagedVisit = ((System.Collections.IDictionary)managedVisit)[("author", "author.failed-managed")]!;
Check(ClientVisitApi.Status("author", "author.failed-managed").Status == PresentationStatus.Invalid &&
      failedManagedVisit.GetType().GetField("ClientFactory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(failedManagedVisit) is null,
    "managed factory failure releases captured callbacks without a native client");

Fresh();
Queue();
manager = new() { ThrowAfterAdd = true };
ClientVisitApi.OnGenerateCustomer(manager);
Check(ClientVisitApi.Status("author", "author.visit").Status == PresentationStatus.Queued, "exception after observable insertion preserves queued state");

Fresh();
Queue();
manager = new() { ThrowBeforeAdd = true };
ClientVisitApi.OnGenerateCustomer(manager);
var attemptsBeforeRetry = manager.clientStack.Count;
ClientVisitApi.OnGenerateCustomer(manager);
Check(ClientVisitApi.Status("author", "author.visit").Status == PresentationStatus.Indeterminate && attemptsBeforeRetry == 0 && manager.clientStack.Count == 0,
    "failed unobserved insertion is indeterminate and is never retried");

Fresh();
Queue();
var beforeEpoch = ClientVisitApi.Status("author", "author.visit");
GameSession.Identity = new(12, "run-a", 2);
var afterEpoch = ClientVisitApi.Status("author", "author.visit");
Check(beforeEpoch.Status == PresentationStatus.Queued && afterEpoch.Status == PresentationStatus.Dismissed, "same run and slot after reload invalidates old epoch");

Fresh();
Queue();
manager = new();
ClientVisitApi.OnGenerateCustomer(manager);
created = manager.clientStack[0];
created.RejectCallbackAccess = true;
GameSession.Identity = new(12, "run-a", 2);
ClientVisitApi.Maintain();
var report = ClientVisitApi.Report();
Check(!(bool)report.GetType().GetProperty("faulted")!.GetValue(report)!, "stale native callback wrappers are dropped without touching their pointers");

Fresh();
var accepted = 0;
for (var i = 0; i < 128; i++) if (Queue($"author.visit{i}").Status == PresentationStatus.Queued) accepted++;
Check(accepted == 128, "bounded queue accepts up to its documented limit");
Check(Queue("author.visit-overflow").Status == PresentationStatus.Conflict, "queue capacity is bounded");

Console.WriteLine(JsonSerializer.Serialize(new
{
    suite = "client-visit-api",
    passed = checks - failures,
    failed = failures,
    cases = results,
    nativeRuntimeVerified = false,
    scope = "real ClientVisitApi and PresentationResult; isolated IL2CPP/native customer and dialogue substitutes"
}, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 ? 0 : 1;

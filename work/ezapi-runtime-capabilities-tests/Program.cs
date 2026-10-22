using EZapi;
using MelonLoader;
using System.Reflection;

var cases = new List<(string Name, Func<bool> Test)>();
void Add(string name, Func<bool> test) => cases.Add((name, test));

void Reset()
{
    GameSession.OnThread = true;
    GameSession.HasRun = true;
    OfficialHookSubscriptions.IsReady = true;
    ModuleApi.Throw = false;
    ModuleApi.Ready = true;
    EffectApi.IsNativeRegistrationReady = true;
    InventoryRuntime.TransferReady = true;
    MachineRuntimeApi.RuntimeInstalled = true;
    InventoryEndpointApi.IsAvailable = true;
    ExternalPowerApi.IsAvailable = true;
    DialogueApi.IsAvailable = true;
    ReadingApi.IsAvailable = true;
    PhoneApi.IsAvailable = true;
    ClientVisitApi.IsAvailable = true;
    SupplierApi.Ready = true;
    ItemStockRuntime.IsAvailable=true;
    NpcStockRuntime.IsAvailable=true;
    typeof(GameBuildFingerprint).GetField("_known", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
    MelonLogger.Warnings.Clear();
}

bool AllFalse(RuntimeCapabilitySnapshot snapshot) =>
    typeof(RuntimeCapabilitySnapshot).GetProperties().Where(p => p.PropertyType == typeof(bool))
        .All(p => !(bool)p.GetValue(snapshot)!);

Add("off-thread query fails closed without native probes", () => {
    Reset();
    GameSession.OnThread = false;
    return AllFalse(RuntimeCapabilities.Current);
});

Add("current run controls run-scoped capabilities", () => {
    Reset();
    GameSession.HasRun = false;
    var value = RuntimeCapabilities.Current;
    return !value.RunDataStaging && !value.DirectInventoryRead && !value.WholeTransferPreview && !value.InventoryTransfer &&
        value.BranchingDialogue && value.PhoneContacts && value.SupplierVisits;
});

Add("runtime hook readiness maps to the matching snapshot fields", () => {
    Reset();
    var ready = RuntimeCapabilities.Current;
    OfficialHookSubscriptions.IsReady = false;
    ModuleApi.Ready = false;
    EffectApi.IsNativeRegistrationReady = false;
    var unavailable = RuntimeCapabilities.Current;
    return ready.NativeItemDirectoryHook && ready.NativeModuleDirectoryHook && ready.NativeEffectRegistration &&
        !unavailable.NativeItemDirectoryHook && !unavailable.NativeModuleDirectoryHook && !unavailable.NativeEffectRegistration;
});

Add("inventory transfer requires the live inventory gate", () => {
    Reset();
    InventoryRuntime.TransferReady = false;
    var value = RuntimeCapabilities.Current;
    return !value.InventoryTransfer && value.DirectInventoryRead && value.WholeTransferPreview;
});

Add("operation-specific gates do not overstate unrelated features", () => {
    Reset();
    MachineRuntimeApi.RuntimeInstalled = false;
    InventoryEndpointApi.IsAvailable = false;
    ExternalPowerApi.IsAvailable = false;
    DialogueApi.IsAvailable = false;
    ReadingApi.IsAvailable = false;
    PhoneApi.IsAvailable = false;
    var value = RuntimeCapabilities.Current;
    return !value.MachineNightProcessing && !value.PersistentInventoryEndpoints && !value.ExternalPowerTransactions &&
        !value.BranchingDialogue && !value.ReadingWindow && !value.PhoneContacts && !value.SupplierVisits && value.RunDataStaging;
});

Add("a failed native probe disables only that capability and logs once", () => {
    Reset();
    ModuleApi.Throw = true;
    var first = RuntimeCapabilities.Current;
    var second = RuntimeCapabilities.Current;
    return !first.NativeModuleDirectoryHook && !second.NativeModuleDirectoryHook && first.RunDataStaging && MelonLogger.Warnings.Count == 1;
});

Add("current returns a fresh snapshot rather than a stale cache", () => {
    Reset();
    var first = RuntimeCapabilities.Current;
    PhoneApi.IsAvailable = false;
    var second = RuntimeCapabilities.Current;
    return first.PhoneContacts && !second.PhoneContacts && !ReferenceEquals(first, second);
});

Add("client visit gate is reflected in each fresh capability snapshot", () => {
    Reset();
    var ready = RuntimeCapabilities.Current;
    ClientVisitApi.IsAvailable = false;
    var unavailable = RuntimeCapabilities.Current;
    return ready.ClientVisits && !unavailable.ClientVisits && !ReferenceEquals(ready, unavailable);
});

Add("supplier capability follows supplier, visit and dialogue gates", () => {
    Reset();
    var ready = RuntimeCapabilities.Current;
    SupplierApi.Ready = false;
    var supplierUnavailable = RuntimeCapabilities.Current;
    SupplierApi.Ready = true;
    ItemStockRuntime.IsAvailable=true;
    NpcStockRuntime.IsAvailable=true;
    DialogueApi.IsAvailable = false;
    var dialogueUnavailable = RuntimeCapabilities.Current;
    DialogueApi.IsAvailable = true;
    ClientVisitApi.IsAvailable = false;
    var visitUnavailable = RuntimeCapabilities.Current;
    return ready.SupplierVisits && !supplierUnavailable.SupplierVisits && !dialogueUnavailable.SupplierVisits && !visitUnavailable.SupplierVisits;
});

Add("known build fingerprint uses the checked-in exact SHA256", () =>
    GameBuildFingerprint.Matches(GameBuildFingerprint.KnownSha256.ToLowerInvariant()) &&
    !GameBuildFingerprint.Matches("0000000000000000000000000000000000000000000000000000000000000000") &&
    !GameBuildFingerprint.Matches("not-a-hash"));

Add("night stock capability follows current runtime readiness",()=> {
 Reset();var before=RuntimeCapabilities.Current;ItemStockRuntime.IsAvailable=false;
 return before.NightShopStock && !RuntimeCapabilities.Current.NightShopStock;
});
Add("NPC stock capability follows live runtime gate",()=> {
 Reset();var first=RuntimeCapabilities.Current;NpcStockRuntime.IsAvailable=false;
 return first.NpcTradeStock && !RuntimeCapabilities.Current.NpcTradeStock;
});
Add("unimplemented capability flags remain false", () => {
    Reset();
    var value = RuntimeCapabilities.Current;
    return !value.NativeAmenityDirectoryHook && value.NightShopStock && value.NpcTradeStock && !value.DossierExpansion;
});

Add("compile-time manifest advertises the runtime snapshot API", () =>
    FrameworkInfo.GetCapabilities().Any(capability => capability.Id == "runtime.capability-snapshot") &&
    FrameworkInfo.GetCapabilities().Any(capability => capability.Id == "presentation.client-visits") &&
    FrameworkInfo.GetCapabilities().Any(capability => capability.Id == "presentation.suppliers"));

var results = new List<object>();
foreach (var test in cases)
{
    Reset();
    try { results.Add(new { name = test.Name, passed = test.Test() }); }
    catch (Exception error) { results.Add(new { name = test.Name, passed = false, error = error.ToString() }); }
}
var failed = results.Count(item => !(bool)item.GetType().GetProperty("passed")!.GetValue(item)!);
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
{
    suite = "runtime-capabilities",
    passed = results.Count - failed,
    failed,
    cases = results,
    realProductSources = new[] { "RuntimeCapabilities.cs", "FrameworkInfo.cs" },
    scope = "main-thread fail-closed behavior, current-run gates, live hook snapshots, operation gates, probe exception isolation, exact assembly fingerprint and unsupported-feature reporting; no native gameplay verification"
}));
if (failed != 0) Environment.Exit(1);

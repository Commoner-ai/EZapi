using EZapi;
using EZapi.Registration;

var tests = new List<(string Name, Func<bool> Run)>();
void Add(string name, Func<bool> run) => tests.Add((name, run));
void Reset() { WorkshopApi.Stop(); WorkshopApi.Start(); }
bool Is(SubmitResult result, SubmitStatus status) => result.Status == status;
WorkshopSnapshot ValidSnapshot() => new(100, false,
    new[] { new WorkshopGroup("示例分组", "Example group", 1) },
    new[] { new WorkshopEntry("ezapi.sample.workshop.node", "节点", "副标题", "说明", "前置说明", "10", "1", "可解锁", "解锁", false, true, true) });
var provider = new Func<WorkshopSnapshot>(ValidSnapshot);
var unlock = new Action<string>(_ => { });

Add("not ready rejects registration", () => { WorkshopApi.Stop(); return Is(WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.tab", "测试", "Test", provider, unlock), SubmitStatus.Invalid); });
Add("tab registration and opening use captured snapshot", () => { Reset(); var r = WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.tab", "测试", "Test", provider, unlock); return r.Accepted && WorkshopApi.OpenTab("ezapi.sample.tab") && WorkshopApi.IsVisible && WorkshopBrowser.LastSnapshot?.Entries.Count == 1; });
Add("chain registration opens through separate route", () => { Reset(); WorkshopApi.RegisterChain("ezapi.sample", "ezapi.sample.chain", "链", "Chain", provider, unlock); return WorkshopApi.OpenChain("ezapi.sample.chain") && WorkshopApi.IsVisible; });
Add("wrong route and unknown ID fail without opening", () => { Reset(); WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.tab", "测试", "Test", provider, unlock); return !WorkshopApi.OpenChain("ezapi.sample.tab") && !WorkshopApi.OpenTab("ezapi.sample.missing") && !WorkshopApi.IsVisible; });
Add("same owner ID and delegates are idempotent", () => { Reset(); var first = WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.tab", "测试", "Test", provider, unlock); var second = WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.tab", "测试", "Test", provider, unlock); return first.Status == SubmitStatus.Accepted && second.Status == SubmitStatus.AlreadyPresent; });
Add("same ID with changed callback or type conflicts", () => { Reset(); WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.tab", "测试", "Test", provider, unlock); return Is(WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.tab", "测试", "Test", provider, new Action<string>(_ => { })), SubmitStatus.Conflict) && Is(WorkshopApi.RegisterChain("ezapi.sample", "ezapi.sample.tab", "测试", "Test", provider, unlock), SubmitStatus.Conflict); });
Add("IDs must belong to lowercase owner namespace", () => { Reset(); return Is(WorkshopApi.RegisterTab("Bad.Mod", "Bad.Mod.tab", "测试", "Test", provider, unlock), SubmitStatus.Invalid) && Is(WorkshopApi.RegisterTab("ezapi.sample", "someone.tab", "测试", "Test", provider, unlock), SubmitStatus.Invalid); });
Add("titles and callbacks are required", () => { Reset(); return Is(WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.tab", " ", "Test", provider, unlock), SubmitStatus.Invalid) && Is(WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.other", "测试", "Test", null!, unlock), SubmitStatus.Invalid); });
Add("registration is initialization-thread only", () => { Reset(); return Task.Run(() => Is(WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.thread", "测试", "Test", provider, unlock), SubmitStatus.Invalid)).Result; });
Add("invalid snapshot is rejected before display", () => { Reset(); WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.invalid", "测试", "Test", () => new WorkshopSnapshot(-1, false, Array.Empty<WorkshopGroup>(), Array.Empty<WorkshopEntry>()), unlock); return !WorkshopApi.OpenTab("ezapi.sample.invalid") && !WorkshopApi.IsVisible; });
Add("snapshot deep copies lists and is read-only", () => { Reset(); var entries = new List<WorkshopEntry> { ValidSnapshot().Entries[0] with { Dependencies = new[] { "ezapi.sample.root" } } }; var source = new WorkshopSnapshot(100, false, new List<WorkshopGroup>(), entries); var frozen = WorkshopDefinitionRules.Copy("ezapi.sample", source); entries.Clear(); return frozen.Entries.Count == 1 && frozen.Entries[0].Dependencies.Count == 1 && frozen.Entries is IList<WorkshopEntry> list && list.IsReadOnly && frozen.Entries[0].Dependencies is IList<string> deps && deps.IsReadOnly; });
Add("duplicate entry IDs are rejected", () => { var entry = ValidSnapshot().Entries[0]; try { WorkshopDefinitionRules.Validate("ezapi.sample", new WorkshopSnapshot(0, false, Array.Empty<WorkshopGroup>(), new[] { entry, entry })); return false; } catch (ArgumentException e) { return e.Message.Contains("EZAPI_WORKSHOP_ENTRY_ID", StringComparison.Ordinal); } });
Add("invalid resource enum and nonfinite graph coordinates are rejected", () => { var entry = ValidSnapshot().Entries[0] with { Conditions = new[] { new WorkshopRequirement((WorkshopResourceKind)99, "", 1, false, true) } }; var badEnum = false; try { WorkshopDefinitionRules.Validate("ezapi.sample", new WorkshopSnapshot(0, false, Array.Empty<WorkshopGroup>(), new[] { entry })); } catch (ArgumentException) { badEnum = true; } var badGraph = false; try { WorkshopDefinitionRules.Validate("ezapi.sample", new WorkshopSnapshot(0, false, Array.Empty<WorkshopGroup>(), new[] { ValidSnapshot().Entries[0] with { GraphX = float.NaN } })); } catch (ArgumentException) { badGraph = true; } return badEnum && badGraph; });
Add("native item keys and namespaced IDs are accepted", () => { var entry = ValidSnapshot().Entries[0] with { Conditions = new[] { new WorkshopRequirement(WorkshopResourceKind.Item, "TOOL", 1, true, true), new WorkshopRequirement(WorkshopResourceKind.Item, "WaterBottle_01", 1, true, true) }, Rewards = new[] { new WorkshopReward(WorkshopResourceKind.Item, "ezapi.sample.reward", 1) } }; try { WorkshopDefinitionRules.Validate("ezapi.sample", new WorkshopSnapshot(0, false, Array.Empty<WorkshopGroup>(), new[] { entry })); return true; } catch { return false; } });
Add("unlock checks fresh snapshot and invokes callback once", () => { Reset(); var unlocked = false; var calls = 0; var localProvider = new Func<WorkshopSnapshot>(() => { var entry = ValidSnapshot().Entries[0] with { Unlocked = unlocked, ActionEnabled = !unlocked }; return ValidSnapshot() with { Entries = new[] { entry } }; }); var localUnlock = new Action<string>(_ => { calls++; unlocked = true; }); WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.live", "测试", "Test", localProvider, localUnlock); WorkshopApi.OpenTab("ezapi.sample.live"); WorkshopApi.TryGet("ezapi.sample.live", false, out var reg); var attempted = new HashSet<string>(StringComparer.Ordinal); var first = WorkshopApi.RunAction(reg, "ezapi.sample.workshop.node", attempted); var second = WorkshopApi.RunAction(reg, "ezapi.sample.workshop.node", attempted); return first.Invoked && first.Snapshot?.Entries[0].Unlocked == true && !second.Invoked && calls == 1; });
Add("failed unlock is consumed and never automatically repeated", () => { Reset(); var calls = 0; var throwing = new Action<string>(_ => { calls++; throw new InvalidOperationException("fixture failure"); }); WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.throw", "测试", "Test", provider, throwing); WorkshopApi.OpenTab("ezapi.sample.throw"); WorkshopApi.TryGet("ezapi.sample.throw", false, out var reg); var attempted = new HashSet<string>(StringComparer.Ordinal); var first = WorkshopApi.RunAction(reg, "ezapi.sample.workshop.node", attempted); var second = WorkshopApi.RunAction(reg, "ezapi.sample.workshop.node", attempted); return first.Invoked && first.Error?.Contains("EZAPI_WORKSHOP_UNLOCK", StringComparison.Ordinal) == true && !second.Invoked && calls == 1; });
Add("disabled and stale entries never invoke author callbacks", () => { Reset(); var calls = 0; var local = ValidSnapshot().Entries[0] with { ActionEnabled = false }; WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.disabled", "测试", "Test", () => ValidSnapshot() with { Entries = new[] { local } }, _ => calls++); WorkshopApi.OpenTab("ezapi.sample.disabled"); WorkshopApi.TryGet("ezapi.sample.disabled", false, out var reg); var disabled = WorkshopApi.RunAction(reg, local.Id, new HashSet<string>(StringComparer.Ordinal)); var stale = WorkshopApi.RunAction(reg, "ezapi.sample.missing", new HashSet<string>(StringComparer.Ordinal)); return !disabled.Invoked && !stale.Invoked && calls == 0; });
Add("callback that closes the window is not followed by stale provider read", () => { Reset(); var captures = 0; var localProvider = new Func<WorkshopSnapshot>(() => { captures++; return ValidSnapshot(); }); var localUnlock = new Action<string>(_ => WorkshopApi.CloseWindow()); WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.close", "测试", "Test", localProvider, localUnlock); WorkshopApi.OpenTab("ezapi.sample.close"); WorkshopApi.TryGet("ezapi.sample.close", false, out var reg); var result = WorkshopApi.RunAction(reg, "ezapi.sample.workshop.node", new HashSet<string>(StringComparer.Ordinal)); return result.Invoked && !WorkshopApi.IsVisible && result.Snapshot is null && captures == 2; });
Add("workshop localized declarations follow game language once per capture",()=>{
 Reset();NativeItemLocalization.Reads=0;NativeItemLocalization.Locale="en";
 WorkshopSnapshot Capture()=>WorkshopSnapshot.ForGameLanguage(100,
  new[]{new WorkshopGroup("工具","Tools",1)},new[]{ValidSnapshot().Entries[0] with{
   Title="",LocalizedTitle=new("野外工具包","Field kit"),Subtitle="",LocalizedSubtitle=new("节点","Node"),
   Description="",LocalizedDescription=new("说明","Description"),Requirement="",LocalizedRequirement=new("要求","Requirement"),
   CostText="",LocalizedCostText=new("花费","Cost"),NodeCostText="",LocalizedNodeCostText=new("节点花费","Node cost"),
   StateText="",LocalizedStateText=new("状态","State"),ActionText="",LocalizedActionText=new("解锁","Unlock"),
   Conditions=new[]{new WorkshopRequirement(WorkshopResourceKind.Credits,"credits",1,false,true){DisplayName="",LocalizedDisplayName=new("资金","Credits")}},
   Rewards=new[]{new WorkshopReward(WorkshopResourceKind.Item,"author.reward",1){DisplayName="",LocalizedDisplayName=new("奖励","Reward")}}
  }},"")with{LocalizedMessage=new("页面","Page")};
 WorkshopApi.RegisterTab("ezapi.sample","ezapi.sample.locale","工具","Tools",Capture,unlock);
 if(!WorkshopApi.OpenTab("ezapi.sample.locale"))return false;var english=WorkshopBrowser.LastSnapshot!;
 var good=english.English&&english.Message=="Page"&&english.Entries[0].Title=="Field kit"&&english.Entries[0].Conditions[0].DisplayName=="Credits"&&english.Entries[0].Rewards[0].DisplayName=="Reward"&&NativeItemLocalization.Reads==1;
 NativeItemLocalization.Locale="zh-CN";WorkshopApi.TryGet("ezapi.sample.locale",false,out var registration);var chinese=WorkshopApi.Capture(registration);
 return good&&!chinese.English&&chinese.Message=="页面"&&chinese.Entries[0].Title=="野外工具包"&&chinese.Entries[0].ActionText=="解锁"&&NativeItemLocalization.Reads==2;
});
Add("workshop explicit legacy language remains stable unless opted into locale",()=>{
 Reset();var legacy=WorkshopDefinitionRules.ResolveLocale(WorkshopDefinitionRules.Copy("ezapi.sample",ValidSnapshot()with{English=true}),"zh-CN");
 var following=WorkshopDefinitionRules.ResolveLocale(WorkshopDefinitionRules.Copy("ezapi.sample",WorkshopSnapshot.ForGameLanguage(100,Array.Empty<WorkshopGroup>(),Array.Empty<WorkshopEntry>())),"zh-CN");
 return legacy.English&&!following.English;
});
Add("close and stop release UI snapshot and registrations", () => { Reset(); WorkshopApi.RegisterTab("ezapi.sample", "ezapi.sample.tab", "测试", "Test", provider, unlock); WorkshopApi.OpenTab("ezapi.sample.tab"); WorkshopApi.CloseWindow(); var closed = !WorkshopApi.IsVisible && WorkshopBrowser.LastSnapshot is null; WorkshopApi.Stop(); return closed && !WorkshopApi.OpenTab("ezapi.sample.tab") && WorkshopApi.Report().ToString()!.Contains("count = 0", StringComparison.Ordinal); });

var results = tests.Select(test =>
{
    Reset();
    try { return new { name = test.Name, passed = test.Run(), error = (string?)null }; }
    catch (Exception error) { return new { name = test.Name, passed = false, error = (string?)error.ToString() }; }
}).ToArray();
var passed = results.Count(result => result.passed);
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
{
    suite = "workshop-api",
    passed,
    failed = results.Length - passed,
    cases = results,
    realProductSources = new[] { "WorkshopApi.cs", "WorkshopDefinitions.cs", "Registration.cs", "PresentationResult.cs" },
    scope = "registration, ownership, idempotence/conflicts, thread/lifecycle, deep snapshot validation, live action gating, one-shot/reentrant callback, UI routing; no payment/reward side effects"
}));
if (passed != results.Length) Environment.Exit(1);

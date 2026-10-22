using EZapi;
using Il2Cpp;
using HarmonyLib;
using System.Text.Json;
using NativeModules = Il2CppSystem.Collections.Generic.List<Il2Cpp.GameItem>;
using NativeStrings = Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray;

var cases = new List<string>();
void Check(bool value, string label) { if (!value) throw new Exception("FAILED: " + label); cases.Add(label); }
void Throws<T>(Action action, string label) where T : Exception {
    try { action(); } catch (T) { cases.Add(label); return; } throw new Exception("DID NOT THROW: " + label);
}
void Reset() {
    ModuleApi.Stop(); GameSession.Start(); ModuleApi.Start();
    TestNative.Trace.Clear(); TestNative.Skip.Clear(); TestNative.Fail = null; TestNative.LearningOriginal = null;
    Harmony.FailPatch = false; Harmony.FailUnpatch = false; Harmony.FailTarget = null;
    ModuleApi.DirectoryReady();
}
GameItem Machine() => new() { identifier = "native.machine", ModuleInventory = new() };
int Pending() => JsonSerializer.SerializeToElement(ModuleApi.Report()).GetProperty("pendingLearning").GetInt32();
GameSession.Start();
Throws<InvalidOperationException>(() => ModuleApi.Subscribe("test.mod", "early", ModulePhase.AfterTemp, _ => { }), "registration before initialization rejected");
ModuleApi.Start();
Throws<InvalidOperationException>(() => ModuleApi.GetInventory(Machine()), "native reads before directory rejected");
ModuleApi.DirectoryReady();
Check(ModuleApi.GetRuntimeStatus().All(x => !x.Ready && x.Targets.Count == 0), "unused groups install no hooks");
var machine = Machine();
var source = new List<string> { "test.mod.alpha", "test.mod.alpha" };
Check(ModuleApi.RegisterAdmission("test.mod", new[] { new ModuleAdmission(machine.identifier, source) }).Accepted, "admission accepts complete batch");
source[0] = "bad.mutated";
MachineHelper.SetupModuleBay(machine.ModuleInventory!, machine, null, null);
Check(MachineHelper.LastTypes!.SequenceEqual(new[] { "MODULE_TYPE_UNIVERSAL", "test.mod.alpha" }), "null native baseline preserves universal and copies caller list");
MachineHelper.SetupModuleBay(machine.ModuleInventory!, machine, null, new NativeStrings(new[] { "NATIVE", "NATIVE" }));
Check(MachineHelper.LastTypes!.SequenceEqual(new[] { "NATIVE", "test.mod.alpha" }), "native original types retained and deduplicated");
MachineHelper.SetupModuleBay(machine.ModuleInventory!, machine, null, new NativeStrings(Array.Empty<string>()));
Check(MachineHelper.LastTypes!.SequenceEqual(new[] { "test.mod.alpha" }), "explicit empty baseline does not invent universal");
Check(ModuleApi.RegisterAdmission("another.mod", new[] { new ModuleAdmission(machine.identifier, new[] { "other" }) }).Accepted, "second owner contributes independently");
MachineHelper.SetupModuleBay(machine.ModuleInventory!, machine, null, new NativeStrings(new[] { "NATIVE" }));
Check(MachineHelper.LastTypes!.SequenceEqual(new[] { "NATIVE", "other", "test.mod.alpha" }), "owners merge deterministically without removing native types");
Check(!ModuleApi.RegisterAdmission("test.mod", new[] { new ModuleAdmission(machine.identifier, new[] { "one" }), new ModuleAdmission(machine.identifier, new[] { "two" }) }).Accepted, "duplicate machine rejects entire replacement");
MachineHelper.SetupModuleBay(machine.ModuleInventory!, machine, null, null);
Check(MachineHelper.LastTypes!.Contains("test.mod.alpha"), "failed replacement preserves previous owner declarations");
Check(!ModuleApi.RegisterAdmission("test.mod", new[] { new ModuleAdmission("", new[] { "one" }) }).Accepted, "empty machine rejected");
Check(!ModuleApi.RegisterAdmission("test.mod", new[] { new ModuleAdmission(machine.identifier, new[] { "bad\n" }) }).Accepted, "control type rejected");
Check(!ModuleApi.RegisterAdmission("test.mod", Array.Empty<ModuleAdmission>()).Accepted, "empty batch rejected");
Check(!ModuleApi.RegisterAdmission("test.mod", new[] { new ModuleAdmission(machine.identifier, Enumerable.Range(0, 65).Select(i => "t" + i).ToArray()) }).Accepted, "oversized type list rejected");
Check(!ModuleApi.RegisterAdmission("test.mod", Enumerable.Range(0, 257).Select(i => new ModuleAdmission("machine" + i, new[] { "one" })).ToArray()).Accepted, "oversized admission batch rejected");
ModuleApi.RemoveAdmission("test.mod"); ModuleApi.RemoveAdmission("another.mod");
MachineHelper.SetupModuleBay(machine.ModuleInventory!, machine, null, null);
Check(MachineHelper.LastTypes is null, "remove only owned additions and preserves native null");
Check(ModuleApi.RegisterAdmission("test.mod", new[] { new ModuleAdmission("unrelated", new[] { "one" }) }).Accepted, "unrelated admission accepted");
MachineHelper.SetupModuleBay(machine.ModuleInventory!, machine, null, null);
Check(MachineHelper.LastTypes is null, "unrelated machine remains untouched");

Reset(); machine = Machine();
Check(ReferenceEquals(ModuleApi.GetInventory(machine), machine.ModuleInventory), "inventory uses official machine accessor before loaded run");
Check(ModuleApi.GetInventory(new()) is null, "non-machine without module inventory returns null");
Throws<ArgumentException>(() => ModuleApi.GetInventory(new GameItem { Pointer = IntPtr.Zero }), "zero pointer read rejected");
Throws<ArgumentException>(() => ModuleApi.GetInventory(new GameItem { unitCount = 0 }), "destroyed item read rejected");
var module = new GameItem { ModuleType = "alpha" }; var second = new GameItem { ModuleType = "beta" };
machine.ModuleInventory!.items.Add(module); machine.ModuleInventory.items.Add(second); machine.ModuleInventory.items.Add(new GameItem { identifier = "other", ModuleType = "alpha" });
Check(ModuleApi.FindInstalled(machine, "test.mod.module").Count == 2, "find matches exact item ID");
Check(ReferenceEquals(ModuleApi.FindInstalled(machine, "test.mod.module", "alpha").Single(), module), "find filters native MODULE_TYPE valueString");
Check(ModuleApi.FindInstalled(new(), "test.mod.module").Count == 0, "find missing module inventory returns empty");
Throws<ArgumentException>(() => ModuleApi.FindInstalled(machine, ""), "find empty ID rejected");
Throws<ArgumentException>(() => ModuleApi.FindInstalled(machine, "test.mod.module", "\n"), "find invalid type rejected");
var snapshot = ModuleApi.FindInstalled(machine, "test.mod.module"); machine.ModuleInventory.items.Clear();
Check(snapshot.Count == 2, "find list snapshot unaffected by later inventory list changes");
machine.ModuleInventory.items.Add(module);
ModuleApi.Recompute(machine);
Check(TestNative.Trace.SequenceEqual(new[] { "temp", "aggregate", "basic" }), "recompute exact temporary aggregate basic order");
Throws<InvalidOperationException>(() => ModuleApi.Recompute(new()), "recompute absent module inventory fails before writes");
TestNative.Trace.Clear();
foreach (var phase in Enum.GetValues<ModulePhase>()) {
    var capture = phase;
    ModuleApi.Subscribe("test.mod", phase.ToString(), phase, _ => TestNative.Trace.Add(capture.ToString()));
}
Check(ModuleApi.GetRuntimeStatus().Single(x => x.OwnerId == "ezapi.modules.phases").Targets.Count == 4, "five phases use four complete native targets");
ModuleApi.Recompute(machine);
Check(TestNative.Trace.SequenceEqual(new[] { "temp", "AfterTemp", "BeforeAggregate", "aggregate", "AfterAggregate", "basic" }), "phase events preserve recompute order");
TestNative.Trace.Clear(); ModuleEffectHelper.OnAddedModuleToGrid(module, machine, machine.ModuleInventory.items); ModuleEffectHelper.OnRemovedModuleFromGrid(module, machine, machine.ModuleInventory.items);
Check(TestNative.Trace.SequenceEqual(new[] { "BeforeAdded", "added", "removed", "AfterRemoved" }), "added prefix and removed postfix map correctly");
TestNative.Trace.Clear(); TestNative.Skip.Add("ComputeAllModuleTempStat"); ModuleEffectHelper.ComputeAllModuleTempStat(machine.ModuleInventory.items);
Check(TestNative.Trace.Count == 0, "skipped temp original emits no AfterTemp"); TestNative.Skip.Clear();
TestNative.Trace.Clear(); TestNative.Fail = "ComputeModuleEffect";
try { ModuleHelper.ComputeModuleEffect(machine.ModuleInventory.items, machine); } catch (Exception e) { Check(ReferenceEquals(e, TestNative.Failure), "native phase exception identity preserved"); }
Check(TestNative.Trace.SequenceEqual(new[] { "BeforeAggregate" }), "failed aggregate emits no AfterAggregate"); TestNative.Fail = null;
Throws<ArgumentException>(() => ModuleApi.Subscribe("test.mod", "BeforeAdded", ModulePhase.AfterTemp, _ => { }), "duplicate callback ID rejected across phase signals");
Throws<ArgumentOutOfRangeException>(() => ModuleApi.Subscribe("test.mod", "bad", (ModulePhase)99, _ => { }), "undefined phase rejected");
Throws<ArgumentException>(() => ModuleApi.Subscribe("test.mod", "\n", ModulePhase.AfterTemp, _ => { }), "unreadable callback ID rejected");
Throws<ArgumentNullException>(() => ModuleApi.Subscribe("test.mod", "nil", ModulePhase.AfterTemp, null!), "null phase callback rejected");
Throws<InvalidOperationException>(() => Task.Run(() => ModuleApi.GetInventory(machine)).GetAwaiter().GetResult(), "worker native read rejected");
Throws<InvalidOperationException>(() => Task.Run(() => ModuleApi.Subscribe("test.mod", "worker", ModulePhase.AfterTemp, _ => { })).GetAwaiter().GetResult(), "worker subscription rejected");

Reset(); machine = Machine(); module = new(); machine.ModuleInventory!.items.Add(module);
var order = new List<int>(); ModuleContext? retained = null;
using var last = ModuleApi.Subscribe("test.mod", "last", ModulePhase.BeforeAggregate, _ => order.Add(3), int.MaxValue);
using var mid = ModuleApi.Subscribe("test.mod", "middle", ModulePhase.BeforeAggregate, _ => order.Add(2));
using var first = ModuleApi.Subscribe("test.mod", "first", ModulePhase.BeforeAggregate, c => { retained = c; order.Add(1); Check(c.IsCurrent && c.Modules.Count == 1 && c.Machine == machine && c.Module is null, "phase context current and complete"); }, int.MinValue);
ModuleHelper.ComputeModuleEffect(machine.ModuleInventory.items, machine);
Check(order.SequenceEqual(new[] { 1, 2, 3 }), "full integer priority range correctly ordered");
Check(retained is not null && !retained.IsCurrent, "phase context expired after callback chain");
Throws<InvalidOperationException>(() => _ = retained!.Modules, "expired phase native getters rejected");
first.Dispose(); mid.Dispose(); last.Dispose();
int badCalls = 0, goodCalls = 0;
using var bad = ModuleApi.Subscribe("test.mod", "fault", ModulePhase.AfterTemp, _ => { badCalls++; throw new Exception("mod failed"); });
using var good = ModuleApi.Subscribe("test.mod", "healthy", ModulePhase.AfterTemp, _ => goodCalls++);
ModuleEffectHelper.ComputeAllModuleTempStat(machine.ModuleInventory.items); ModuleEffectHelper.ComputeAllModuleTempStat(machine.ModuleInventory.items);
Check(badCalls == 1 && goodCalls == 2, "faulting phase subscription removed while healthy owner continues");
good.Dispose();
int recursions = 0;
using (ModuleApi.Subscribe("test.mod", "recursive", ModulePhase.BeforeAggregate, _ => { recursions++; ModuleHelper.ComputeModuleEffect(machine.ModuleInventory.items, machine); })) ModuleHelper.ComputeModuleEffect(machine.ModuleInventory.items, machine);
Check(recursions == 1, "same phase native recursion suppresses repeated callbacks");
using (ModuleApi.Subscribe("test.mod", "recursive-recompute", ModulePhase.BeforeAggregate, _ => Throws<InvalidOperationException>(() => ModuleApi.Recompute(machine), "same-machine recompute reentry rejected"))) ModuleApi.Recompute(machine);
var old = machine.ModuleInventory;
using (ModuleApi.Subscribe("test.mod", "replace", ModulePhase.AfterTemp, _ => machine.ModuleInventory = new()))
    Throws<InvalidOperationException>(() => ModuleApi.Recompute(machine), "recompute detects module inventory replacement before next write");
machine.ModuleInventory = old;
using (ModuleApi.Subscribe("test.mod", "scene", ModulePhase.AfterTemp, _ => GameSession.Invalidate()))
    Throws<InvalidOperationException>(() => ModuleApi.Recompute(machine), "recompute stops after scene revision changes");

Reset(); machine = Machine(); module = new();
int actions = 0; ModuleActionContext? retainedAction = null;
using var action = ModuleApi.SubscribeAction("test.mod", "on-use", module.identifier, c => { actions++; retainedAction = c; Check(c.Module == module && c.Machine == machine && c.Inventory == machine.ModuleInventory, "action exposes actual native arguments"); });
Throws<ArgumentException>(() => ModuleApi.SubscribeAction("test.mod", "foreign", "another.mod.module", _ => { }), "foreign module namespace rejected");
Throws<ArgumentNullException>(() => ModuleApi.SubscribeAction("test.mod", "nil", module.identifier, null!), "null action callback rejected");
ModuleEffectHelper.ActivateOnUseAction(module, machine.ModuleInventory!, machine);
Check(actions == 1 && TestNative.Trace.Last() == "action", "action dispatched without skipping native behavior");
Check(retainedAction is not null && !retainedAction.IsCurrent, "action context expires");
Throws<InvalidOperationException>(() => _ = retainedAction!.Inventory, "expired action inventory inaccessible");
ModuleEffectHelper.ActivateOnUseAction(new() { identifier = "other" }, machine.ModuleInventory!, machine);
Check(actions == 1, "action callback only receives matching module ID");
action.Dispose(); ModuleEffectHelper.ActivateOnUseAction(module, machine.ModuleInventory!, machine); Check(actions == 1, "disposed action no longer called");

Reset(); module = new(); var captures = new List<int>(); var completions = new List<(int Before, int After)>();
using var learning = ModuleApi.SubscribeLearning("test.mod", "learn", m => { captures.Add(m.LearningValue); return m.LearningValue; }, (m, before) => completions.Add((before, m.LearningValue)));
Check(ModuleApi.GetRuntimeStatus().Single(x => x.OwnerId == "ezapi.modules.learning").Targets.Count == 1, "learning complete hook has precise single target");
ModuleEffectHelper.OnUsedLearningAlgo(module); ModuleEffectHelper.OnUsedLearningAlgo(module);
Check(captures.SequenceEqual(new[] { 0, 1 }) && completions.SequenceEqual(new[] { (0, 1), (1, 2) }), "learning captures independent before values per invocation");
Check(Pending() == 0, "normal learning frames released by finalizer");
TestNative.Skip.Add("OnUsedLearningAlgo"); ModuleEffectHelper.OnUsedLearningAlgo(module);
Check(completions.Count == 2 && module.LearningValue == 2 && Pending() == 0, "skipped original has no learning completion and clears state"); TestNative.Skip.Clear();
TestNative.Fail = "OnUsedLearningAlgo";
try { ModuleEffectHelper.OnUsedLearningAlgo(module); } catch (Exception e) { Check(ReferenceEquals(e, TestNative.Failure), "learning finalizer preserves native exception identity"); }
Check(completions.Count == 2 && Pending() == 0, "failed learning does not complete and releases snapshot"); TestNative.Fail = null;
var nested = new GameItem(); bool inner = false;
TestNative.LearningOriginal = m => { if (!inner) { inner = true; ModuleEffectHelper.OnUsedLearningAlgo(nested); inner = false; } };
ModuleEffectHelper.OnUsedLearningAlgo(module);
Check(completions.TakeLast(2).SequenceEqual(new[] { (0, 1), (2, 3) }), "different-module nested learning keeps separate state");
int beforeCount = captures.Count;
TestNative.LearningOriginal = m => { if (!inner) { inner = true; ModuleEffectHelper.OnUsedLearningAlgo(m); inner = false; } };
ModuleEffectHelper.OnUsedLearningAlgo(module);
Check(captures.Count == beforeCount + 1 && Pending() == 0, "same-module native recursion suppresses duplicate learning capture");
TestNative.LearningOriginal = null; learning.Dispose();
int captureFaults = 0, completeFaults = 0;
using var captureBad = ModuleApi.SubscribeLearning<int>("test.mod", "capture-fails", _ => { captureFaults++; throw new Exception(); }, (_, _) => throw new Exception("must not complete"));
using var completeBad = ModuleApi.SubscribeLearning("test.mod", "complete-fails", _ => 1, (_, _) => { completeFaults++; throw new Exception(); });
ModuleEffectHelper.OnUsedLearningAlgo(module); ModuleEffectHelper.OnUsedLearningAlgo(module);
Check(captureFaults == 1 && completeFaults == 1 && Pending() == 0, "capture and completed failures isolated and subscriptions disabled");
int invalidCompletions = 0;
IDisposable? self = null;
self = ModuleApi.SubscribeLearning("test.mod", "self-dispose", _ => { self!.Dispose(); return 0; }, (_, _) => invalidCompletions++);
ModuleEffectHelper.OnUsedLearningAlgo(module);
Check(invalidCompletions == 0 && Pending() == 0, "dispose during capture prevents completion");
using (ModuleApi.SubscribeLearning("test.mod", "scene-capture", _ => { GameSession.Invalidate(); ModuleApi.SceneChanged(); return new byte[1024]; }, (_, _) => invalidCompletions++)) ModuleEffectHelper.OnUsedLearningAlgo(module);
Check(invalidCompletions == 0 && Pending() == 0, "scene change during capture clears pending and never completes");
using (ModuleApi.SubscribeLearning("test.mod", "scene-native", _ => 1, (_, _) => invalidCompletions++)) {
    TestNative.LearningOriginal = _ => { GameSession.Invalidate(); ModuleApi.SceneChanged(); };
    ModuleEffectHelper.OnUsedLearningAlgo(module);
}
Check(invalidCompletions == 0 && Pending() == 0, "scene change during native learning prevents stale completion");
TestNative.LearningOriginal = null;
Throws<ArgumentNullException>(() => ModuleApi.SubscribeLearning<int>("test.mod", "null-capture", null!, (_, _) => { }), "null learning capture rejected");
Throws<ArgumentNullException>(() => ModuleApi.SubscribeLearning<int>("test.mod", "null-completed", _ => 0, null!), "null learning completed rejected");

Reset();
Harmony.FailTarget = "OnRemovedModuleFromGrid";
using var disabled = ModuleApi.Subscribe("test.mod", "disabled", ModulePhase.AfterTemp, _ => throw new Exception("must not run"));
var phaseHook = ModuleApi.GetRuntimeStatus().Single(x => x.OwnerId == "ezapi.modules.phases");
Check(!phaseHook.Ready && phaseHook.Error is not null, "partial phase install disables entire group");
Check(!Harmony.Installed.Values.SelectMany(x => x).Any(x => x.Owner == "ezapi.modules.phases"), "failed group removes already installed hooks");
Harmony.FailTarget = null; ModuleApi.DirectoryReady();
Check(ModuleApi.GetRuntimeStatus().Single(x => x.OwnerId == "ezapi.modules.phases").Ready, "directory callback can retry failed group");
ModuleApi.DirectoryReady();
Check(Harmony.Installed.Values.SelectMany(x => x).Count(x => x.Owner == "ezapi.modules.phases") == 4, "repeated installation has no duplicates");
new Harmony("ezapi.modules.phases").UnpatchSelf();
Check(!ModuleApi.GetRuntimeStatus().Single(x => x.OwnerId == "ezapi.modules.phases").Ready, "lost required patch owner invalidates whole group");
ModuleApi.Stop();
Check(Harmony.Installed.Values.SelectMany(x => x).All(x => !x.Owner.StartsWith("ezapi.modules")), "stop removes module hooks");
Throws<InvalidOperationException>(() => ModuleApi.GetInventory(Machine()), "reads after stop rejected");
Reset();
Check(JsonSerializer.SerializeToElement(ModuleApi.Report()).GetProperty("subscriptions").GetArrayLength() == 0, "restart has no old subscriptions");
Check(JsonSerializer.SerializeToElement(ModuleApi.Report()).GetProperty("admissions").GetArrayLength() == 0, "restart has no old owner admissions");
for (var i = 0; i < 256; i++) ModuleApi.Subscribe("test.mod", "limit" + i, ModulePhase.AfterTemp, _ => { });
Throws<InvalidOperationException>(() => ModuleApi.Subscribe("test.mod", "overflow", ModulePhase.AfterTemp, _ => { }), "subscription limit enforced");
ModuleApi.Stop(); GameSession.Stop();
var report = new { passed = cases.Count, failed = 0, cases, nativeRuntimeVerified = false,
    scope = "real Modules/GameSession/NativeHooks with native and Harmony simulation; does not prove IL2CPP detours or native module computations" };
File.WriteAllText("work/ezapi-module-tests.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { report.passed, report.failed }));

using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using NativeModules = Il2CppSystem.Collections.Generic.List<Il2Cpp.GameItem>;
namespace EZapi;

/// <summary>追加到机器模块仓的类型。不会删除原生类型，也不会重建已存在的仓。</summary>
public sealed record ModuleAdmission(string MachineId, IReadOnlyList<string> ModuleTypes);
public enum ModulePhase { BeforeAggregate, AfterAggregate, AfterTemp, BeforeAdded, AfterRemoved }

/// <summary>仅本次同步回调有效。Modules为列表快照；原生物品包装对象不可跨场景缓存。</summary>
public sealed class ModuleContext
{
    private readonly ModuleCallScope _scope;
    private readonly IReadOnlyList<GameItem> _modules;
    private readonly GameItem? _machine, _module;
    internal ModuleContext(ModuleCallScope scope, IReadOnlyList<GameItem> modules, GameItem? machine, GameItem? module)
        => (_scope, _modules, _machine, _module) = (scope, modules, machine, module);
    public bool IsCurrent => _scope.IsCurrent;
    public IReadOnlyList<GameItem> Modules { get { _scope.Require(); return _modules; } }
    public GameItem? Machine { get { _scope.Require(); return _machine; } }
    public GameItem? Module { get { _scope.Require(); return _module; } }
}

/// <summary>原生ActivateOnUseAction之前的通知；不代表机器已经成功生产。</summary>
public sealed class ModuleActionContext
{
    private readonly ModuleCallScope _scope;
    private readonly GameItem _module, _machine;
    private readonly GameInventory _inventory;
    internal ModuleActionContext(ModuleCallScope scope, GameItem module, GameInventory inventory, GameItem machine)
        => (_scope, _module, _inventory, _machine) = (scope, module, inventory, machine);
    public bool IsCurrent => _scope.IsCurrent;
    public GameItem Module { get { _scope.Require(); return _module; } }
    public GameInventory Inventory { get { _scope.Require(); return _inventory; } }
    public GameItem Machine { get { _scope.Require(); return _machine; } }
}

internal sealed class ModuleCallScope
{
    private readonly long _generation = ModuleApi.Generation, _revision = GameSession.Revision;
    private readonly (GameItem Item, IntPtr Pointer, int UniqueId, string Id)[] _items;
    private readonly GameInventory? _inventory;
    private readonly IntPtr _inventoryPointer;
    private bool _active = true;
    internal ModuleCallScope(IEnumerable<GameItem> items, GameInventory? inventory = null)
    {
        _items = items.Select(i => (i, i.Pointer, i.uniqueId, i.identifier)).ToArray();
        _inventory = inventory; _inventoryPointer = inventory?.Pointer ?? IntPtr.Zero;
    }
    internal bool IsCurrent {
        get {
            if (!_active || !GameSession.OnThread || !ModuleApi.IsGeneration(_generation) || GameSession.Revision != _revision) return false;
            try {
                return (_inventory is null || (_inventoryPointer != IntPtr.Zero && _inventory.Pointer == _inventoryPointer)) &&
                    _items.All(x => x.Pointer != IntPtr.Zero && x.Item.Pointer == x.Pointer && x.Item.uniqueId == x.UniqueId && x.Item.identifier == x.Id && x.Item.unitCount > 0);
            } catch { return false; }
        }
    }
    internal void Require() { if (!IsCurrent) throw new InvalidOperationException("EZAPI_MODULE_STALE: synchronous context expired."); }
    internal void Close() => _active = false;
}

/// <summary>独立模块适配：原生模块仓、追加准入、五阶段、动作和逐调用成长状态。无Forge依赖。</summary>
public static class ModuleApi
{
    private sealed class Subscription : IDisposable
    {
        internal readonly string Owner, Id;
        internal readonly ModulePhase? Phase;
        internal readonly string? ModuleId;
        internal readonly int Order;
        internal readonly Action<ModuleContext>? Callback;
        internal readonly Action<ModuleActionContext>? Action;
        internal readonly Func<GameItem, Action<GameItem>>? Prepare;
        private int _disposed;
        internal bool Disposed => Volatile.Read(ref _disposed) != 0;
        internal Subscription(string owner, string id, ModulePhase? phase = null, string? moduleId = null,
            int order = 0, Action<ModuleContext>? callback = null, Action<ModuleActionContext>? action = null,
            Func<GameItem, Action<GameItem>>? prepare = null)
            => (Owner, Id, Phase, ModuleId, Order, Callback, Action, Prepare) = (owner, id, phase, moduleId, order, callback, action, prepare);
        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }
    private sealed class LearningFrame
    {
        internal GameItem? Item;
        internal ModuleCallScope? Scope;
        internal readonly List<(Subscription Subscription, Action<GameItem> Complete)> Completed = new();
        internal void Close() { Scope?.Close(); Scope = null; Item = null; Completed.Clear(); }
    }
    private static readonly Dictionary<string, ModuleAdmission[]> Admissions = new(StringComparer.Ordinal);
    private static readonly List<Subscription> Subscriptions = new();
    private static readonly HashSet<LearningFrame> Pending = new();
    private static HashSet<(string Signal, IntPtr Item)> Active = new();
    private static readonly NativeHookGroup AdmissionHooks = new("ezapi.modules.admission");
    private static readonly NativeHookGroup PhaseHooks = new("ezapi.modules.phases");
    private static readonly NativeHookGroup ActionHooks = new("ezapi.modules.actions");
    private static readonly NativeHookGroup LearningHooks = new("ezapi.modules.learning");
    private static bool _started, _directoryReady;
    internal static bool IsNativeDirectoryReady => _started && _directoryReady && GameSession.OnThread;
    private static object? _lastAdmission;
    internal static long Generation { get; private set; }
    internal static bool IsGeneration(long value) => _started && value == Generation;

    /// <summary>原子替换owner的声明。最多256台机器/owner、64类型/机器、4096条/进程。只影响后续SetupModuleBay。</summary>
    public static ContentSubmissionResult RegisterAdmission(string ownerId, IReadOnlyList<ModuleAdmission> admissions)
    {
        ReadyForDeclaration(ownerId); ArgumentNullException.ThrowIfNull(admissions);
        if (admissions.Count is < 1 or > 256) return new(false, "EZAPI_MODULE_LIMIT: 1..256 machines required.");
        var copy = new List<ModuleAdmission>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var admission in admissions) {
            if (admission is null || !ValidId(admission.MachineId) || admission.ModuleTypes is null || admission.ModuleTypes.Count is < 1 or > 64 ||
                admission.ModuleTypes.Any(t => !ValidId(t))) return new(false, "EZAPI_MODULE_ADMISSION: valid machine ID and 1..64 types required.");
            if (!seen.Add(admission.MachineId)) return new(false, "EZAPI_MODULE_DUPLICATE: machine repeated in batch.");
            copy.Add(new(admission.MachineId, Array.AsReadOnly(admission.ModuleTypes.Distinct(StringComparer.Ordinal).ToArray())));
        }
        if (Admissions.Where(x => x.Key != ownerId).Sum(x => x.Value.Length) + copy.Count > 4096)
            return new(false, "EZAPI_MODULE_LIMIT: 4096 admission entries maximum.");
        Admissions[ownerId] = copy.ToArray(); EnsureHooks(); return new(true, null);
    }
    public static void RemoveAdmission(string ownerId)
    { ReadyForDeclaration(ownerId); Admissions.Remove(ownerId); }

    public static IDisposable SubscribeAction(string ownerId, string callbackId, string moduleId, Action<ModuleActionContext> callback)
    {
        ReadyForDeclaration(ownerId); ValidateCallbackId(callbackId); ArgumentNullException.ThrowIfNull(callback);
        if (!ValidId(moduleId) || !moduleId.StartsWith(ownerId + ".", StringComparison.Ordinal) || moduleId.Length <= ownerId.Length + 1)
            throw new ArgumentException("EZAPI_MODULE_OWNER: module ID must belong to owner namespace.");
        return Add(new(ownerId, callbackId, moduleId:moduleId, action:callback));
    }
    public static IDisposable Subscribe(string ownerId, string callbackId, ModulePhase phase, Action<ModuleContext> callback, int order = 0)
    {
        ReadyForDeclaration(ownerId); ValidateCallbackId(callbackId); ArgumentNullException.ThrowIfNull(callback);
        if (!Enum.IsDefined(typeof(ModulePhase), phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        return Add(new(ownerId, callbackId, phase:phase, order:order, callback:callback));
    }
    /// <summary>capture在原生OnUsedLearningAlgo前执行；completed仅在该次原方法正常返回时执行。T不与下一次调用共享。</summary>
    public static IDisposable SubscribeLearning<T>(string ownerId, string callbackId, Func<GameItem, T> capture, Action<GameItem, T> completed)
    {
        ReadyForDeclaration(ownerId); ValidateCallbackId(callbackId); ArgumentNullException.ThrowIfNull(capture); ArgumentNullException.ThrowIfNull(completed);
        return Add(new(ownerId, callbackId, prepare:item => { var state = capture(item); return current => completed(current, state); }));
    }
    private static IDisposable Add(Subscription entry)
    {
        Subscriptions.RemoveAll(s => s.Disposed);
        if (Subscriptions.Any(s => s.Owner == entry.Owner && s.Id == entry.Id)) throw new ArgumentException("EZAPI_MODULE_DUPLICATE: owner/callbackId already registered.");
        if (Subscriptions.Count >= 256) throw new InvalidOperationException("EZAPI_MODULE_LIMIT: max256 subscriptions.");
        Subscriptions.Add(entry); EnsureHooks(); return entry;
    }
    /// <summary>通过当前官方GetModuleInv读取。无模块仓返回null；初始化主线程、原生目录就绪后可用，支持尚未交付的新机器。</summary>
    public static GameGridInventory? GetInventory(GameItem machine)
    {
        RequireNative(); RequireItem(machine);
        var inventory = MachineHelper.GetModuleInv(machine);
        return inventory is null || inventory.Pointer == IntPtr.Zero ? null : inventory;
    }
    public static IReadOnlyList<GameItem> FindInstalled(GameItem machine, string moduleId, string? moduleType = null)
    {
        if (!ValidId(moduleId) || (moduleType is not null && !ValidId(moduleType))) throw new ArgumentException("EZAPI_MODULE_ID: valid module ID/type required.");
        var inventory = GetInventory(machine);
        if (inventory is null) return Array.Empty<GameItem>();
        return Array.AsReadOnly(Capture(inventory.items).Where(i => i.identifier == moduleId &&
            (moduleType is null || i.GetTagReadonly("MODULE_TYPE")?.valueString == moduleType)).ToArray());
    }
    /// <summary>按原生顺序重算：临时属性→聚合→基础效果。不是库存事务，失败不自动重试或回滚属性。</summary>
    public static void Recompute(GameItem machine)
    {
        var inventory = GetInventory(machine) ?? throw new InvalidOperationException("EZAPI_MODULE_INVENTORY: machine has no module inventory.");
        var active = Active; var key = ("recompute", machine.Pointer);
        if (!active.Add(key)) throw new InvalidOperationException("EZAPI_MODULE_REENTRANT: recursive recomputation rejected.");
        var scope = new ModuleCallScope(new[] { machine }, inventory);
        try {
            ModuleEffectHelper.ComputeAllModuleTempStat(inventory.items);
            scope.Require(); RequireSameInventory(machine, inventory);
            ModuleHelper.ComputeModuleEffect(inventory, null, machine);
            scope.Require(); RequireSameInventory(machine, inventory);
            ModuleHelper.ApplyBasicModuleEffect(inventory, null, machine);
            scope.Require(); RequireSameInventory(machine, inventory);
        } finally { scope.Close(); active.Remove(key); }
    }
    private static void RequireSameInventory(GameItem machine, GameGridInventory inventory)
    { if (MachineHelper.GetModuleInv(machine)?.Pointer != inventory.Pointer) throw new InvalidOperationException("EZAPI_MODULE_STALE: module inventory replaced during recomputation."); }
    public static IReadOnlyList<NativeHookStatus> GetRuntimeStatus()
    { GameSession.RequireThread(); return Array.AsReadOnly(new[] { AdmissionHooks.Snapshot(), PhaseHooks.Snapshot(), ActionHooks.Snapshot(), LearningHooks.Snapshot() }); }

    internal static void Start() { if (_started) return; _started = true; _directoryReady = false; Generation++; }
    internal static void DirectoryReady() { if (!_started) return; GameSession.RequireThread(); _directoryReady = true; EnsureHooks(); }
    private static void EnsureHooks()
    {
        if (!_started || !_directoryReady) return;
        if (Admissions.Count > 0) AdmissionHooks.Install(typeof(ModuleApi), new NativeHookSpec(typeof(MachineHelper), nameof(MachineHelper.SetupModuleBay), typeof(void),
            new[] { typeof(GameInventory), typeof(GameItem), typeof(Il2CppSystem.Action), typeof(Il2CppStringArray) }, Prefix:nameof(BeforeSetup)));
        if (Subscriptions.Any(s => !s.Disposed && s.Phase.HasValue)) PhaseHooks.Install(typeof(ModuleApi),
            new(typeof(ModuleHelper), nameof(ModuleHelper.ComputeModuleEffect), typeof(void), new[] { typeof(NativeModules), typeof(GameItem) }, Prefix:nameof(BeforeAggregate), Postfix:nameof(AfterAggregate)),
            new(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.ComputeAllModuleTempStat), typeof(void), new[] { typeof(NativeModules) }, Postfix:nameof(AfterTemp)),
            new(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.OnAddedModuleToGrid), typeof(void), new[] { typeof(GameItem), typeof(GameItem), typeof(NativeModules) }, Prefix:nameof(BeforeAdded)),
            new(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.OnRemovedModuleFromGrid), typeof(void), new[] { typeof(GameItem), typeof(GameItem), typeof(NativeModules) }, Postfix:nameof(AfterRemoved)));
        if (Subscriptions.Any(s => !s.Disposed && s.Action is not null)) ActionHooks.Install(typeof(ModuleApi), new NativeHookSpec(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.ActivateOnUseAction), typeof(void),
            new[] { typeof(GameItem), typeof(GameInventory), typeof(GameItem) }, Prefix:nameof(BeforeAction)));
        if (Subscriptions.Any(s => !s.Disposed && s.Prepare is not null)) LearningHooks.Install(typeof(ModuleApi), new NativeHookSpec(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.OnUsedLearningAlgo), typeof(void),
            new[] { typeof(GameItem) }, Prefix:nameof(BeforeLearning), Postfix:nameof(AfterLearning), Finalizer:nameof(FinishLearning)));
    }
    private static bool InHook(NativeHookGroup hooks) => _started && _directoryReady && GameSession.OnThread && hooks.Snapshot().Ready;
    private static void BeforeSetup(GameItem __1, ref Il2CppStringArray? __3)
    {
        if (!InHook(AdmissionHooks)) return;
        try {
            RequireItem(__1);
            var additions = Admissions.OrderBy(x => x.Key, StringComparer.Ordinal).SelectMany(x => x.Value).Where(x => x.MachineId == __1.identifier).SelectMany(x => x.ModuleTypes).ToArray();
            if (additions.Length == 0) return; // null语义原样保留，不因无关规则改变原生默认。
            var original = __3?.ToArray();
            var baseline = original ?? new[] { "MODULE_TYPE_UNIVERSAL" };
            var merged = baseline.Concat(additions).Distinct(StringComparer.Ordinal).ToArray();
            if (original is null || !original.SequenceEqual(merged, StringComparer.Ordinal)) __3 = new Il2CppStringArray(merged);
            _lastAdmission = new { machineId=__1.identifier, originalWasNull=original is null, original, merged };
        } catch (Exception e) { Log("admission", e); }
    }
    private static void BeforeAggregate(NativeModules __0, GameItem __1) => Dispatch(ModulePhase.BeforeAggregate, __0, __1, null);
    private static void AfterAggregate(NativeModules __0, GameItem __1, bool __runOriginal) { if (__runOriginal) Dispatch(ModulePhase.AfterAggregate, __0, __1, null); }
    private static void AfterTemp(NativeModules __0, bool __runOriginal) { if (__runOriginal) Dispatch(ModulePhase.AfterTemp, __0, null, null); }
    private static void BeforeAdded(GameItem __0, GameItem __1, NativeModules __2) => Dispatch(ModulePhase.BeforeAdded, __2, __1, __0);
    private static void AfterRemoved(GameItem __0, GameItem __1, NativeModules __2, bool __runOriginal) { if (__runOriginal) Dispatch(ModulePhase.AfterRemoved, __2, __1, __0); }
    private static Subscription[] Ordered(Func<Subscription, bool> predicate) => Subscriptions.Where(s => !s.Disposed && predicate(s))
        .OrderBy(s => s.Order).ThenBy(s => s.Owner, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
    private static void Dispatch(ModulePhase phase, NativeModules modules, GameItem? machine, GameItem? module)
    {
        if (!InHook(PhaseHooks)) return;
        var active = Active; var key = ("phase:" + phase, machine?.Pointer ?? modules?.Pointer ?? IntPtr.Zero);
        if (!active.Add(key)) return;
        ModuleCallScope? scope = null;
        try {
            var subscribers = Ordered(s => s.Phase == phase); if (subscribers.Length == 0) return;
            var snapshot = Capture(modules);
            var items = snapshot.Concat(machine is null ? Array.Empty<GameItem>() : new[] { machine }).Concat(module is null ? Array.Empty<GameItem>() : new[] { module });
            scope = new(items); var context = new ModuleContext(scope, snapshot, machine, module);
            foreach (var s in subscribers) {
                if (!scope.IsCurrent || !InHook(PhaseHooks)) break;
                if (s.Disposed) continue;
                try { s.Callback!(context); } catch (Exception e) { s.Dispose(); Log(s.Owner + "/" + s.Id, e); }
            }
        } catch (Exception e) { Log("phase:" + phase, e); }
        finally { scope?.Close(); active.Remove(key); }
    }
    private static void BeforeAction(GameItem __0, GameInventory __1, GameItem __2)
    {
        if (!InHook(ActionHooks)) return;
        var active = Active; var key = ("action", __0?.Pointer ?? IntPtr.Zero);
        if (!active.Add(key)) return;
        ModuleCallScope? scope = null;
        try {
            RequireItem(__0); RequireItem(__2);
            if (__1 is null || __1.Pointer == IntPtr.Zero) return;
            scope = new(new[] { __0, __2 }, __1); var context = new ModuleActionContext(scope, __0, __1, __2);
            foreach (var s in Ordered(s => s.Action is not null && s.ModuleId == __0.identifier)) {
                if (!scope.IsCurrent || !InHook(ActionHooks)) break;
                if (s.Disposed) continue;
                try { s.Action!(context); } catch (Exception e) { s.Dispose(); Log(s.Owner + "/" + s.Id, e); }
            }
        } catch (Exception e) { Log("action", e); }
        finally { scope?.Close(); active.Remove(key); }
    }
    private static void BeforeLearning(GameItem __0, out LearningFrame? __state)
    {
        __state = null;
        if (!InHook(LearningHooks)) return;
        try {
            RequireItem(__0);
            if (Pending.Any(f => f.Item?.Pointer == __0.Pointer) || Pending.Count >= 64) return;
            var frame = new LearningFrame { Item = __0, Scope = new(new[] { __0 }) };
            Pending.Add(frame); __state = frame;
            foreach (var s in Ordered(s => s.Prepare is not null)) {
                if (frame.Scope?.IsCurrent != true || !InHook(LearningHooks)) break;
                if (s.Disposed) continue;
                try {
                    var complete = s.Prepare!(__0);
                    if (!s.Disposed && frame.Scope?.IsCurrent == true) frame.Completed.Add((s, complete));
                } catch (Exception e) { s.Dispose(); Log(s.Owner + "/" + s.Id, e); }
            }
        } catch (Exception e) { Log("learning capture", e); }
    }
    private static void AfterLearning(LearningFrame? __state, bool __runOriginal)
    {
        if (__state is null || !__runOriginal || !InHook(LearningHooks)) return;
        foreach (var entry in __state.Completed.ToArray()) {
            if (__state.Scope?.IsCurrent != true || __state.Item is null || !InHook(LearningHooks)) break;
            if (entry.Subscription.Disposed) continue;
            try { entry.Complete(__state.Item); } catch (Exception e) { entry.Subscription.Dispose(); Log(entry.Subscription.Owner + "/" + entry.Subscription.Id, e); }
        }
    }
    // finalizer覆盖原生异常、其它prefix跳过和postfix失败；原样返回异常，不假报成长完成。
    private static Exception? FinishLearning(LearningFrame? __state, Exception? __exception)
    {
        if (__state is not null) { Pending.Remove(__state); __state.Close(); }
        return __exception;
    }
    private static IReadOnlyList<GameItem> Capture(NativeModules? modules)
    {
        if (modules is null || modules.Pointer == IntPtr.Zero) return Array.Empty<GameItem>();
        if (modules.Count > 4096) throw new InvalidOperationException("EZAPI_MODULE_LIMIT: module snapshot exceeds4096; no truncated success.");
        var result = new List<GameItem>();
        for (var i = 0; i < modules.Count; i++) { var item = modules[i]; RequireItem(item); result.Add(item); }
        return Array.AsReadOnly(result.ToArray());
    }
    private static void RequireNative()
    { GameSession.RequireThread(); if (!_started || !_directoryReady) throw new InvalidOperationException("EZAPI_MODULE_NOT_READY: wait for native directory."); }
    private static void ReadyForDeclaration(string owner)
    { GameSession.RequireThread(); RunData.ValidateModId(owner); if (!_started) throw new InvalidOperationException("EZAPI_MODULE_NOT_READY: initialize EZapi first."); }
    private static void RequireItem([System.Diagnostics.CodeAnalysis.NotNull] GameItem? item)
    { if (item is null || item.Pointer == IntPtr.Zero || item.unitCount <= 0) throw new ArgumentException("EZAPI_MODULE_ITEM: live item required."); }
    private static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 160 && value.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c is '.' or '_' or '-');
    private static void ValidateCallbackId(string value)
    { if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(char.IsControl)) throw new ArgumentException("EZAPI_MODULE_CALLBACK_ID: readable ID <=160 required."); }
    private static void Log(string source, Exception e) => MelonLogger.Error("[EZapi] EZAPI_MODULE_CALLBACK " + source + ": " + e.GetType().Name + ": " + e.Message);
    internal static void SceneChanged() { foreach (var frame in Pending) frame.Close(); Pending.Clear(); Active = new(); }
    internal static void Stop()
    {
        _started = false; _directoryReady = false; Generation++; SceneChanged();
        _lastAdmission = null;
        foreach (var subscription in Subscriptions) subscription.Dispose(); Subscriptions.Clear(); Admissions.Clear();
        AdmissionHooks.Stop(); PhaseHooks.Stop(); ActionHooks.Stop(); LearningHooks.Stop();
    }
    internal static object Report() => new { started = _started, directoryReady = _directoryReady, hooks = GetRuntimeStatus(),
        admissions = Admissions.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new { owner=x.Key, entries=x.Value }).ToArray(),
        subscriptions = Subscriptions.Where(s => !s.Disposed).Select(s => new { s.Owner, s.Id, phase=s.Phase?.ToString(), s.ModuleId, s.Order, learning=s.Prepare is not null }).ToArray(), pendingLearning = Pending.Count, lastAdmission = _lastAdmission };
}

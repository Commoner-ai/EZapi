using Il2Cpp;
using MelonLoader;
using Effect = Il2Cpp.ModuleEffectHelper.ModuleEffect;
using EffectTable = Il2CppSystem.Collections.Generic.Dictionary<string, Il2Cpp.ModuleEffectHelper.ModuleEffect>;
namespace EZapi;

/// <summary>原生模块效果声明。Factory只在目录就绪后调用；须返回独立效果且identifier等于EffectId。</summary>
public sealed record NativeEffectRegistration(string EffectId, Func<Effect> Factory, bool RandomEligible = false);
public sealed record ContentSubmissionResult(bool Accepted, string? Reason);

/// <summary>独立效果目录。整批声明不执行工厂；真实应用结果和随机策略见Snapshot/GetRuntimeStatus。</summary>
public static class EffectApi
{
    private sealed record Entry(string Owner, NativeEffectRegistration Definition);
    private sealed record Owned(Entry Entry, EffectTable Table, Effect Effect);
    private sealed class RandomScope
    {
        internal readonly List<Owned> Removed = new();
        internal bool Closed;
    }
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, NativeApplicationView> Views = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Owned> Applied = new(StringComparer.Ordinal);
    private static readonly HashSet<RandomScope> Scopes = new();
    // 原生移除失败时继续持有工厂和效果，不能释放仍在原生目录中的委托目标。
    private static readonly List<Owned> Retained = new();
    private static readonly NativeHookGroup Hooks = new("ezapi.effect-random");
    private static bool _started, _directorySeen, _applying;
    internal static bool IsNativeRegistrationReady => _started && _directorySeen && _error is null && Entries.Count > 0 &&
        Entries.Keys.All(id => Views.TryGetValue(id, out var view) && view.Status == NativeApplicationStatus.Applied);
    private static long _generation;
    private static IntPtr _lastTable;
    private static long _nextCheck;
    private static string? _error;

    public static ContentSubmissionResult RegisterEffect(string ownerId, string effectId, Func<Effect> factory, bool randomEligible = false)
        => RegisterEffects(ownerId, new[] { new NativeEffectRegistration(effectId, factory, randomEligible) });

    internal static bool IsDeclaredForOwner(string ownerId,string effectId)
        => Entries.TryGetValue(effectId,out var entry)&&entry.Owner==ownerId;

    internal static bool TryGetApplied(string ownerId,string effectId,out Effect effect)
    {
        effect=null!;
        if(!Applied.TryGetValue(effectId,out var owned)||owned.Entry.Owner!=ownerId||!SameTable(owned.Table,ModuleEffectHelper.moduleEffects)||
            !owned.Table.TryGetValue(effectId,out var current)||!SameEffect(current,owned.Effect))return false;
        effect=owned.Effect;return true;
    }

    internal static bool IsAppliedObject(string ownerId,string effectId,Effect effect)
        => effect is not null&&Applied.TryGetValue(effectId,out var owned)&&owned.Entry.Owner==ownerId&&SameEffect(owned.Effect,effect)&&
           SameTable(owned.Table,ModuleEffectHelper.moduleEffects)&&owned.Table.TryGetValue(effectId,out var current)&&SameEffect(current,effect);

    /// <summary>最多256项/批、4096项/进程；重复ID/任一无效项拒绝整批。ID属于owner命名空间。</summary>
    public static ContentSubmissionResult RegisterEffects(string ownerId, IReadOnlyList<NativeEffectRegistration> effects)
    {
        GameSession.RequireThread(); RunData.ValidateModId(ownerId); ArgumentNullException.ThrowIfNull(effects);
        if (!_started) return new(false, "EZAPI_EFFECT_NOT_READY: initialize EZapi first.");
        if (_directorySeen) return new(false, "EZAPI_EFFECT_TOO_LATE: register during initialization.");
        if (effects.Count is < 1 or > 256 || Entries.Count + effects.Count > 4096)
            return new(false, "EZAPI_EFFECT_LIMIT: max256 per batch /4096 total.");
        var copy = effects.ToArray(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in copy) {
            if (e is null || e.Factory is null || string.IsNullOrWhiteSpace(e.EffectId) || e.EffectId.Length > 160 ||
                e.EffectId.Length <= ownerId.Length + 1 || !e.EffectId.StartsWith(ownerId + ".", StringComparison.Ordinal) || e.EffectId.Any(c => !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c is '.' or '_' or '-')))
                return new(false, "EZAPI_EFFECT_ID: nonnull factory and owner-prefixed ASCII ID <=160 required.");
            if (!ids.Add(e.EffectId) || Entries.ContainsKey(e.EffectId)) return new(false, "EZAPI_EFFECT_DUPLICATE: " + e.EffectId);
        }
        foreach (var e in copy) {
            var entry = new Entry(ownerId, e); Entries.Add(e.EffectId, entry);
            Set(entry, NativeApplicationStatus.Staged, null);
        }
        ContentApi.PublishBatch(copy.Select(e => Views[e.EffectId]));
        return new(true, null);
    }
    public static IReadOnlyList<NativeApplicationView> Snapshot()
    {
        GameSession.RequireThread();
        return Array.AsReadOnly(Views.Values.OrderBy(v => v.OwnerId, StringComparer.Ordinal).ThenBy(v => v.ContentId, StringComparer.Ordinal).ToArray());
    }
    public static NativeHookStatus GetRuntimeStatus() { GameSession.RequireThread(); return Hooks.Snapshot(); }
    internal static void Start() { if (_started) return; _started = true; _generation++; _error = null; }

    internal static void Apply()
    {
        if (!_started || _applying) return;
        GameSession.RequireThread(); _directorySeen = true;
        if (Entries.Count == 0) return; // 无声明不触发原生效果类型初始化，也不安装补丁。
        _applying = true; var generation = _generation;
        var notifications = new List<NativeApplicationView>();
        try {
            var table = ModuleEffectHelper.moduleEffects ?? throw new InvalidOperationException("native moduleEffects table unavailable.");
            if (table.Pointer == _lastTable) return;
            _error = null;
            // 随机策略必需钩子先全部安装，再允许任何excluded效果进入目录。
            if (Entries.Values.Any(e => !e.Definition.RandomEligible))
                Hooks.Install(typeof(EffectApi), new NativeHookSpec(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.InitRandomEffect),
                    typeof(void), new[] { typeof(GameItem), typeof(int) }, Prefix: nameof(RandomPrefix), Finalizer: nameof(RandomFinalizer)));
            var randomReady = Hooks.Snapshot().Ready;
            foreach (var e in Entries.Values.ToArray()) {
                if (!_started || generation != _generation) break;
                var id = e.Definition.EffectId;
                Owned? newlyInserted = null;
                try {
                    if (!e.Definition.RandomEligible && !randomReady)
                        throw new InvalidOperationException("EZAPI_EFFECT_RANDOM_UNAVAILABLE: excluded effect not installed because random guard is unavailable.");
                    if (table.TryGetValue(id, out var prior)) {
                        var owned = Applied.TryGetValue(id, out var old) && SameTable(table, old.Table) && SameEffect(prior, old.Effect);
                        Set(e, owned ? NativeApplicationStatus.Applied : NativeApplicationStatus.Conflict, owned ? null : "native effect ID already exists.");
                    } else {
                        var effect = e.Definition.Factory();
                        if (!_started || generation != _generation || !SameTable(table, ModuleEffectHelper.moduleEffects))
                            throw new InvalidOperationException("EZAPI_EFFECT_STALE: registry changed during factory; no insertion.");
                        if (effect is null || effect.Pointer == IntPtr.Zero || effect.identifier != id)
                            throw new InvalidOperationException("EZAPI_EFFECT_FACTORY: live independent effect with matching identifier required.");
                        // 工厂可重入其它原生注册；提交前再次检查，不能覆盖刚写入的同名效果。
                        if (table.ContainsKey(id)) { Set(e, NativeApplicationStatus.Conflict, "native effect ID inserted during factory."); }
                        else {
                            if (Applied.TryGetValue(id, out var previous)) Retained.Add(previous);
                            var ownership = new Owned(e, table, effect); Applied[id] = ownership; newlyInserted = ownership;
                            table.Add(id, effect);
                            if (!table.TryGetValue(id, out var actual) || !SameEffect(actual, effect))
                                throw new InvalidOperationException("EZAPI_EFFECT_APPLY_READBACK: native entry differs after Add.");
                            Set(e, NativeApplicationStatus.Applied, null);
                        }
                    }
                } catch (Exception ex) {
                    if (!_started || generation != _generation) break;
                    if (newlyInserted is not null) { Withdraw(newlyInserted); Applied.Remove(id); }
                    Set(e, NativeApplicationStatus.Failed, ex.GetType().Name + ": " + ex.Message);
                    MelonLogger.Error("[EZapi] EZAPI_EFFECT_APPLY_FAILED " + id + ": " + ex.Message);
                }
                notifications.Add(Views[id]);
            }
            if (_started && generation == _generation && (randomReady || Entries.Values.All(e => e.Definition.RandomEligible)))
                _lastTable = table.Pointer;
        } catch (Exception error) {
            _error = "EZAPI_EFFECT_DIRECTORY: " + error.Message;
            MelonLogger.Error("[EZapi] " + _error); // 不标记目录已应用，后续官方目录事件可重试。
        } finally { _applying = false; }
        ContentApi.PublishBatch(notifications);
    }

    // Harmony每次调用独立__state。只用finalizer释放，避免Postfix+Finalizer双重减深度。
    private static void RandomPrefix(out RandomScope? __state)
    {
        __state = null;
        if (!_started || !Hooks.Snapshot().Ready) return;
        GameSession.RequireThread();
        var table = ModuleEffectHelper.moduleEffects;
        if (table is null) return;
        var scope = new RandomScope(); __state = scope; Scopes.Add(scope);
        try {
            foreach (var owned in Applied.Values.ToArray()) {
                var id = owned.Entry.Definition.EffectId;
                if (owned.Entry.Definition.RandomEligible || !SameTable(table, owned.Table) ||
                    !table.TryGetValue(id, out var current) || !SameEffect(current, owned.Effect)) continue;
                scope.Removed.Add(owned); // Remove写一半抛异常时，finalizer仍能恢复该项。
                if (!table.Remove(id) || table.ContainsKey(id)) throw new InvalidOperationException("native removal readback failed: " + id);
            }
        } catch (Exception error) {
            Restore(scope);
            throw new InvalidOperationException("EZAPI_EFFECT_RANDOM_SCOPE: random invocation refused; " + error.Message, error);
        }
    }
    private static Exception? RandomFinalizer(Exception? __exception, RandomScope? __state)
    { if (__state is not null) Restore(__state); return __exception; }
    private static void Restore(RandomScope scope)
    {
        if (scope.Closed) return;
        scope.Closed = true; Scopes.Remove(scope);
        var notifications = new List<NativeApplicationView>();
        foreach (var owned in scope.Removed) {
            try {
                var id = owned.Entry.Definition.EffectId;
                // 只恢复曾经移除的那张表；不污染替换后的新目录，不覆盖别人后来注册的同名定义。
                if (!owned.Table.ContainsKey(id)) owned.Table.Add(id, owned.Effect);
                if (!owned.Table.TryGetValue(id, out var current)) throw new InvalidOperationException("native restore readback missing: " + id);
                if (!SameEffect(current, owned.Effect)) {
                    Set(owned.Entry, NativeApplicationStatus.Conflict, "effect replaced during random invocation; replacement preserved.");
                    notifications.Add(Views[id]);
                }
            } catch (Exception error) {
                _error = "EZAPI_EFFECT_RANDOM_RESTORE: " + error.Message;
                Set(owned.Entry, NativeApplicationStatus.Failed, _error); Retained.Add(owned);
                notifications.Add(Views[owned.Entry.Definition.EffectId]);
                MelonLogger.Error("[EZapi] " + _error); // 保留原始游戏异常，不能在finalizer吞掉或替换它。
            }
        }
        ContentApi.PublishBatch(notifications);
    }
    internal static void Maintain()
    {
        if (!_started || Applied.Count == 0 || Environment.TickCount64 < _nextCheck) return;
        _nextCheck = Environment.TickCount64 + 1000;
        if (!Applied.Values.Any(o => !o.Entry.Definition.RandomEligible) || Hooks.Snapshot().Ready) return;
        var notifications = new List<NativeApplicationView>();
        // owner丢失时撤下本方excluded定义；只清理本方确切对象，不覆盖其它Mod。
        foreach (var o in Applied.Values.Where(o => !o.Entry.Definition.RandomEligible).ToArray()) {
            Withdraw(o); Applied.Remove(o.Entry.Definition.EffectId);
            Set(o.Entry, NativeApplicationStatus.Failed, "EZAPI_EFFECT_RANDOM_LOST: hook unavailable; withdrawal attempted; inspect error for cleanup failures.");
            notifications.Add(Views[o.Entry.Definition.EffectId]);
        }
        ContentApi.PublishBatch(notifications);
    }
    private static void Withdraw(Owned o)
    {
        try {
            var id = o.Entry.Definition.EffectId;
            if (o.Table.TryGetValue(id, out var current) && SameEffect(current, o.Effect)) {
                o.Table.Remove(id);
                if (o.Table.TryGetValue(id, out current) && SameEffect(current, o.Effect))
                    throw new InvalidOperationException("native removal readback failed.");
            }
        } catch (Exception e) { Retained.Add(o); _error = "EZAPI_EFFECT_CLEANUP: " + e.Message; MelonLogger.Error("[EZapi] " + _error); }
    }
    internal static void Stop()
    {
        _started = false; _generation++;
        foreach (var scope in Scopes.ToArray()) Restore(scope);
        foreach (var o in Applied.Values.ToArray()) Withdraw(o);
        foreach (var o in Retained.ToArray()) WithdrawRetained(o);
        Hooks.Stop(); Entries.Clear(); Views.Clear(); Applied.Clear(); Scopes.Clear();
        _directorySeen = false; _lastTable = IntPtr.Zero; _nextCheck = 0;
    }
    private static void WithdrawRetained(Owned o)
    {
        try {
            var id = o.Entry.Definition.EffectId;
            if (o.Table.TryGetValue(id, out var current) && SameEffect(current, o.Effect)) o.Table.Remove(id);
            if (!o.Table.TryGetValue(id, out current) || !SameEffect(current, o.Effect)) Retained.Remove(o);
        } catch (Exception e) { _error = "EZAPI_EFFECT_CLEANUP: " + e.Message; MelonLogger.Error("[EZapi] " + _error); }
    }
    private static bool SameEffect(Effect? a, Effect b) => a is not null && a.Pointer == b.Pointer;
    private static bool SameTable(EffectTable a, EffectTable? b) => b is not null && a.Pointer == b.Pointer;
    private static void Set(Entry e, NativeApplicationStatus status, string? reason)
        => Views[e.Definition.EffectId] = new(e.Owner, e.Definition.EffectId, "effect", status, reason);
    internal static object Report() => new { ready = _started, directorySeen = _directorySeen, hook = Hooks.Snapshot(),
        entries = Snapshot(), declarations = Entries.Values.Select(e => new { ownerId=e.Owner, e.Definition.EffectId, e.Definition.RandomEligible }).ToArray(),
        pendingRandomScopes = Scopes.Count, retainedAfterCleanupFailure = Retained.Count, error = _error };
}

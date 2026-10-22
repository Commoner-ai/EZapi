using EZapi.Registration;
using Il2Cpp;

namespace EZapi;

/// <summary>
/// 初始化期登记 Mod 开局身份与游戏原生 NewGameData.StartType 的映射。
/// 该注册表不添加游戏菜单选项、不修改原生开局配置，也不发放物品或 perk。
/// </summary>
public static class StartApi
{
    private static readonly Dictionary<string, StartRegistrationView> Entries = new(StringComparer.Ordinal);
    private static bool _started;
    private static int _thread;

    /// <summary>登记一个命名空间开局身份。必须在 EZapi 初始化线程且原生物品目录初始化前调用。</summary>
    public static SubmitResult RegisterStart(string ownerId, string startId, int nativeStartType)
    {
        if (!_started)
            return Invalid("EZAPI_START_NOT_READY: initialize EZapi before registering starts.");
        if (Environment.CurrentManagedThreadId != _thread)
            return Invalid("EZAPI_START_THREAD: register starts on the EZapi initialization thread.");
        if (!PresentationRules.Owner(ownerId))
            return Invalid("EZAPI_START_OWNER: ownerId must be a lowercase namespaced Mod ID.");
        if (!PresentationRules.Owned(ownerId, startId))
            return Invalid("EZAPI_START_ID: startId must belong to the owner namespace.");
        if (!Enum.IsDefined(typeof(NewGameData.StartType), nativeStartType))
            return Invalid("EZAPI_START_NATIVE_TYPE: value is not defined by the current NewGameData.StartType enum.");

        if (Entries.TryGetValue(startId, out var current))
        {
            if (StringComparer.Ordinal.Equals(current.OwnerId, ownerId) && current.NativeStartType == nativeStartType)
                return new(SubmitStatus.AlreadyPresent, "");
            return new(SubmitStatus.Conflict, "EZAPI_START_CONFLICT: startId is already mapped to another owner or native type.");
        }

        var phaseError = ItemRegistry.DeclarationRegistrationError();
        if (phaseError is not null)
            return Invalid("EZAPI_START_PHASE: " + phaseError);

        Entries.Add(startId, new(ownerId, startId, nativeStartType));
        return new(SubmitStatus.Accepted, "");
    }

    /// <summary>用游戏官方枚举登记，避免作者硬编码数值；非当前枚举成员仍会被拒绝。</summary>
    public static SubmitResult RegisterStart(string ownerId, string startId, NewGameData.StartType nativeStartType)
        => RegisterStart(ownerId, startId, (int)nativeStartType);

    /// <summary>返回按 ownerId、startId 排序的只读映射快照。</summary>
    public static IReadOnlyList<StartRegistrationView> Snapshot()
    {
        RequireThread();
        return Array.AsReadOnly(Entries.Values
            .OrderBy(entry => entry.OwnerId, StringComparer.Ordinal)
            .ThenBy(entry => entry.StartId, StringComparer.Ordinal)
            .ToArray());
    }

    internal static void Start()
    {
        if (_started) return;
        _thread = Environment.CurrentManagedThreadId;
        _started = true;
    }

    internal static void Stop()
    {
        Entries.Clear();
        _thread = 0;
        _started = false;
    }

    internal static object Report() => new
    {
        ready = _started,
        count = Entries.Count,
        entries = Entries.Values
            .OrderBy(entry => entry.OwnerId, StringComparer.Ordinal)
            .ThenBy(entry => entry.StartId, StringComparer.Ordinal)
            .ToArray()
    };

    private static void RequireThread()
    {
        if (!_started || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("EZAPI_START_THREAD: Snapshot requires the active EZapi main thread.");
    }

    private static SubmitResult Invalid(string reason) => new(SubmitStatus.Invalid, reason);
}

using EZapi.Registration;

namespace EZapi;

internal sealed class WorkshopRegistration
{
    internal readonly string OwnerId, Id, ChineseTitle, EnglishTitle;
    internal readonly bool IsChain;
    internal readonly Func<WorkshopSnapshot> SnapshotProvider;
    internal readonly Action<string> Unlock;

    internal WorkshopRegistration(string ownerId, string id, string chineseTitle, string englishTitle,
        bool isChain, Func<WorkshopSnapshot> snapshot, Action<string> unlock)
        => (OwnerId, Id, ChineseTitle, EnglishTitle, IsChain, SnapshotProvider, Unlock)
            = (ownerId, id, chineseTitle, englishTitle, isChain, snapshot, unlock);

    internal WorkshopTabHeader Header => new(Id, ChineseTitle, EnglishTitle);

    internal bool SameAs(WorkshopRegistration other)
        => OwnerId == other.OwnerId && Id == other.Id && ChineseTitle == other.ChineseTitle &&
           EnglishTitle == other.EnglishTitle && IsChain == other.IsChain &&
           SnapshotProvider == other.SnapshotProvider && Unlock == other.Unlock;
}

internal sealed record WorkshopActionResult(bool Invoked, WorkshopSnapshot? Snapshot, string? Error);

/// <summary>
/// 作者注册只读工坊页和动作回调。EZapi 提供独立 UI 与快照隔离；支付、物品扣除、奖励和保存仍由作者回调负责。
/// </summary>
public static class WorkshopApi
{
    private const int MaxRegistrations = 128;
    private static readonly Dictionary<string, WorkshopRegistration> Registrations = new(StringComparer.Ordinal);
    private static bool _started;
    private static int _thread;

    /// <summary>当前 EZapi 工坊窗口是否显示。</summary>
    public static bool IsVisible => WorkshopBrowser.Visible;

    /// <summary>注册一个工坊标签。必须在 EZapi 初始化线程调用；ID 必须属于 ownerId 命名空间。</summary>
    public static SubmitResult RegisterTab(string ownerId, string tabId, string chineseTitle, string englishTitle,
        Func<WorkshopSnapshot> snapshot, Action<string> unlock)
        => Register(ownerId, tabId, chineseTitle, englishTitle, false, snapshot, unlock);

    /// <summary>注册一个独立进度链；快照与解锁回调仍完全由作者提供。</summary>
    public static SubmitResult RegisterChain(string ownerId, string chainId, string chineseTitle, string englishTitle,
        Func<WorkshopSnapshot> snapshot, Action<string> unlock)
        => Register(ownerId, chainId, chineseTitle, englishTitle, true, snapshot, unlock);

    /// <summary>打开指定标签。快照提供器异常或快照无效时返回 false 并保留诊断。</summary>
    public static bool OpenTab(string tabId) => Open(tabId, isChain: false);

    /// <summary>关闭 EZapi 工坊窗口并释放其快照和回调引用。</summary>
    public static void CloseWindow()
    {
        RequireThread();
        WorkshopBrowser.Close();
    }

    /// <summary>打开指定进度链。</summary>
    public static bool OpenChain(string chainId) => Open(chainId, isChain: true);

    internal static IReadOnlyList<WorkshopRegistration> RegisteredTabs()
    {
        RequireThread();
        return Array.AsReadOnly(Registrations.Values.Where(value => !value.IsChain)
            .OrderBy(value => value.OwnerId, StringComparer.Ordinal).ThenBy(value => value.Id, StringComparer.Ordinal).ToArray());
    }

    internal static bool TryGet(string id, bool isChain, out WorkshopRegistration registration)
    {
        RequireThread();
        if (Registrations.TryGetValue(id, out var value) && value.IsChain == isChain)
        {
            registration = value;
            return true;
        }
        registration = null!;
        return false;
    }

    internal static WorkshopSnapshot Capture(WorkshopRegistration registration)
    {
        var frozen=WorkshopDefinitionRules.Copy(registration.OwnerId, registration.SnapshotProvider());
        return WorkshopDefinitionRules.ResolveLocale(frozen,frozen.FollowGameLanguage?NativeItemLocalization.CurrentLocaleCode:(frozen.English?"en":"zh"));
    }

    /// <summary>执行一次当前快照允许的作者动作；异常不重放，刷新异常也如实返回。</summary>
    internal static WorkshopActionResult RunAction(WorkshopRegistration registration, string entryId, ISet<string> attempted)
    {
        RequireThread();
        if (!Registrations.TryGetValue(registration.Id, out var currentRegistration) || !ReferenceEquals(currentRegistration, registration))
            return new(false, null, "EZAPI_WORKSHOP_STALE: registration is no longer active.");
        if (!WorkshopBrowser.IsShowing(registration))
            return new(false, null, "EZAPI_WORKSHOP_STALE: the requested page is no longer visible.");
        if (attempted.Contains(entryId))
            return new(false, null, "EZAPI_WORKSHOP_ACTION_USED: this entry was already attempted in this window.");

        WorkshopSnapshot snapshot;
        try { snapshot = Capture(registration); }
        catch (Exception error) { return new(false, null, "EZAPI_WORKSHOP_SNAPSHOT: " + error); }
        var entry = snapshot.Entries.FirstOrDefault(value => StringComparer.Ordinal.Equals(value.Id, entryId));
        if (entry is null || entry.Unlocked || !entry.PrerequisiteMet || !entry.ActionEnabled)
            return new(false, snapshot, "EZAPI_WORKSHOP_ACTION_UNAVAILABLE: latest snapshot does not enable this entry.");

        attempted.Add(entryId); // 消费执行权后才调用作者；失败也不在同一窗口自动重试。
        string? callbackError = null;
        try { registration.Unlock(entryId); }
        catch (Exception error) { callbackError = "EZAPI_WORKSHOP_UNLOCK: " + error; }

        // The callback may close the window, switch tabs, or unload the scene. Do not query its old provider afterward.
        if (!WorkshopBrowser.IsShowing(registration))
            return new(true, null, callbackError);

        try
        {
            var refreshed = Capture(registration);
            return new(true, refreshed, callbackError);
        }
        catch (Exception error)
        {
            var refreshError = "EZAPI_WORKSHOP_REFRESH: " + error;
            return new(true, null, callbackError is null ? refreshError : callbackError + "\n" + refreshError);
        }
    }

    internal static void Start()
    {
        if (_started) return;
        _thread = Environment.CurrentManagedThreadId;
        _started = true;
    }

    internal static void Stop()
    {
        if (_started && Environment.CurrentManagedThreadId != _thread) return;
        WorkshopBrowser.Close();
        Registrations.Clear();
        _thread = 0;
        _started = false;
    }

    internal static object Report() => new
    {
        ready = _started,
        visible = WorkshopBrowser.Visible,
        count = Registrations.Count,
        tabs = Registrations.Values.Where(value => !value.IsChain)
            .OrderBy(value => value.OwnerId, StringComparer.Ordinal).ThenBy(value => value.Id, StringComparer.Ordinal)
            .Select(value => new { value.OwnerId, value.Id, value.ChineseTitle, value.EnglishTitle }).ToArray(),
        chains = Registrations.Values.Where(value => value.IsChain)
            .OrderBy(value => value.OwnerId, StringComparer.Ordinal).ThenBy(value => value.Id, StringComparer.Ordinal)
            .Select(value => new { value.OwnerId, value.Id, value.ChineseTitle, value.EnglishTitle }).ToArray(),
        browser = WorkshopBrowser.Report()
    };

    private static SubmitResult Register(string ownerId, string id, string chineseTitle, string englishTitle,
        bool isChain, Func<WorkshopSnapshot> snapshot, Action<string> unlock)
    {
        if (!_started) return Invalid("EZAPI_WORKSHOP_NOT_READY: initialize EZapi before registering workshop content.");
        if (Environment.CurrentManagedThreadId != _thread)
            return Invalid("EZAPI_WORKSHOP_THREAD: register workshop content on the EZapi initialization thread.");
        if (!PresentationRules.Owned(ownerId, id))
            return Invalid("EZAPI_WORKSHOP_ID: IDs must use the lowercase owner namespace.");
        if (!PresentationRules.Text(chineseTitle, 256) || string.IsNullOrWhiteSpace(chineseTitle) ||
            !PresentationRules.Text(englishTitle, 256) || string.IsNullOrWhiteSpace(englishTitle))
            return Invalid("EZAPI_WORKSHOP_TITLE: nonempty Chinese and English titles <=256 characters required.");
        if (snapshot is null || unlock is null)
            return Invalid("EZAPI_WORKSHOP_CALLBACK: snapshot and unlock callbacks are required.");

        var registration = new WorkshopRegistration(ownerId, id, chineseTitle, englishTitle, isChain, snapshot, unlock);
        if (Registrations.TryGetValue(id, out var existing))
            return existing.SameAs(registration)
                ? new(SubmitStatus.AlreadyPresent, "")
                : new(SubmitStatus.Conflict, "EZAPI_WORKSHOP_CONFLICT: ID is already registered by another owner or definition.");
        if (Registrations.Count >= MaxRegistrations)
            return new(SubmitStatus.Conflict, "EZAPI_WORKSHOP_LIMIT: at most 128 tabs/chains per process.");

        Registrations.Add(id, registration);
        return new(SubmitStatus.Accepted, "");
    }

    private static bool Open(string id, bool isChain)
    {
        if (!_started || Environment.CurrentManagedThreadId != _thread || !PresentationRules.Owner(id)) return false;
        if (!Registrations.TryGetValue(id, out var registration) || registration.IsChain != isChain) return false;
        return WorkshopBrowser.Open(registration);
    }

    private static void RequireThread()
    {
        if (!_started || Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("EZAPI_WORKSHOP_THREAD: use the active EZapi main thread.");
    }

    private static SubmitResult Invalid(string reason) => new(SubmitStatus.Invalid, reason);
}

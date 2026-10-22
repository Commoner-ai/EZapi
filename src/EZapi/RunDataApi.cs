using System.Text.Json;
using Il2Cpp;
using NativeData = Il2CppSystem.Collections.Generic.Dictionary<string, string>;

namespace EZapi;

/// <summary>内存记录状态；没有任何状态表示游戏存档已落盘。</summary>
public enum RunDataStatus
{
    Disabled, Invalid, Unavailable, Missing, Present, StagedInMemory, Conflict, Indeterminate
}

/// <summary>Json 是原始 JSON 文本，也是下次 Stage 的比较令牌；不要重新格式化后当作旧值。</summary>
public sealed record RunDataResult(RunDataStatus Status, string? Json = null, string? Reason = null);

/// <summary>
/// 带冲突检查的周目 JSON 记录。key 必须是 ownerId + "." 开头的完整键。
/// 使用公开 PlayerStore.modData；不加载 Forge、不保存文件、不自动迁移或删除旧数据。
/// 仅在游戏主线程的当前载入中执行；保留 RunData.Open 的简单键值接口与旧命名空间。
/// </summary>
public static class RunDataApi
{
    public const int MaxJsonCharacters = 1_048_576;
    public const int MaxJsonDepth = 64;

    /// <summary>自动定位当前玩家；内容 Mod 通常使用此重载，不需要保留 PlayerStore。</summary>
    public static RunDataResult Read(string ownerId, string key)
    {
        if (!GameSession.OnThread) return Disabled();
        try { return Read(PlayerStore.instance, ownerId, key); }
        catch (Exception error) { return Unavailable(error.GetType().Name); }
    }

    /// <summary>使用游戏事件里的 Run 快照提交；同一槽重新载入也会因 epoch 过期而拒绝。</summary>
    public static RunDataResult Stage(string ownerId, string key, GameRunIdentity identity,
        string? expectedJson, string nextJson)
    {
        if (!GameSession.OnThread) return Disabled();
        if (identity is null) return Invalid("EZAPI_RUN_RECORD_INPUT: current event Run identity required.");
        try
        {
            if (!GameSession.TryResolve(identity.Epoch, out var store) || GameSession.Identity != identity)
                return Conflict("EZAPI_RUN_RECORD_IDENTITY: event run identity expired; read again in the current event.");
            return Stage(store, ownerId, key, identity.RunId, identity.SaveSlotId, expectedJson, nextJson);
        }
        catch (Exception error) { return Unavailable(error.GetType().Name); }
    }

    /// <summary>只读原始 JSON 对象；缺键、坏数据和不可用周目分别返回不同状态。</summary>
    public static RunDataResult Read(PlayerStore? store, string ownerId, string key)
    {
        if (!GameSession.OnThread) return Disabled();
        if (!ValidKey(ownerId, key)) return Invalid("EZAPI_RUN_RECORD_KEY: use a full key beginning with ownerId + '.'.");
        try
        {
            if (!TryCurrent(store, out var identity) || store!.modData is not { } data)
                return Unavailable();
            var found = data.TryGetValue(key, out var json);
            if (!SameContext(store, identity, data)) return Unavailable();
            if (!found) return new(RunDataStatus.Missing);
            return ValidJson(json) ? new(RunDataStatus.Present, json)
                : Invalid("EZAPI_RUN_RECORD_JSON: stored value is not a valid bounded JSON object; it was preserved.");
        }
        catch (Exception error) { return Unavailable(error.GetType().Name); }
    }

    /// <summary>
    /// 比较槽/周目与原始旧值后暂存 nextJson。expectedJson=null 表示要求键不存在。
    /// 写入前重复检查，写入后读回；一旦尝试写入而无法确认结果，返回 Indeterminate，禁止盲目重试。
    /// </summary>
    public static RunDataResult Stage(PlayerStore? store, string ownerId, string key,
        string runId, int slotId, string? expectedJson, string nextJson)
    {
        if (!GameSession.OnThread) return Disabled();
        if (!ValidKey(ownerId, key) || string.IsNullOrWhiteSpace(runId) || slotId < 0 ||
            !ValidJson(nextJson) || (expectedJson is not null && !ValidJson(expectedJson)))
            return Invalid("EZAPI_RUN_RECORD_INPUT: valid owner/key, run identity and JSON objects required.");
        var attempted = false;
        try
        {
            if (!TryCurrent(store, out var identity) || store!.modData is not { } data)
                return Unavailable();
            if (identity.RunId != runId || identity.SaveSlotId != slotId)
                return Conflict("EZAPI_RUN_RECORD_IDENTITY: run or save slot changed.");
            if (!MatchesValue(data, key, expectedJson)) return Conflict("EZAPI_RUN_RECORD_REVISION: stored value changed.");

            // 原生包装器访问也可能触发其它 Mod：写前再次核对载入 epoch、字典和旧值。
            if (!SameContext(store, identity, data) || !MatchesValue(data, key, expectedJson) ||
                !SameContext(store, identity, data))
                return Conflict("EZAPI_RUN_RECORD_PREFLIGHT: context or value changed before write.");
            attempted = true;
            if (expectedJson is null) data.Add(key, nextJson);
            else data[key] = nextJson;

            if (!SameContext(store, identity, data) || !MatchesValue(data, key, nextJson) ||
                !SameContext(store, identity, data))
                return new(RunDataStatus.Indeterminate, Reason: "EZAPI_RUN_RECORD_UNCONFIRMED: write attempted; reconcile before retrying.");
            return new(RunDataStatus.StagedInMemory, nextJson);
        }
        catch (Exception error)
        {
            return attempted
                ? new(RunDataStatus.Indeterminate, Reason: "EZAPI_RUN_RECORD_UNCONFIRMED: " + error.GetType().Name)
                : Unavailable(error.GetType().Name);
        }
    }

    private static bool TryCurrent(PlayerStore? store, out GameRunIdentity identity)
    {
        identity = null!;
        if (store is null || !GameSession.TryResolve(null, out var current) ||
            store.Pointer == IntPtr.Zero || store.Pointer != current.Pointer) return false;
        identity = GameSession.Identity!;
        return true;
    }

    private static bool SameContext(PlayerStore store, GameRunIdentity identity, NativeData data)
        => GameSession.TryResolve(identity.Epoch, out var current) && GameSession.Identity == identity &&
            store.Pointer == current.Pointer && store.modData?.Pointer == data.Pointer;

    private static bool MatchesValue(NativeData data, string key, string? expected)
    {
        var found = data.TryGetValue(key, out var actual);
        return expected is null ? !found : found && string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static bool ValidKey(string ownerId, string key)
    {
        try { RunData.ValidateModId(ownerId); }
        catch (ArgumentException) { return false; }
        return key is not null && key.Length <= 320 && key.Length > ownerId.Length + 1 &&
            key.StartsWith(ownerId + ".", StringComparison.Ordinal) &&
            key.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.');
    }

    private static bool ValidJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonCharacters) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }

    private static RunDataResult Disabled() => new(RunDataStatus.Disabled, Reason: "EZAPI_RUN_RECORD_THREAD: use the initialized game main thread.");
    private static RunDataResult Invalid(string reason) => new(RunDataStatus.Invalid, Reason: reason);
    private static RunDataResult Unavailable(string? reason = null) => new(RunDataStatus.Unavailable, Reason: reason ?? "EZAPI_RUN_RECORD_UNAVAILABLE: current loaded player and modData required.");
    private static RunDataResult Conflict(string reason) => new(RunDataStatus.Conflict, Reason: reason);
}

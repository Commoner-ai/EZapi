using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Il2Cpp;
using Il2CppInterop.Runtime;

namespace EZapi;

/// <summary>单个物品标签数据的读取/暂存结果；StagedInMemory 不表示游戏文件已保存。</summary>
public enum ItemDataStatus
{
    Disabled, Invalid, Unavailable, Missing, Present, VersionMismatch, Frozen,
    StagedInMemory, Conflict, Indeterminate
}

/// <summary>JSON 读回结果。Json 保留原始 envelope 文本，可用于比较暂存或显式迁移。</summary>
public sealed record ItemDataReadResult<T>(ItemDataStatus Status, T? Value = default,
    string? Json = null, int? Version = null, string? Reason = null)
{
    /// <summary>仅 Missing/Present/VersionMismatch 的有效读取提供比较令牌；不可保存或跨载入复用。</summary>
    public ItemDataRevision? Revision { get; internal init; }
}

/// <summary>只读比较令牌，不持有原生对象。由 Read 产生，用于同一物品和周目的 Stage；禁止序列化保存。</summary>
public sealed class ItemDataRevision
{
    internal readonly ItemDataApi.Identity Identity;
    internal readonly string TagKey, OwnerPrefix;
    internal ItemDataRevision(ItemDataApi.Identity identity, string tagKey, string ownerPrefix, string? json)
        => (Identity, TagKey, OwnerPrefix, Json) = (identity, tagKey, ownerPrefix, json);
    /// <summary>读取时的原始 envelope；null 表示当时标签不存在。</summary>
    public string? Json { get; }
    /// <summary>读取时的载入身份；同槽重载也使令牌失效。</summary>
    public GameRunIdentity Run => Identity.Run;
}

/// <summary>物品标签 JSON 写入结果；不触发游戏存档，也不承诺落盘。</summary>
public sealed record ItemDataWriteResult(ItemDataStatus Status, string? Json = null, string? Reason = null);

/// <summary>
/// 读写绑定在物品自身上的、带命名空间和格式版本的 JSON 数据。
/// 每次调用重新验证当前周目、物品实例、冻结状态及标签读回；只暂存内存物品状态。
/// </summary>
public static class ItemDataApi
{
    public const string TagPrefix = "EZAPI_ITEM_DATA_V1|";
    public const int MaxJsonUtf8Bytes = 16 * 1024;
    public const int MaxJsonDepth = 16;
    public const int MaxOwnerUtf8Bytes = 64 * 1024;
    public const int MaxOwnerKeys = 128;
    private static readonly HashSet<IntPtr> Writing = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = MaxJsonDepth };

    /// <summary>
    /// 读取并反序列化物品标签数据。缺失、损坏和版本不符分别报告；不会创建标签或自动迁移。
    /// ownerId 与 key 组合成隔离 tag key；expectedVersion 默认为 1。
    /// </summary>
    public static ItemDataReadResult<T> Read<T>(GameItem item, string ownerId, string key, int expectedVersion = 1)
    {
        if (!GameSession.OnThread) return ReadResult<T>(ItemDataStatus.Disabled, reason: "EZAPI_ITEM_DATA_THREAD: use the initialized game main thread.");
        if (!ValidAddress(ownerId, key) || expectedVersion < 1)
            return ReadResult<T>(ItemDataStatus.Invalid, reason: "EZAPI_ITEM_DATA_INPUT: valid owner, key and version >= 1 required.");
        try
        {
            var run = NativeAccess.RequireRun();
            if (!GameBuildFingerprint.IsKnown) return ReadResult<T>(ItemDataStatus.Unavailable, reason: "EZAPI_ITEM_DATA_BUILD: unsupported game build.");
            var identity = Capture(item);
            if (identity is null || item.state?.dict is not { } dictionary)
                return ReadResult<T>(ItemDataStatus.Unavailable, reason: "EZAPI_ITEM_DATA_UNAVAILABLE: live item state and tag dictionary required.");
            var statePointer = item.state.Pointer;
            var dictionaryPointer = dictionary.Pointer;
            var tagKey = MakeTagKey(ownerId, key);
            var ownerPrefix = TagPrefix + ownerId + "|";
            var tag = item.GetTagReadonly(tagKey);
            if (!dictionary.ContainsKey(tagKey) && IsMissingTag(tag))
                return Stable<T>(run, identity, statePointer, dictionaryPointer, item)
                    ? ReadResult<T>(ItemDataStatus.Missing) with { Revision = new(identity, tagKey, ownerPrefix, null) }
                    : ReadResult<T>(ItemDataStatus.Unavailable, reason: "EZAPI_ITEM_DATA_STALE: item or run changed while reading.");
            if (tag is null || tag.Pointer == IntPtr.Zero || !tag.IsEnabled())
                return ReadResult<T>(ItemDataStatus.Invalid, reason: "EZAPI_ITEM_DATA_TAG: namespaced tag exists but is unavailable or disabled; it was preserved.");

            var json = tag.GetString();
            var envelope = ParseEnvelope(json);
            if (envelope.Status != ItemDataStatus.Present)
                return ReadResult<T>(envelope.Status, json: json, version: envelope.Version, reason: envelope.Reason);
            if (!Stable<T>(run, identity, statePointer, dictionaryPointer, item))
                return ReadResult<T>(ItemDataStatus.Unavailable, json: json, version: envelope.Version,
                    reason: "EZAPI_ITEM_DATA_STALE: item or run changed while reading.");
            if (envelope.Version != expectedVersion)
                return ReadResult<T>(ItemDataStatus.VersionMismatch, json: json, version: envelope.Version,
                    reason: "EZAPI_ITEM_DATA_VERSION: stored data needs an explicit migration.") with { Revision = new(identity, tagKey, ownerPrefix, json) };
            T? value;
            try { value = envelope.Data!.Value.Deserialize<T>(JsonOptions); }
            catch (Exception error)
            {
                return ReadResult<T>(ItemDataStatus.Invalid, json: json, version: envelope.Version,
                    reason: "EZAPI_ITEM_DATA_TYPE: " + error.GetType().Name + "; stored JSON was preserved.");
            }
            // User-defined constructors/setters can run during deserialization.
            if (!Stable(run, identity, statePointer, dictionaryPointer, item) || !MatchesTag(item, tagKey, json))
                return ReadResult<T>(ItemDataStatus.Conflict, reason: "EZAPI_ITEM_DATA_STALE: item or data changed during deserialization.");
            return value is null
                ? ReadResult<T>(ItemDataStatus.Invalid, json: json, version: envelope.Version,
                    reason: "EZAPI_ITEM_DATA_TYPE: data is null or cannot be read as the requested type.")
                : ReadResult<T>(ItemDataStatus.Present, value, json, envelope.Version) with { Revision = new(identity, tagKey, ownerPrefix, json) };
        }
        catch (JsonException error)
        {
            return ReadResult<T>(ItemDataStatus.Invalid, reason: "EZAPI_ITEM_DATA_JSON: " + error.Message);
        }
        catch (Exception error)
        {
            return ReadResult<T>(ItemDataStatus.Unavailable, reason: "EZAPI_ITEM_DATA_UNAVAILABLE: " + error.GetType().Name);
        }
    }

    /// <summary>
    /// 用 Read 提供的令牌比较暂存新版本。令牌绑定物品、载入和标签旧值；
    /// 显式迁移可使用 VersionMismatch 返回的令牌。失败或不确定结果禁止盲目重试。
    /// </summary>
    public static ItemDataWriteResult Stage<T>(GameItem item, ItemDataRevision revision, T value, int version = 1)
    {
        if (!GameSession.OnThread) return WriteResult(ItemDataStatus.Disabled, reason: "EZAPI_ITEM_DATA_THREAD: use the initialized game main thread.");
        if (revision is null || version < 1 || value is null)
            return WriteResult(ItemDataStatus.Invalid, reason: "EZAPI_ITEM_DATA_INPUT: a Read revision, nonnull data and version >= 1 are required.");
        var identity = revision.Identity;
        if (!Writing.Add(identity.Pointer)) return WriteResult(ItemDataStatus.Conflict, reason: "EZAPI_ITEM_DATA_BUSY: recursive writes to this item are refused.");
        var attempted = false;
        string? nextJson = null;
        try
        {
            var run = NativeAccess.RequireRun();
            if (!GameBuildFingerprint.IsKnown || !InventoryFreezeApi.IsAvailable)
                return WriteResult(ItemDataStatus.Unavailable, reason: "EZAPI_ITEM_DATA_BUILD: known game build and inventory freeze gate required.");
            if (!Stable(run, identity, identity.State, identity.Dictionary, item))
                return WriteResult(ItemDataStatus.Conflict, reason: "EZAPI_ITEM_DATA_STALE: read revision is for a different item, context or load.");
            if (InventoryFreezeApi.IsFrozen(item)) return WriteResult(ItemDataStatus.Frozen, reason: "EZAPI_ITEM_DATA_FROZEN: wait for the owning transaction to finish.");
            if (InventoryApi.IsItemDragActive) return WriteResult(ItemDataStatus.Conflict, reason: "EZAPI_ITEM_DATA_BUSY: wait for drag completion.");

            try { nextJson = JsonSerializer.Serialize(new Envelope<T>(version, value), JsonOptions); }
            catch (Exception error)
            {
                return WriteResult(ItemDataStatus.Invalid, reason: "EZAPI_ITEM_DATA_JSON: " + error.GetType().Name + "; no write attempted.");
            }
            if (!ValidJsonSize(nextJson)) return WriteResult(ItemDataStatus.Invalid, reason: "EZAPI_ITEM_DATA_LIMIT: JSON envelope max 16 KiB UTF-8 / depth 16.");
            // Serialization may call arbitrary author getters. Recheck all observed inputs.
            if (!Stable(run, identity, identity.State, identity.Dictionary, item) || !InventoryFreezeApi.IsAvailable ||
                InventoryFreezeApi.IsFrozen(item) || InventoryApi.IsItemDragActive)
                return WriteResult(ItemDataStatus.Conflict, reason: "EZAPI_ITEM_DATA_PREFLIGHT: context changed during serialization.");
            var dictionary = item.state!.dict;
            if (!MatchesTag(item, revision.TagKey, revision.Json))
                return WriteResult(ItemDataStatus.Conflict, reason: "EZAPI_ITEM_DATA_REVISION: value changed since Read.");
            if (!WithinQuota(dictionary, revision.OwnerPrefix, revision.TagKey, nextJson))
                return WriteResult(ItemDataStatus.Invalid, reason: "EZAPI_ITEM_DATA_LIMIT: per item/owner max 128 keys / 64 KiB; max 4096 total tags inspected.");
            if (!Stable(run, identity, identity.State, identity.Dictionary, item) || !MatchesTag(item, revision.TagKey, revision.Json))
                return WriteResult(ItemDataStatus.Conflict, reason: "EZAPI_ITEM_DATA_PREFLIGHT: item, run or tag changed before write.");

            // Official per-item tag entry point. Contain managed callback errors rather
            // than throwing them across IL2CPP. The native call may create a missing tag
            // before invoking us, so every failure after entering it is indeterminate.
            var calls = 0;
            Exception? callbackError = null;
            var callback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<TagState>>((Action<TagState>)(tag => {
                try
                {
                    if (++calls != 1 || tag is null || tag.Pointer == IntPtr.Zero ||
                        !Stable(run, identity, identity.State, identity.Dictionary, item) || !InventoryFreezeApi.IsAvailable ||
                        InventoryFreezeApi.IsFrozen(item) || InventoryApi.IsItemDragActive ||
                        (revision.Json is null
                            ? tag.IsEnabled() || !string.IsNullOrEmpty(tag.GetString())
                            : !MatchesTag(item, revision.TagKey, revision.Json)))
                        throw new InvalidOperationException("tag callback context changed");
                    tag.Enable().SetString(nextJson);
                }
                catch (Exception error) { callbackError = error; }
            }));
            if (callback is null) return WriteResult(ItemDataStatus.Unavailable, reason: "EZAPI_ITEM_DATA_TAG: native delegate conversion failed.");
            if (!Stable(run, identity, identity.State, identity.Dictionary, item) ||
                !MatchesTag(item, revision.TagKey, revision.Json) || !InventoryFreezeApi.IsAvailable ||
                InventoryFreezeApi.IsFrozen(item) || InventoryApi.IsItemDragActive)
                return WriteResult(ItemDataStatus.Conflict, reason: "EZAPI_ITEM_DATA_PREFLIGHT: context changed before native tag write.");
            try
            {
                attempted = true;
                item.ModifyTag(revision.TagKey, callback, true);
                if (callbackError is not null || calls != 1)
                    return WriteResult(ItemDataStatus.Indeterminate, nextJson, "EZAPI_ITEM_DATA_UNCONFIRMED: native callback failed or ran an unexpected number of times.");
                if (!Stable(run, identity, identity.State, identity.Dictionary, item))
                    return WriteResult(ItemDataStatus.Indeterminate, nextJson, "EZAPI_ITEM_DATA_UNCONFIRMED: item context changed during native write.");
                item.SyncModifiedState();
            }
            finally { GC.KeepAlive(callback); }

            if (!Stable(run, identity, identity.State, identity.Dictionary, item) || !MatchesTag(item, revision.TagKey, nextJson))
                return WriteResult(ItemDataStatus.Indeterminate, json: nextJson,
                    reason: "EZAPI_ITEM_DATA_UNCONFIRMED: write attempted; reconcile before retrying.");
            return WriteResult(ItemDataStatus.StagedInMemory, nextJson);
        }
        catch (Exception error)
        {
            return attempted
                ? WriteResult(ItemDataStatus.Indeterminate, json: nextJson, reason: "EZAPI_ITEM_DATA_UNCONFIRMED: " + error.GetType().Name)
                : WriteResult(ItemDataStatus.Unavailable, reason: "EZAPI_ITEM_DATA_UNAVAILABLE: " + error.GetType().Name);
        }
        finally { Writing.Remove(identity.Pointer); }
    }

    private static bool WithinQuota(Il2CppSystem.Collections.Generic.Dictionary<string, TagState> dictionary, string prefix, string key, string next)
    {
        if (dictionary.Count > 4096 || dictionary.Count == 4096 && !dictionary.ContainsKey(key)) return false;
        var count = 1;
        long bytes = Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(next);
        foreach (var entry in dictionary) {
            if (entry.Key == key || !entry.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (entry.Value is null || entry.Value.Pointer == IntPtr.Zero) return false;
            count++;
            bytes += Encoding.UTF8.GetByteCount(entry.Key) + Encoding.UTF8.GetByteCount(entry.Value.GetString() ?? "");
            if (count > MaxOwnerKeys || bytes > MaxOwnerUtf8Bytes) return false;
        }
        return count <= MaxOwnerKeys && bytes <= MaxOwnerUtf8Bytes;
    }

    private sealed record Envelope<T>([property: JsonPropertyName("version")] int Version, [property: JsonPropertyName("data")] T Data);
    private sealed record ParsedEnvelope(ItemDataStatus Status, JsonElement? Data = null, int? Version = null, string? Reason = null);

    private static ParsedEnvelope ParseEnvelope(string? json)
    {
        if (!ValidJsonSize(json)) return new(ItemDataStatus.Invalid, Reason: "EZAPI_ITEM_DATA_JSON: envelope missing, malformed, oversized or too deep.");
        try
        {
            using var document = JsonDocument.Parse(json!, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new(ItemDataStatus.Invalid, Reason: "EZAPI_ITEM_DATA_JSON: envelope object required.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Name is not ("version" or "data") || !seen.Add(property.Name))
                    return new(ItemDataStatus.Invalid, Reason: "EZAPI_ITEM_DATA_JSON: only unique version and data fields are supported.");
            if (!document.RootElement.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var versionValue) || versionValue < 1 ||
                !document.RootElement.TryGetProperty("data", out var data) || data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return new(ItemDataStatus.Invalid, Reason: "EZAPI_ITEM_DATA_JSON: positive Version and nonnull Data required.");
            return new(ItemDataStatus.Present, data.Clone(), versionValue);
        }
        catch (JsonException error) { return new(ItemDataStatus.Invalid, Reason: "EZAPI_ITEM_DATA_JSON: " + error.Message); }
    }

    internal sealed record Identity(IntPtr Pointer, int InstanceId, string ItemId, int Units, IntPtr Parent,
        IntPtr State, IntPtr Dictionary, GameRunIdentity Run);

    private static Identity? Capture(GameItem? item)
    {
        if (item is null || item.Pointer == IntPtr.Zero || item.unitCount <= 0 || string.IsNullOrWhiteSpace(item.identifier)) return null;
        var state = item.state;
        var dictionary = state?.dict;
        if (state is null || state.Pointer == IntPtr.Zero || dictionary is null || dictionary.Pointer == IntPtr.Zero) return null;
        return new(item.Pointer, item.uniqueId, item.identifier, item.unitCount, item.parentInventory?.Pointer ?? IntPtr.Zero,
            state.Pointer, dictionary.Pointer, GameSession.Identity!);
    }

    private static bool Stable(GameRunIdentity run, Identity identity, IntPtr state, IntPtr dictionary, GameItem item)
        => item is not null && GameSession.TryResolve(run.Epoch, out _) && GameSession.Identity == run && identity.Run == run &&
            item.Pointer == identity.Pointer && item.uniqueId == identity.InstanceId && item.identifier == identity.ItemId &&
            item.unitCount == identity.Units && (item.parentInventory?.Pointer ?? IntPtr.Zero) == identity.Parent &&
            item.state?.Pointer == state && item.state?.dict?.Pointer == dictionary;

    private static bool Stable<T>(GameRunIdentity run, Identity? identity, IntPtr state, IntPtr dictionary, GameItem item)
        => identity is not null && Stable(run, identity, state, dictionary, item);

    private static bool MatchesTag(GameItem item, string key, string? expected)
    {
        var tag = item.GetTagReadonly(key);
        if (expected is null) return !item.state!.dict.ContainsKey(key) && IsMissingTag(tag);
        return tag is not null && tag.Pointer != IntPtr.Zero && tag.IsEnabled() &&
            string.Equals(tag.GetString(), expected, StringComparison.Ordinal);
    }

    // This build returns an empty, disabled TagState wrapper for an absent key from
    // GetTagReadonly. It is a read-only sentinel, not a stored tag; disabled tags with
    // a nonempty value remain Invalid and are never silently treated as missing.
    private static bool IsMissingTag(TagState? tag)
        => tag is null || tag.Pointer != IntPtr.Zero && !tag.IsEnabled() && string.IsNullOrEmpty(tag.GetString());

    private static bool ValidAddress(string owner, string key)
    {
        try { RunData.ValidateModId(owner); }
        catch (ArgumentException) { return false; }
        return key is not null && key.Length is >= 1 and <= 120 && Regex.IsMatch(key, @"^[A-Za-z0-9_.-]+\z");
    }

    private static string MakeTagKey(string owner, string key) => TagPrefix + owner + "|" + key;
    private static bool ValidJsonSize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxJsonUtf8Bytes) return false;
        try { using var _ = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxJsonDepth }); return true; }
        catch (JsonException) { return false; }
    }
    private static ItemDataReadResult<T> ReadResult<T>(ItemDataStatus status, T? value = default, string? json = null, int? version = null, string? reason = null)
        => new(status, value, json, version, reason);
    private static ItemDataWriteResult WriteResult(ItemDataStatus status, string? json = null, string? reason = null)
        => new(status, json, reason);
}

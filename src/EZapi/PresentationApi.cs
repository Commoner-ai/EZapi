using Il2Cpp;
using MelonLoader;
using Effect = Il2Cpp.ModuleEffectHelper.ModuleEffect;

namespace EZapi;

/// <summary>运行时模块 tooltip 附加文字；不会替换游戏原有属性或动作。</summary>
public sealed record ModuleText(string? AdditionalEffect = null, IReadOnlyList<string>? Lines = null);

/// <summary>
/// 物品名称、说明、owner 来源名、模块 tooltip 和自有模块效果文本。
/// 所有注册只接受初始化主线程声明；模块回调只在对应 owner 的已应用模块上执行。
/// </summary>
public static class PresentationApi
{
    private const int MaxOwnerNames = 1024, MaxModuleTexts = 4096, MaxEffectTexts = 4096;
    private const int MaxNameLength = 200, MaxDescriptionLength = 4000, MaxModuleLines = 128, MaxModuleLineLength = 2048;

    private sealed record ModuleEntry(string Owner, Func<GameItem, bool, ModuleText?> Callback);
    private sealed record EffectEntry(string Owner, LocalizedItemText Name, LocalizedItemText Description);

    private sealed class AppliedEffectText
    {
        internal readonly string Owner;
        internal readonly string Id;
        internal readonly Effect Effect;
        internal readonly IntPtr Pointer;
        internal readonly string OriginalName;
        internal readonly string OriginalDescription;
        internal string LastName;
        internal string LastDescription;
        internal bool OwnName = true;
        internal bool OwnDescription = true;

        internal AppliedEffectText(string owner, string id, Effect effect)
        {
            Owner = owner; Id = id; Effect = effect; Pointer = effect.Pointer;
            OriginalName = effect.displayName ?? "";
            OriginalDescription = effect.description ?? "";
            LastName = OriginalName; LastDescription = OriginalDescription;
        }
    }

    private static readonly Dictionary<string, LocalizedItemText> OwnerNames = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ModuleEntry> ModuleTexts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, EffectEntry> EffectTexts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, AppliedEffectText> AppliedEffectTexts = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedErrors = new(StringComparer.Ordinal);
    private static bool _started, _directorySeen;

    /// <summary>为物品 tooltip 来源行注册中英 owner 名称。每个 owner 只能注册一次。</summary>
    public static ContentSubmissionResult RegisterOwnerDisplayName(string ownerId, string chinese, string english)
    {
        var rejection = RegistrationError();
        if (rejection is not null) return new(false, rejection);
        try { RunData.ValidateModId(ownerId); }
        catch (ArgumentException error) { return new(false, error.Message); }
        if (!ValidText(chinese, MaxNameLength, allowEmpty: false) || !ValidText(english, MaxNameLength, allowEmpty: false))
            return new(false, "EZAPI_OWNER_DISPLAY_TEXT: Chinese and English names must be nonblank and at most 200 characters.");
        if (OwnerNames.Count >= MaxOwnerNames) return new(false, "EZAPI_OWNER_DISPLAY_LIMIT: max 1024 owners.");
        if (OwnerNames.ContainsKey(ownerId)) return new(false, "EZAPI_OWNER_DISPLAY_DUPLICATE: " + ownerId);
        OwnerNames.Add(ownerId, new(chinese, english));
        return new(true, null);
    }

    /// <summary>查询已声明的 JSON/DLL 物品名称；缺少翻译时回退另一语言，未声明时返回 null。</summary>
    public static string? GetRegisteredItemName(string itemId, bool english = false)
    {
        GameSession.RequireThread();
        if (string.IsNullOrWhiteSpace(itemId)) return null;
        if (NativeFactoryRegistry.TryGetRegisteredName(itemId, english, out var native)) return native;
        return ItemRegistry.TryGetRegisteredName(itemId, english, out var definition) ? definition : null;
    }

    /// <summary>查询已声明物品的简介；JSON 使用 description/shortDescription/longDescription 顺序回退。</summary>
    public static string? GetRegisteredItemDescription(string itemId, bool english = false)
    {
        GameSession.RequireThread();
        if (string.IsNullOrWhiteSpace(itemId)) return null;
        if (NativeFactoryRegistry.TryGetRegisteredDescription(itemId, english, out var native)) return native;
        return ItemRegistry.TryGetRegisteredDescription(itemId, english, out var definition) ? definition : null;
    }

    /// <summary>登记动态模块文字。回调接收实例和英文选择标志；只追加 AdditionalEffect 与 Lines。</summary>
    public static ContentSubmissionResult RegisterModuleText(string ownerId, string itemId, Func<GameItem, bool, ModuleText?> text)
    {
        var rejection = RegistrationError();
        if (rejection is not null) return new(false, rejection);
        try { RunData.ValidateModId(ownerId); }
        catch (ArgumentException error) { return new(false, error.Message); }
        if (!OwnedId(ownerId, itemId) || text is null)
            return new(false, "EZAPI_MODULE_TEXT_ID: use an owner-prefixed item ID and nonnull callback.");
        if (!ItemRegistry.IsDeclaredModule(ownerId, itemId))
            return new(false, "EZAPI_MODULE_TEXT_OWNER: declare this owner's module with ItemApi/ItemRegistry first.");
        if (ModuleTexts.Count >= MaxModuleTexts) return new(false, "EZAPI_MODULE_TEXT_LIMIT: max 4096 modules.");
        if (ModuleTexts.ContainsKey(itemId)) return new(false, "EZAPI_MODULE_TEXT_DUPLICATE: " + itemId);
        ModuleTexts.Add(itemId, new(ownerId, text));
        return new(true, null);
    }

    /// <summary>为本 owner 通过 EffectApi 注册的效果提供双语名称和说明。</summary>
    public static ContentSubmissionResult RegisterEffectText(string ownerId, string effectId,
        LocalizedItemText name, LocalizedItemText description)
    {
        var rejection = RegistrationError();
        if (rejection is not null) return new(false, rejection);
        try { RunData.ValidateModId(ownerId); }
        catch (ArgumentException error) { return new(false, error.Message); }
        if (!OwnedId(ownerId, effectId) || name is null || description is null ||
            !ValidLocalized(name, MaxNameLength, requireText: true) || !ValidLocalized(description, MaxDescriptionLength, requireText: false))
            return new(false, "EZAPI_EFFECT_TEXT: owned effect ID, nonblank localized name, and bounded description required.");
        if (!EffectApi.IsDeclaredForOwner(ownerId, effectId))
            return new(false, "EZAPI_EFFECT_TEXT_OWNER: register the effect with EffectApi under the same owner first.");
        if (EffectTexts.Count >= MaxEffectTexts) return new(false, "EZAPI_EFFECT_TEXT_LIMIT: max 4096 effects.");
        if (EffectTexts.ContainsKey(effectId)) return new(false, "EZAPI_EFFECT_TEXT_DUPLICATE: " + effectId);
        EffectTexts.Add(effectId, new(ownerId, name, description));
        return new(true, null);
    }

    internal static void Start()
    {
        _started = true;
        _directorySeen = false;
    }

    internal static void DirectoryReady()
    {
        if (!_started || !GameSession.OnThread) return;
        _directorySeen = true;
        RefreshEffectTexts();
    }

    /// <summary>官方 tooltip Early 回调内同步刷新，语言变更后原生效果字段使用当前本地化文本。</summary>
    internal static void RefreshEffectTexts()
    {
        if (!_started || !GameSession.OnThread || EffectTexts.Count == 0) return;
        foreach (var stale in AppliedEffectTexts.Values.Where(state =>
                     !EffectApi.IsAppliedObject(state.Owner, state.Id, state.Effect)).ToArray())
        {
            // 只恢复我们此前持有并修改的原对象；同 ID 的替代效果从未被本 API 取出或写入。
            try { RestoreEffectText(stale); }
            catch (Exception error) { LogOnce(stale.Id, "EZAPI_EFFECT_TEXT_RESTORE", error); }
            AppliedEffectTexts.Remove(stale.Id);
        }

        var english = NativeItemLocalization.IsEnglish;
        foreach (var pair in EffectTexts)
        {
            var entry = pair.Value;
            if (!EffectApi.TryGetApplied(entry.Owner, pair.Key, out var effect)) continue;
            try
            {
                if (!AppliedEffectTexts.TryGetValue(pair.Key, out var state) || state.Pointer != effect.Pointer)
                {
                    state = new AppliedEffectText(entry.Owner, pair.Key, effect);
                    AppliedEffectTexts[pair.Key] = state;
                }
                var name = entry.Name.For(english);
                var description = entry.Description.For(english);
                if (name is not null) WriteEffectName(state, name);
                if (description is not null) WriteEffectDescription(state, description);
            }
            catch (Exception error) { LogOnce(pair.Key, "EZAPI_EFFECT_TEXT_APPLY", error); }
        }
    }

    internal static bool TryGetModuleText(string itemId, GameItem item, out ModuleText text)
    {
        text = null!;
        if (!_started || !GameSession.OnThread || item is null || item.Pointer == IntPtr.Zero || item.identifier != itemId ||
            !ModuleTexts.TryGetValue(itemId, out var entry) || !ItemRegistry.IsOwnedModule(entry.Owner, itemId)) return false;
        try
        {
            var value = entry.Callback(item, NativeItemLocalization.IsEnglish);
            if (value is null) return false;
            var additional = value.AdditionalEffect;
            if (additional is not null && !ValidText(additional, MaxDescriptionLength, allowEmpty: true))
                throw new InvalidOperationException("AdditionalEffect must be <=4000 characters without control characters.");
            var lines = value.Lines?.ToArray() ?? Array.Empty<string>();
            if (lines.Length > MaxModuleLines || lines.Any(line => !ValidText(line, MaxModuleLineLength, allowEmpty: true)))
                throw new InvalidOperationException("Lines must contain at most 128 entries, each <=2048 characters without control characters.");
            text = new ModuleText(additional, Array.AsReadOnly(lines));
            return true;
        }
        catch (Exception error)
        {
            LogOnce(itemId, "EZAPI_MODULE_TEXT_CALLBACK", error);
            return false;
        }
    }

    internal static string? ResolveOwnerDisplayName(string ownerId, string locale)
    {
        if (OwnerNames.TryGetValue(ownerId, out var text)) return text.ForLocale(locale);
        return null;
    }

    internal static string? ResolveOwnerDisplayName(string ownerId, bool english)
        => OwnerNames.TryGetValue(ownerId, out var text) ? text.For(english) : null;

    internal static object Report() => new
    {
        ready = _started,
        directorySeen = _directorySeen,
        ownerDisplayNames = OwnerNames.Count,
        moduleTextRegistrations = ModuleTexts.Count,
        effectTextRegistrations = EffectTexts.Count,
        appliedEffectTexts = AppliedEffectTexts.Count
    };

    internal static void Stop()
    {
        foreach (var state in AppliedEffectTexts.Values.ToArray())
        {
            try { RestoreEffectText(state); }
            catch (Exception error) { LogOnce(state.Id, "EZAPI_EFFECT_TEXT_RESTORE", error); }
        }
        AppliedEffectTexts.Clear();
        OwnerNames.Clear(); ModuleTexts.Clear(); EffectTexts.Clear(); LoggedErrors.Clear();
        _directorySeen = false; _started = false;
    }

    private static string? RegistrationError()
    {
        if (!_started) return "EZAPI_PRESENTATION_NOT_READY: initialize EZapi first.";
        var error = ItemRegistry.DeclarationRegistrationError();
        return error is not null ? error : _directorySeen ? "EZAPI_PRESENTATION_TOO_LATE: register during initialization." : null;
    }

    private static bool OwnedId(string owner, string? id)
        => id is not null && id.Length <= 160 && id.Length > owner.Length + 1 && id.StartsWith(owner + ".", StringComparison.Ordinal) &&
           System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)+\z");

    private static bool ValidLocalized(LocalizedItemText text, int maximum, bool requireText)
    {
        if(text.Key is not null)return false;
        var values = new[] { text.Chinese, text.English };
        if (requireText && (!values.Any(value => value is not null) || values.Any(value => value is not null && string.IsNullOrWhiteSpace(value)))) return false;
        return values.All(value => value is null || ValidText(value, maximum, allowEmpty: !requireText));
    }

    private static bool ValidText(string? value, int maximum, bool allowEmpty)
        => value is not null && value.Length <= maximum && (allowEmpty || !string.IsNullOrWhiteSpace(value)) && !value.Any(char.IsControl);

    private static void WriteEffectName(AppliedEffectText state, string value)
    {
        if (!state.OwnName) return;
        var current = state.Effect.displayName ?? "";
        if (current != state.LastName && current != state.OriginalName) { state.OwnName = false; return; }
        state.Effect.displayName = value; state.LastName = value;
    }

    private static void WriteEffectDescription(AppliedEffectText state, string value)
    {
        if (!state.OwnDescription) return;
        var current = state.Effect.description ?? "";
        if (current != state.LastDescription && current != state.OriginalDescription) { state.OwnDescription = false; return; }
        state.Effect.description = value; state.LastDescription = value;
    }

    private static void RestoreEffectText(AppliedEffectText state)
    {
        if (state.OwnName && (state.Effect.displayName ?? "") == state.LastName) state.Effect.displayName = state.OriginalName;
        if (state.OwnDescription && (state.Effect.description ?? "") == state.LastDescription) state.Effect.description = state.OriginalDescription;
    }

    private static void LogOnce(string id, string code, Exception error)
    {
        if (LoggedErrors.Add(code + ":" + id)) MelonLogger.Warning("[EZapi] " + code + " " + id + ": " + error.GetType().Name + ": " + error.Message);
    }
}

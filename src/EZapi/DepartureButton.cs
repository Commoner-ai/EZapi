namespace EZapi;

/// <summary>一次入口查询/点击的纯托管快照。不可缓存为后续存档写入凭据；Generation 在重新读档后改变。</summary>
public sealed record DepartureContext(long Generation, string RunId, int SlotId,
    int RemainingVisits, bool InTransit, GameLanguageSnapshot Language);

/// <summary>
/// 在原生“离开店铺”地图中添加地点按钮。Label 为可读回退文字；所有查询必须只读。
/// OnClick 由作者实现目的地业务；框架不扣外出次数、不切场景、不保存、不自动调用原版垃圾场。
/// </summary>
public sealed record DepartureButton(string Id, string Label, Action<DepartureContext> OnClick)
{
    /// <summary>较小值先分配空位，同值按 Id 稳定排序；不改其它 Mod 的布局顺序。</summary>
    public int Order { get; init; }
    public Func<DepartureContext, bool>? Visible { get; init; }
    public Func<DepartureContext, bool>? CanEnter { get; init; }
    /// <summary>可调用 context.Language.Resolve 或 CSV catalog 支持任意游戏语言；结果不可空白，最多200字符。</summary>
    public Func<DepartureContext, string>? DisplayName { get; init; }
    /// <summary>默认要求剩余外出次数大于0；这里只控制准入，不扣次数。无需次数的地点可显式关闭。</summary>
    public bool RequireVisit { get; init; } = true;
    /// <summary>兼容 UpperDistrict 动态占用的原版入口下方四行；默认预留，避免首次布局竞态。</summary>
    public bool ReserveLegacySlots { get; init; } = true;
}

/// <summary>当前 UI 观察；Applied 表示已创建，Placed 表示本次找到可见空位。不是目的地业务执行结果。</summary>
public sealed record DepartureButtonView(string OwnerId, string Id, bool Applied, bool Visible,
    bool Placed, bool Interactable, string? Error);

internal sealed class DepartureEntry
{
    internal readonly string Owner;
    internal readonly DepartureButton Definition;
    internal bool Applied, Visible, Placed, Interactable;
    internal string? Error;
    internal DepartureEntry(string owner, DepartureButton definition) => (Owner, Definition) = (owner, definition with { });
    internal DepartureButtonView View() => new(Owner, Definition.Id, Applied, Visible, Placed, Interactable, Error);
    internal void Clear() { Applied = Visible = Placed = Interactable = false; Error = null; }
}

/// <summary>纯托管注册与回调门控；与 Unity 布局分离，便于离线验证隔离、重复注册和重入。</summary>
internal sealed class DepartureCatalog
{
    internal readonly Dictionary<string, DepartureEntry> Entries = new(StringComparer.Ordinal);
    internal bool Busy { get; private set; }

    internal PresentationResult Register(string owner, DepartureButton definition)
    {
        if (Busy) return new(PresentationStatus.Conflict, "EZAPI_DEPARTURE_REENTRY");
        if (definition is null || !PresentationRules.Owned(owner, definition.Id) ||
            !ValidLabel(definition.Label) || definition.OnClick is null)
            return new(PresentationStatus.Invalid, "EZAPI_DEPARTURE_DEFINITION");
        if (Entries.ContainsKey(definition.Id)) return new(PresentationStatus.Conflict, "EZAPI_DEPARTURE_DUPLICATE");
        if (Entries.Count >= 64) return new(PresentationStatus.Conflict, "EZAPI_DEPARTURE_LIMIT");
        Entries.Add(definition.Id, new(owner, definition));
        return new(PresentationStatus.Queued);
    }

    internal PresentationResult Remove(string owner, string id)
    {
        if (Busy) return new(PresentationStatus.Conflict, "EZAPI_DEPARTURE_REENTRY");
        if (!PresentationRules.Owned(owner, id)) return new(PresentationStatus.Invalid, "EZAPI_DEPARTURE_OWNER");
        if (Entries.TryGetValue(id, out var entry) && entry.Owner != owner)
            return new(PresentationStatus.Conflict, "EZAPI_DEPARTURE_OWNER");
        Entries.Remove(id);
        return new(PresentationStatus.Closed);
    }

    internal bool Evaluate(DepartureEntry entry, DepartureContext context, Func<bool> current,
        out string label, out bool visible, out bool enabled)
    {
        label = entry.Definition.Label; visible = enabled = false;
        if (Busy || !Contains(entry) || !current()) return false;
        Busy = true;
        try
        {
            visible = entry.Definition.Visible?.Invoke(context) ?? true;
            if (!current()) return false;
            if (!visible) { entry.Error = null; return true; }
            label = entry.Definition.DisplayName?.Invoke(context) ?? entry.Definition.Label;
            if (!current()) return false;
            if (!ValidLabel(label)) throw new ArgumentException("EZAPI_DEPARTURE_LABEL");
            enabled = !context.InTransit && (!entry.Definition.RequireVisit || context.RemainingVisits > 0);
            if (enabled) enabled = entry.Definition.CanEnter?.Invoke(context) ?? true;
            if (!current()) return false;
            entry.Error = null;
            return true;
        }
        catch (Exception error)
        {
            entry.Error = "EZAPI_DEPARTURE_QUERY: " + error.GetType().Name;
            visible = enabled = false;
            return false;
        }
        finally { Busy = false; }
    }

    internal bool Invoke(DepartureEntry entry, DepartureContext context, Func<bool> current)
    {
        if (!entry.Placed || !entry.Interactable ||
            !Evaluate(entry, context, current, out _, out var visible, out var enabled) || !visible || !enabled || !current()) return false;
        Busy = true;
        try { entry.Definition.OnClick(context); return true; }
        catch (Exception error) { entry.Error = "EZAPI_DEPARTURE_CLICK: " + error.GetType().Name; return false; }
        finally { Busy = false; }
    }

    internal bool Contains(DepartureEntry entry) => Entries.TryGetValue(entry.Definition.Id, out var live) && ReferenceEquals(live, entry);
    internal static bool ValidLabel(string? label) => !string.IsNullOrWhiteSpace(label) &&
        PresentationRules.Text(label, 200) && !label!.Any(char.IsControl);
}

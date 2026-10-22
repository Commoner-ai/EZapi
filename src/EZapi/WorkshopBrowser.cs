using System.Text;
using UnityEngine;

namespace EZapi;

/// <summary>工坊只读窗口。按钮仅分派作者回调，快照与回调随关闭/场景卸载释放。</summary>
internal static class WorkshopBrowser
{
    private static readonly DebugInputShield Shield = new();
    private static readonly List<(string Id, Rect Area, Action Click)> Buttons = new();
    private static readonly Dictionary<string, float> Scroll = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (Rect Area, float Maximum)> ScrollAreas = new(StringComparer.Ordinal);
    private static WorkshopRegistration? _registration;
    private static WorkshopSnapshot? _snapshot;
    private static string _message = "";
    private static string? _lastError;
    private static string? _pressed;
    private static int _page, _tabPage;
    private static bool _graphView;
    private static GUIStyle? _label, _title, _button, _small, _fill;
    private static int _renderedFrames, _visibleEdges;
    private static object? _lastClosedView;
    private static readonly HashSet<string> AttemptedActions = new(StringComparer.Ordinal);

    internal static bool Visible { get; private set; }
    internal static bool IsShowing(WorkshopRegistration registration)
        => Visible && ReferenceEquals(_registration, registration);

    private static Rect Panel
    {
        get
        {
            var width = Mathf.Max(320, Mathf.Min(1120, Screen.width - 32));
            var height = Mathf.Max(320, Mathf.Min(900, Screen.height - 40));
            return new Rect(Mathf.Max(16, (Screen.width - width) / 2), Mathf.Max(20, (Screen.height - height) / 2), width, height);
        }
    }

    internal static bool Open(WorkshopRegistration registration)
    {
        if (RecipeGuideBrowser.Visible || !WorkshopApi.TryGet(registration.Id, registration.IsChain, out var current) ||
            !ReferenceEquals(registration, current)) return false;
        if (!TryCapture(registration, out var snapshot, out var error))
        {
            _lastError = error;
            MelonLoader.MelonLogger.Error("[EZapi] EZAPI_WORKSHOP_SNAPSHOT: " + error);
            return false;
        }
        Close();
        _registration = registration;
        _snapshot = snapshot;
        _page = 0;
        _tabPage = 0;
        _graphView = false;
        _message = "";
        _lastError = null;
        Visible = true;
        return true;
    }

    internal static void Close()
    {
        // 自动化与故障排查只保留标量/字符串，不保留窗口、原生对象或作者回调。
        if (Visible) _lastClosedView = new
        {
            at = DateTimeOffset.Now, selected = _registration?.Id, graphView = _graphView,
            renderedFrames = _renderedFrames, visibleEdges = _visibleEdges,
            detailScroll = Scroll.Select(value => new { id = value.Key, offset = value.Value }).ToArray(),
            attemptedActions = AttemptedActions.Count, lastError = _lastError
        };
        Visible = false;
        _registration = null;
        _snapshot = null;
        _pressed = null;
        _page = 0;
        _tabPage = 0;
        _graphView = false;
        _message = "";
        AttemptedActions.Clear();
        Scroll.Clear();
        ScrollAreas.Clear();
        _renderedFrames = _visibleEdges = 0;
        Buttons.Clear();
        Shield.Stop();
    }

    internal static bool PointerOver()
    {
        if (!Visible || !Application.isFocused) return false;
        var mouse = Input.mousePosition;
        return Panel.Contains(new Vector2(mouse.x, Screen.height - mouse.y));
    }

    internal static void Update()
    {
        if (!Visible) return;
        var panel = Panel;
        Shield.Update(new Rect(0, 0, 0, 0), panel, true);
        if (!Application.isFocused) { _pressed = null; return; }
        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        var mouse = Input.mousePosition;
        var point = new Vector2(mouse.x, Screen.height - mouse.y);
        // 在 Update 中消费一次滚轮，避免 OnGUI 的 Layout/Repaint 重复累计。
        var wheel = Input.mouseScrollDelta.y;
        if (!_graphView && float.IsFinite(wheel) && wheel != 0)
            foreach (var region in ScrollAreas)
                if (region.Value.Area.Contains(point))
                {
                    MoveScroll(region.Key, -wheel * 36, region.Value.Maximum);
                    break;
                }
        if (Input.GetMouseButtonDown(0))
            _pressed = Buttons.FirstOrDefault(button => button.Area.Contains(point)).Id;
        if (Input.GetMouseButtonUp(0))
        {
            var pressed = _pressed;
            _pressed = null;
            var button = Buttons.FirstOrDefault(value => value.Id == pressed && value.Area.Contains(point));
            if (pressed is not null) button.Click?.Invoke();
        }
    }

    internal static void SceneChanged() => Close();

    internal static void Draw()
    {
        Buttons.Clear();
        ScrollAreas.Clear();
        _visibleEdges = 0;
        if (!Visible || _registration is null || _snapshot is null) return;
        _label ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = false };
        _title ??= new GUIStyle(_label) { fontSize = 22, fontStyle = FontStyle.Bold };
        _button ??= new GUIStyle(GUI.skin.button) { fontSize = 15, richText = false, wordWrap = true };
        _small ??= new GUIStyle(_label) { fontSize = 13 };
        if (_fill is null)
        {
            _fill = new GUIStyle(GUI.skin.box);
            _fill.normal.background = Texture2D.whiteTexture;
        }

        var previousDepth = GUI.depth;
        GUI.depth = -1100;
        try
        {
            var panel = Panel;
            var x = panel.x + 20;
            var width = panel.width - 40;
            var color = GUI.color;
            try
            {
                GUI.color = new Color(0.12f, 0.12f, 0.16f, 1f);
                GUI.Box(panel, "", _fill);
            }
            finally { GUI.color = color; }

            var title = _snapshot.English ? _registration.EnglishTitle : _registration.ChineseTitle;
            GUI.Label(new Rect(x, panel.y + 14, width - 130, 34), title, _title);
            Button("close", new Rect(panel.xMax - 112, panel.y + 14, 92, 32), _snapshot.English ? "Close" : "关闭", Close);

            DrawTabs(x, panel.y + 54, width);
            var groupNames = _snapshot.Groups.Select(group => _snapshot.English ? group.EnglishTitle : group.ChineseTitle).ToArray();
            var groupLine = groupNames.Length == 0 ? "" : string.Join("  ·  ", groupNames.Take(6)) +
                (groupNames.Length > 6 ? $"  ·  +{groupNames.Length - 6}" : "");
            GUI.Label(new Rect(x, panel.y + 92, width, 28), groupLine, _small);
            GUI.Label(new Rect(x, panel.y + 118, width - 390, 28),
                $"{(_snapshot.English ? "Credits" : "資金")}: {_snapshot.Credits}    {(_registration.IsChain ? (_snapshot.English ? "Progress chain" : "進度鏈") : (_snapshot.English ? "Workshop" : "工坊"))}    {(_snapshot.English ? "Entries" : "條目")}: {_snapshot.Entries.Count}", _small);
            Button("view", new Rect(panel.xMax - 360, panel.y + 114, 112, 30),
                _graphView ? (_snapshot.English ? "List view" : "列表") : (_snapshot.English ? "Graph view" : "進度圖"),
                () => { _graphView = !_graphView; _page = 0; });
            Button("refresh", new Rect(panel.xMax - 236, panel.y + 114, 112, 30), _snapshot.English ? "Refresh" : "刷新", Refresh);
            var pageSize = _graphView ? GraphPageSize(width, panel.height - 290) : ListPageSize(panel.height);
            var pages = Math.Max(1, (_snapshot.Entries.Count + pageSize - 1) / pageSize);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            if (pages > 1)
            {
                Button("prev-page", new Rect(panel.xMax - 116, panel.y + 114, 42, 30), "‹", () => _page = (_page + pages - 1) % pages);
                Button("next-page", new Rect(panel.xMax - 66, panel.y + 114, 42, 30), "›", () => _page = (_page + 1) % pages);
            }

            if (!string.IsNullOrWhiteSpace(_snapshot.Message))
                GUI.Label(new Rect(x, panel.y + 148, width, 42), _snapshot.Message, _label);
            if (_snapshot.Entries.Count == 0)
                GUI.Label(new Rect(x, panel.y + 202, width, 64), _snapshot.English ? "No workshop entries." : "此工坊暂无条目。", _label);

            var entriesTop = panel.y + 194;
            var entryAreaHeight = Math.Max(96, panel.yMax - entriesTop - 58);
            var cardHeight = (entryAreaHeight - 10 * (pageSize - 1)) / pageSize;
            if (_graphView)
                DrawGraph(x, entriesTop, width, entryAreaHeight, pageSize);
            else
            {
                var visibleEntries = _snapshot.Entries.Skip(_page * pageSize).Take(pageSize).ToArray();
                for (var i = 0; i < visibleEntries.Length; i++)
                    DrawEntry(visibleEntries[i], new Rect(x, entriesTop + i * (cardHeight + 10), width, cardHeight));
            }

            GUI.Label(new Rect(x, panel.yMax - 36, width, 24),
                _lastError is not null ? _lastError : _message, _small);
            _renderedFrames++;
        }
        catch (Exception error)
        {
            _lastError = error.ToString();
            MelonLoader.MelonLogger.Error("[EZapi] EZAPI_WORKSHOP_RENDER: " + error);
            Close();
        }
        finally { GUI.depth = previousDepth; }
    }

    private static void DrawTabs(float x, float y, float width)
    {
        var tabs = WorkshopApi.RegisteredTabs();
        if (tabs.Count == 0) return;
        const int pageSize = 5;
        var pageCount = Math.Max(1, (tabs.Count + pageSize - 1) / pageSize);
        _tabPage = Mathf.Clamp(_tabPage, 0, pageCount - 1);
        var tabWidth = Math.Max(90, Math.Min(190, (width - 12) / Math.Min(pageSize, tabs.Count)));
        var start = _tabPage * pageSize;
        for (var i = 0; i < pageSize && start + i < tabs.Count; i++)
        {
            var tab = tabs[start + i];
            var selected = _registration?.Id == tab.Id && !_registration.IsChain;
            var label = _snapshot?.English == true ? tab.EnglishTitle : tab.ChineseTitle;
            Button("tab:" + tab.Id, new Rect(x + i * (tabWidth + 4), y, tabWidth, 32), selected ? "• " + label : label,
                () => WorkshopApi.OpenTab(tab.Id));
        }
        if (pageCount > 1)
        {
            Button("tab-page-prev", new Rect(x + width - 88, y, 38, 32), "‹", () => _tabPage = (_tabPage + pageCount - 1) % pageCount);
            Button("tab-page-next", new Rect(x + width - 44, y, 38, 32), "›", () => _tabPage = (_tabPage + 1) % pageCount);
        }
    }

    private static void DrawEntry(WorkshopEntry entry, Rect card)
    {
        GUI.Box(card, "");
        GUI.Label(new Rect(card.x + 10, card.y + 6, card.width - 166, 24), entry.Title, _label);
        var actionable = !entry.Unlocked && entry.PrerequisiteMet && entry.ActionEnabled && !AttemptedActions.Contains(entry.Id);
        var actionLabel = string.IsNullOrWhiteSpace(entry.ActionText) ? (_snapshot!.English ? "Open" : "操作") : entry.ActionText;
        Button("action:" + entry.Id, new Rect(card.xMax - 154, card.y + 6, 144, 30), actionLabel,
            () => Unlock(entry.Id), actionable);

        var details = EntryDetails(entry);
        var area = new Rect(card.x + 10, card.y + 42, card.width - 20, Math.Max(30, card.height - 50));
        var textArea = new Rect(area.x, area.y, area.width - 30, area.height);
        var contentHeight = Mathf.Max(textArea.height, _small!.CalcHeight(new GUIContent(details), textArea.width));
        var maximum = Math.Max(0, contentHeight - textArea.height);
        var offset = Mathf.Clamp(Scroll.GetValueOrDefault(entry.Id), 0, maximum);
        Scroll[entry.Id] = offset;
        ScrollAreas[entry.Id] = (textArea, maximum);
        // 当前 IL2CPP 构建的 Begin/EndScrollView 进入 unstripping stub。
        // 保留连续滚动，用已核对的公开裁剪组绘制；finally 保证 GUI 裁剪栈平衡。
        GUI.BeginGroup(textArea);
        try { GUI.Label(new Rect(0, -offset, textArea.width, contentHeight), details, _small); }
        finally { GUI.EndGroup(); }
        if (maximum > 0)
        {
            var right = area.xMax - 24;
            Button("scroll-up:" + entry.Id, new Rect(right, area.y, 24, 24), "▲",
                () => MoveScroll(entry.Id, -Math.Max(36, textArea.height * 0.8f), maximum), offset > 0);
            Button("scroll-down:" + entry.Id, new Rect(right, area.yMax - 24, 24, 24), "▼",
                () => MoveScroll(entry.Id, Math.Max(36, textArea.height * 0.8f), maximum), offset < maximum);
            var track = new Rect(right + 9, area.y + 28, 6, Math.Max(1, area.height - 56));
            var thumbHeight = Math.Max(6, track.height * textArea.height / contentHeight);
            var thumb = new Rect(track.x, track.y + (track.height - thumbHeight) * offset / maximum, track.width, thumbHeight);
            var color = GUI.color;
            try { GUI.color = new Color(0.64f, 0.7f, 0.8f, 1); GUI.Box(thumb, "", _fill); }
            finally { GUI.color = color; }
        }
    }

    private static void MoveScroll(string id, float delta, float maximum)
        => Scroll[id] = Mathf.Clamp(Scroll.GetValueOrDefault(id) + delta, 0, maximum);

    private static int ListPageSize(float panelHeight) => Mathf.Clamp((int)((panelHeight - 290) / 236), 1, 3);

    private static int GraphPageSize(float width, float height)
    {
        var columns = Mathf.Clamp((int)(width / 178), 2, 6);
        var rows = Mathf.Clamp((int)(height / 112), 1, 6);
        return columns * rows;
    }

    private static void DrawGraph(float x, float y, float width, float height, int pageSize)
    {
        if (_snapshot is null) return;
        var start = _page * pageSize;
        var indexed = _snapshot.Entries.Select((entry, index) => (entry, index))
            .Skip(start).Take(pageSize).ToArray();
        if (indexed.Length == 0) return;

        var columns = Math.Min(Mathf.Clamp((int)(width / 178), 2, 6), indexed.Length);
        var rows = Math.Max(1, (indexed.Length + columns - 1) / columns);
        var gapX = 12f;
        var gapY = 12f;
        var nodeWidth = Mathf.Min(164, (width - gapX * (columns - 1)) / columns);
        var nodeHeight = Mathf.Clamp((height - gapY * (rows - 1)) / rows, 62, 100);
        var coords = indexed.Where(item => item.entry.GraphX.HasValue && item.entry.GraphY.HasValue).ToArray();
        var minX = coords.Length == 0 ? 0 : coords.Min(item => item.entry.GraphX!.Value);
        var maxX = coords.Length == 0 ? 0 : coords.Max(item => item.entry.GraphX!.Value);
        var minY = coords.Length == 0 ? 0 : coords.Min(item => item.entry.GraphY!.Value);
        var maxY = coords.Length == 0 ? 0 : coords.Max(item => item.entry.GraphY!.Value);
        var canScale = coords.Length > 1 && (maxX > minX || maxY > minY);
        var nodeRects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var centers = new Dictionary<string, Vector2>(StringComparer.Ordinal);

        for (var i = 0; i < indexed.Length; i++)
        {
            var column = i % columns;
            var row = i / columns;
            float px, py;
            var entry = indexed[i].entry;
            if (canScale && entry.GraphX.HasValue && entry.GraphY.HasValue)
            {
                var nx = maxX == minX ? 0.5f : (entry.GraphX.Value - minX) / (maxX - minX);
                var ny = maxY == minY ? 0.5f : (entry.GraphY.Value - minY) / (maxY - minY);
                px = x + nodeWidth / 2 + nx * Math.Max(0, width - nodeWidth);
                py = y + nodeHeight / 2 + ny * Math.Max(0, height - nodeHeight);
            }
            else
            {
                px = x + column * (nodeWidth + gapX) + nodeWidth / 2;
                py = y + row * (nodeHeight + gapY) + nodeHeight / 2;
            }
            var rect = new Rect(Mathf.Clamp(px - nodeWidth / 2, x, x + width - nodeWidth),
                Mathf.Clamp(py - nodeHeight / 2, y, y + height - nodeHeight), nodeWidth, nodeHeight);
            nodeRects[entry.Id] = rect;
            centers[entry.Id] = rect.center;
        }

        var edges = new HashSet<(string From, string To)>();
        foreach (var item in indexed)
        {
            foreach (var target in item.entry.VisualLinks.Concat(item.entry.Dependencies))
                if (target != item.entry.Id && centers.ContainsKey(target)) edges.Add((item.entry.Id, target));
        }
        foreach (var edge in edges.Take(512)) DrawLine(centers[edge.From], centers[edge.To]);
        _visibleEdges = Math.Min(edges.Count, 512);
        foreach (var item in indexed)
        {
            var entry = item.entry;
            var state = entry.Unlocked ? (_snapshot.English ? "Unlocked" : "已解锁") :
                !entry.PrerequisiteMet ? (_snapshot.English ? "Prerequisite missing" : "前置未满足") :
                entry.Completed == true ? (_snapshot.English ? "Completed" : "已完成") :
                (_snapshot.English ? "Locked" : "未解锁");
            var caption = entry.Title + "\n" + state;
            Button("graph:" + entry.Id, nodeRects[entry.Id], caption,
                () => ShowEntry(item.index));
        }
        if (edges.Count > 512)
            GUI.Label(new Rect(x, y + height - 22, width, 20), _snapshot.English ? "Only the first 512 visible links are drawn." : "图中仅绘制前 512 条可见连接。", _small);
    }

    private static void DrawLine(Vector2 from, Vector2 to)
    {
        var delta = to - from;
        var length = delta.magnitude;
        if (length < 1) return;
        var midpoint = (from + to) / 2;
        var matrix = GUI.matrix;
        var color = GUI.color;
        try
        {
            GUI.color = new Color(0.72f, 0.76f, 0.83f, 0.75f);
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, midpoint);
            GUI.Box(new Rect(midpoint.x - length / 2, midpoint.y - 1, length, 2), "", _fill);
        }
        finally { GUI.matrix = matrix; GUI.color = color; }
    }

    private static void ShowEntry(int index)
    {
        if (_snapshot is null) return;
        _graphView = false;
        _page = index / ListPageSize(Panel.height);
    }

    private static string EntryDetails(WorkshopEntry entry)
    {
        var english = _snapshot!.English;
        var lines = new List<string>();
        Add(entry.Subtitle);
        Add(entry.Description);
        AddLabel(english ? "Requirement" : "要求", entry.Requirement);
        AddLabel(english ? "Cost" : "花费", entry.CostText);
        AddLabel(english ? "Node cost" : "节点花费", entry.NodeCostText);
        AddLabel(english ? "State" : "状态", entry.StateText);
        AddLabel(english ? "Dependencies" : "前置", string.Join(", ", entry.Dependencies));
        AddLabel(english ? "Conditions" : "条件", string.Join("; ", entry.Conditions.Select(value =>
            $"{(value.Satisfied ? "✓" : "×")} {Resource(value.Kind, value.ResourceId, value.DisplayName)} × {value.Quantity}" +
            (value.Consume ? (english ? " (consume)" : "（消耗）") : ""))));
        AddLabel(english ? "Rewards" : "奖励", string.Join("; ", entry.Rewards.Select(value =>
            $"{Resource(value.Kind, value.ResourceId, value.DisplayName)} × {value.Quantity}")));
        if (entry.Completed.HasValue) AddLabel(english ? "Completed" : "完成", entry.Completed.Value ? "✓" : "×");
        AddLabel(english ? "Visual links" : "图示连接", string.Join(", ", entry.VisualLinks));
        if (entry.GraphX.HasValue || entry.GraphY.HasValue)
            AddLabel(english ? "Graph" : "图谱", $"{entry.GraphX?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "—"}, {entry.GraphY?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "—"}");
        return lines.Count == 0 ? (english ? "No details." : "暂无说明。") : string.Join("\n", lines);

        void Add(string text) { if (!string.IsNullOrWhiteSpace(text)) lines.Add(text); }
        void AddLabel(string label, string value) { if (!string.IsNullOrWhiteSpace(value)) lines.Add(label + ": " + value); }
    }

    private static string Resource(WorkshopResourceKind kind, string id, string displayName)
        => !string.IsNullOrWhiteSpace(displayName) ? displayName : kind == WorkshopResourceKind.Credits
            ? (_snapshot!.English ? "Credits" : "资金")
            : id;

    private static void Button(string id, Rect area, string text, Action click, bool enabled = true)
    {
        GUI.Box(area, text, _button);
        if (enabled) Buttons.Add((id, area, click));
    }

    private static void Refresh()
    {
        if (_registration is null) return;
        var registration = _registration;
        if (!TryCapture(registration, out var snapshot, out var error))
        {
            _lastError = error;
            _message = _snapshot?.English == true ? "Refresh failed; previous view retained." : "刷新失败；保留旧快照。";
            MelonLoader.MelonLogger.Error("[EZapi] EZAPI_WORKSHOP_REFRESH: " + error);
            return;
        }
        if (!ReferenceEquals(_registration, registration)) return;
        _snapshot = snapshot;
        var pageSize = Mathf.Clamp((int)((Panel.height - 290) / 236), 1, 3);
        _page = Math.Min(_page, Math.Max(0, (snapshot.Entries.Count - 1) / pageSize));
        _message = snapshot.English ? "Snapshot refreshed." : "快照已刷新。";
        _lastError = null;
    }

    private static void Unlock(string entryId)
    {
        if (_registration is null || _snapshot is null) return;
        var registration = _registration;
        var result = WorkshopApi.RunAction(registration, entryId, AttemptedActions);
        // 作者回调可能主动切换页签或关闭窗口；不能用旧动作覆盖新窗口状态。
        if (!ReferenceEquals(_registration, registration)) return;
        if (result.Snapshot is not null) _snapshot = result.Snapshot;
        _lastError = result.Error;
        _message = result.Invoked
            ? (_snapshot.English ? "Mod callback finished; the view was refreshed." : "Mod 回调已结束，快照已刷新。")
            : (_snapshot.English ? "Action was not called; inspect its current state." : "未调用动作回调；请检查当前状态。");
        if (result.Error is not null) MelonLoader.MelonLogger.Error("[EZapi] " + result.Error);
    }

    private static bool TryCapture(WorkshopRegistration registration, out WorkshopSnapshot snapshot, out string error)
    {
        try
        {
            snapshot = WorkshopApi.Capture(registration);
            error = "";
            return true;
        }
        catch (Exception exception)
        {
            snapshot = null!;
            error = exception.ToString();
            return false;
        }
    }

    internal static object Report() => new
    {
        visible = Visible,
        selected = _registration is null ? null : new { _registration.OwnerId, _registration.Id, _registration.IsChain },
        entryCount = _snapshot?.Entries.Count ?? 0,
        page = _page,
        graphView = _graphView,
        renderedFrames = _renderedFrames,
        visibleEdges = _visibleEdges,
        detailScroll = Scroll.Select(value => new { id = value.Key, offset = value.Value }).ToArray(),
        lastClosedView = _lastClosedView,
        attemptedActions = AttemptedActions.Count,
        lastError = _lastError,
        english = _snapshot?.English,
        resolvedMessage = _snapshot?.Message,
        renderedText = _snapshot is null ? null : string.Join("\n", _snapshot.Entries.Select(entry => entry.Title + "\n" + EntryDetails(entry))),
        scope = "independent read-only workshop UI; author callback owns qualification, payment, reward delivery and persistence; no automatic retry"
    };

    // Checks the actual captured view after Draw has rendered it; no alternate translation path.
    internal static bool VerifyLocaleProbe(bool english) => Visible && _snapshot is { } view &&
        view.English == english && _renderedFrames > 0 && _lastError is null &&
        view.Entries.Any(entry => entry.Title == (english ? "Workshop demo node" : "工坊演示节点") &&
            EntryDetails(entry).Contains(english ? "Demo condition" : "演示条件", StringComparison.Ordinal) &&
            EntryDetails(entry).Contains(english ? "Demo reward (not delivered)" : "演示奖励（不发放）", StringComparison.Ordinal));
}

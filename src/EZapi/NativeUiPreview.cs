using Il2Cpp;
using Il2CppTMPro;
using MelonLoader.Utils;
using UnityEngine;

namespace EZapi;

/// <summary>Actual native text geometry and font measurements; coordinates are logical pixels relative to the window.</summary>
public sealed record UiTextMeasurement(string Name, string Text, string? Font, float FontSize, string Alignment,
    bool Wrap, float RequiredWidth, float RequiredHeight, UiMeasuredBox Box, bool Overflows);
public sealed record UiMeasuredBox(float X, float Y, float Width, float Height);
public sealed record UiPreviewControl(string Id, UiKind Kind, UiBox Expected, UiMeasuredBox Actual);
/// <summary>Scoped measurements, not a guarantee of game design quality, accessibility or functional correctness.</summary>
public sealed record NativeUiInspection(string Locale, int WindowWidth, int WindowHeight,
    IReadOnlyList<UiTextMeasurement> Text, IReadOnlyList<string> Issues);
/// <summary>Preview report: expected layout versus actual native graph, with read-only text inspection.</summary>
public sealed record NativeUiPreviewReport(bool Visible, string Locale, IReadOnlyList<UiPreviewControl> Controls,
    NativeUiInspection Inspection, IReadOnlyList<string> Issues);

/// <summary>
/// One explicit native UI preview, gated by ordinary developer tools. Uses native window/labels/buttons/inventory,
/// never submits callbacks, delivers items or saves. Closing, loading or leaving the scene releases its own objects.
/// 单个可重载原生预览；仅开发模式启用，不替换其他Mod UI，不执行交易/加工/存档。
/// </summary>
public static class NativeUiPreviewApi
{
    private sealed record Control(UiLayoutNode Plan, PixelElement Element, TreeNodeRender? Text);
    private static readonly List<Control> Controls = new();
    private static PixelWindow? _window;
    private static GameObject? _object;
    private static PixelElement? _content;
    private static GameRunIdentity? _run;
    private static NativeUiDocument? _document;
    private static string _locale = "";
    private static double _next;
    private static NativeUiPreviewReport? _last;
    private static string? _error;
    public static bool IsVisible => _window is not null && _object != null;

    /// <summary>Validate fully, then replace this preview only; a loaded store and no active drag are required.</summary>
    public static NativeUiPreviewReport Open(NativeUiDocument document)
    {
        GameSession.RequireThread();
        if (!DeveloperToolsSettings.Current.Enabled) throw new InvalidOperationException("EZAPI_UI_PREVIEW_DISABLED: enable ordinary developer tools.");
        var run = NativeAccess.RequireRun();
        if (!GameBuildFingerprint.IsKnown || !VisibleStoreInventory.IsReady || InventoryApi.IsItemDragActive)
            throw new InvalidOperationException("EZAPI_UI_PREVIEW_STATE: supported loaded store without active drag required.");
        var plan = NativeUiLayout.Measure(document); // Validate before releasing the previous preview.
        Close(); _run = run; _document = document with { Root = plan.Definition }; _locale = NativeItemLocalization.CurrentLocaleCode;
        try {
            _window = new PixelWindow(true, document.Title.ForLocale(_locale) ?? "UI preview");
            _object = _window.handler.gameObject;
            _content = Build(plan);
            if (!_window.Attach(_content)) throw new InvalidOperationException("EZAPI_UI_NATIVE: preview root attach refused.");
            _window.Validate(); ApplyStyle(); NativeWindowTitles.Set(_window, document.Title.ForLocale(_locale) ?? "UI preview");
            _window.Show().Center(); ApplyStyle();
            var titleRect = _window.titleText.rectTransform;
            titleRect.sizeDelta = new Vector2(Math.Max(1, _window.widthPixels - 3 * _window.xPadding - _window.topCloseButton.rectTransform.sizeDelta.x), titleRect.sizeDelta.y);
            _last = Measure(); _error = null; return _last;
        } catch (Exception error) { _error = error.GetType().Name + ": " + error.Message; Close(); throw; }
    }
    /// <summary>Read bounded JSON, then open its preview. The input stream remains owned by the caller.</summary>
    public static NativeUiPreviewReport Open(Stream json) => Open(NativeUiLayout.Parse(json));
    /// <summary>Export current native geometry without saving or modifying inventory contents.</summary>
    public static NativeUiPreviewReport Measure()
    {
        GameSession.RequireThread();
        if (_window is null || _content is null || _run is null || !NativeAccess.SameRun(_run) || _object == null)
            throw new InvalidOperationException("EZAPI_UI_PREVIEW_STATE: no current preview.");
        Canvas.ForceUpdateCanvases();
        var rows = new List<UiPreviewControl>(); var issues = new List<string>();
        foreach (var c in Controls) {
            var actual = Relative(c.Element.handler.rectTransform, _content.handler.rectTransform, c.Element.widthPixels, c.Element.heightPixels);
            rows.Add(new(c.Plan.Id, c.Plan.Kind, c.Plan.Box, actual));
            var p = c.Plan.Box;
            if (Math.Abs(actual.X - p.X) > 1.1f || Math.Abs(actual.Y - p.Y) > 1.1f || Math.Abs(actual.Width - p.Width) > 1.1f || Math.Abs(actual.Height - p.Height) > 1.1f)
                issues.Add(c.Plan.Id + ": native geometry differs from expected logical box");
        }
        var inspection = InspectWindow(_window);
        issues.AddRange(inspection.Issues);
        return _last = new(_window.IsVisible(), _locale, rows.AsReadOnly(), inspection, issues.AsReadOnly());
    }
    /// <summary>
    /// Read-only audit of an existing native PixelWindow, including other Mods' windows. Does not restyle or clone it.
    /// Fits and overflow checks use the current actual TMP font, not character counts. At most 256 labels.
    /// </summary>
    public static NativeUiInspection InspectWindow(PixelWindow window)
    {
        NativeAccess.RequireRun();
        if (window is null || window.Pointer == IntPtr.Zero || window.handler == null)
            throw new ArgumentException("EZAPI_UI_NATIVE: current native window required.");
        var root = window.rectTransform; var rows = new List<UiTextMeasurement>(); var issues = new List<string>();
        var labels = window.handler.GetComponentsInChildren<TextMeshProUGUI>(true);
        if (labels.Length > 256) issues.Add("window: more than 256 text nodes; report bounded to 256");
        for (var i = 0; i < Math.Min(labels.Length, 256); i++) {
            var label = labels[i];
            if (label == null || !label.gameObject.activeInHierarchy || string.IsNullOrEmpty(label.text)) continue;
            var b = Relative(label.rectTransform, root); var margin = label.margin;
            var availableWidth = Math.Max(0, label.rectTransform.rect.width - margin.x - margin.z);
            var availableHeight = Math.Max(0, label.rectTransform.rect.height - margin.y - margin.w);
            var needed = label.GetPreferredValues(label.text, label.enableWordWrapping ? availableWidth : float.PositiveInfinity, float.PositiveInfinity);
            var over = !float.IsFinite(needed.x) || !float.IsFinite(needed.y) || needed.x > availableWidth + 1 || needed.y > availableHeight + 1 || label.isTextOverflowing;
            var name = label.gameObject.name;
            rows.Add(new(name, label.text, label.font?.name, label.fontSize, label.alignment.ToString(), label.enableWordWrapping, needed.x, needed.y, b, over));
            if (over) issues.Add(name + ": text exceeds available box");
            if (b.X < -1 || b.Y < -1 || b.X + b.Width > window.widthPixels + 1 || b.Y + b.Height > window.heightPixels + 1)
                issues.Add(name + ": text rect leaves window bounds");
        }
        return new(NativeItemLocalization.CurrentLocaleCode, window.widthPixels, window.heightPixels, rows.AsReadOnly(), issues.AsReadOnly());
    }
    /// <summary>Close and release this tool's objects only. Does not access any player inventory or save.</summary>
    public static void Close()
    {
        GameSession.RequireThread();
        try {
            if (_object != null) {
                if (_window is not null && RenderHandler.current != null && _run is not null && NativeAccess.SameRun(_run)) _window.Hide();
                UnityEngine.Object.Destroy(_object);
            }
        } finally { _window = null; _object = null; _content = null; _document = null; _run = null; Controls.Clear(); }
    }
    private static PixelElement Build(UiLayoutNode plan)
    {
        var d = plan.Definition; var children = plan.Children.Select(Build).ToArray(); PixelElement element; TreeNodeRender? text = null;
        if (d.Kind is UiKind.Row or UiKind.Column) {
            element = d.Kind == UiKind.Row ? NativeUiComponents.Row(d.Gap, d.Vertical, children) : NativeUiComponents.Column(d.Gap, d.Horizontal, children);
            element = NativeUiComponents.Inset(element, d.Padding);
            element = NativeUiComponents.Frame(element, plan.Box.Width, plan.Box.Height, d.Horizontal, d.Vertical);
        } else if (d.Kind == UiKind.Frame) {
            element = NativeUiComponents.Inset(children[0], d.Padding);
            element = NativeUiComponents.Frame(element, plan.Box.Width, plan.Box.Height, d.Horizontal, d.Vertical);
        } else if (d.Kind is UiKind.Inventory or UiKind.Slot) {
            GameInventory inv = d.Kind == UiKind.Slot ? new GameSlotInventory(d.Columns, d.Rows, false, false) : new GameGridInventory(d.Columns, d.Rows);
            inv.identifier = "ezapi.ui_preview." + d.Id; inv.LockInv();
            element = NativeUiComponents.Frame(inv.Cast<PixelElement>(), plan.Box.Width, plan.Box.Height, d.Horizontal, d.Vertical);
        } else if (d.Kind == UiKind.Button) {
            var button = NativeUiComponents.Button(d.Text!.ForLocale(_locale)!, plan.Box.Width, plan.Box.Height, fontSize: d.FontSize);
            button.Disable(); text = button.textNode; element = button.Cast<PixelElement>();
        } else if (d.Kind is UiKind.Label or UiKind.Tag) {
            element = NativeUiComponents.Label(d.Text!.ForLocale(_locale)!, plan.Box.Width, plan.Box.Height, d.Horizontal, d.Vertical, d.FontSize, d.Wrap, d.Kind == UiKind.Tag);
            text = d.Kind == UiKind.Tag ? element.Cast<TagElement>().textNode : element.Cast<RichTextElement>().textNode;
        } else element = NativeUiComponents.Spacer(plan.Box.Width, plan.Box.Height);
        element.handler.gameObject.name = "EZapi.UiPreview." + plan.Id;
        Controls.Add(new(plan, element, text)); return element;
    }
    private static void ApplyStyle()
    {
        foreach (var c in Controls) if (c.Text is { } node) {
            var d = c.Plan.Definition;
            node.gameObject.name = "EZapi.UiPreview.Text." + d.Id;
            NativeUiComponents.Style(node, d.Text!.ForLocale(_locale)!, c.Plan.Box.Width, c.Plan.Box.Height, d.Horizontal, d.Vertical, d.FontSize, d.Wrap);
        }
    }
    private static UiMeasuredBox Relative(RectTransform rect, RectTransform root, int? logicalWidth = null, int? logicalHeight = null)
    {
        // Native render handlers are zero-sized anchors; their element owns logical size.
        var r = logicalWidth is { } w && logicalHeight is { } h ? new Rect(0, -h, w, h) : rect.rect; var points = new[] { new Vector3(r.xMin, r.yMin, 0), new Vector3(r.xMax, r.yMin, 0), new Vector3(r.xMin, r.yMax, 0), new Vector3(r.xMax, r.yMax, 0) }
            .Select(p => root.InverseTransformPoint(rect.TransformPoint(p))).ToArray();
        var x = points.Min(p => p.x); var y = points.Max(p => p.y);
        return new(x - root.rect.xMin, root.rect.yMax - y, points.Max(p => p.x) - x, y - points.Min(p => p.y));
    }
    internal static string ReloadFile()
    {
        var path = Path.Combine(MelonEnvironment.UserDataDirectory, "EZapi", "UiPreview", "layout.json");
        try { using var file = File.Exists(path) ? File.OpenRead(path) : Template("computer"); var report = Open(file); return NativeItemLocalization.IsEnglish ? $"Native preview: {report.Issues.Count} issue(s); export diagnostics." : $"原生预览：{report.Issues.Count} 个待检查项；可导出诊断。"; }
        catch (Exception error) { _error = error.GetType().Name + ": " + error.Message; return "UI preview: " + _error; }
    }
    internal static Stream Template(string name) => typeof(NativeUiPreviewApi).Assembly.GetManifestResourceStream("EZapi.UiPreview." + name + ".json")
        ?? throw new InvalidOperationException("EZAPI_UI_NATIVE: embedded layout template missing.");
    internal static void Maintain()
    {
        if (_window is null) return;
        if (!DeveloperToolsSettings.Current.Enabled || _run is null || !NativeAccess.SameRun(_run) || !VisibleStoreInventory.IsReady || _object == null) { Close(); return; }
        if (Time.realtimeSinceStartupAsDouble < _next) return; _next = Time.realtimeSinceStartupAsDouble + .5;
        try {
            if (!_window.IsVisible()) { Close(); return; }
            var locale = NativeItemLocalization.CurrentLocaleCode;
            if (locale != _locale && _document is { } doc) { Open(doc); return; }
            ApplyStyle(); _last = Measure();
        } catch (Exception error) { _error = error.GetType().Name + ": " + error.Message; Close(); }
    }
    internal static object Report() => new { visible = IsVisible, locale = _locale, error = _error, current = IsVisible ? _last : null, last = _last,
        retainedControls = Controls.Count, scope = "native visual layout only; buttons inert, empty inventories locked; no gameplay/save acceptance" };
}

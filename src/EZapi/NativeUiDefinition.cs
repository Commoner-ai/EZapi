using System.Text.Json;
using System.Text.Json.Serialization;

namespace EZapi;

/// <summary>Logical native pixels, before the game's canvas scaling. 原生逻辑像素，非屏幕像素。</summary>
public sealed record UiBox(int X, int Y, int Width, int Height);
/// <summary>Start = left/top; End = right/bottom. Center uses integer pixels. 起点为左/上，终点为右/下。</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UiAlign { Start, Center, End }
/// <summary>Native component recipes. Row/Column determine flow; Frame supplies padding/alignment.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UiKind { Row, Column, Frame, Label, Tag, Button, Inventory, Slot, Spacer }

/// <summary>
/// Declarative layout node. Every ID is unique within one document. Width/Height are outer logical pixels;
/// zero auto-sizes groups and inventory, but text requires an explicit box. Children never shrink silently.
/// 声明布局；分组可自动尺寸，文字必须有明确框，放不下会报错，不偷偷缩小。
/// </summary>
public sealed record UiNode
{
    public string Id { get; init; } = "";
    public UiKind Kind { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Gap { get; init; } = 4;
    public int Padding { get; init; }
    public UiAlign Horizontal { get; init; } = UiAlign.Start;
    public UiAlign Vertical { get; init; } = UiAlign.Center;
    public bool Reverse { get; init; }
    public LocalizedItemText? Text { get; init; }
    /// <summary>Null retains the native locale font size; 4..24 is an explicit logical font size.</summary>
    public float? FontSize { get; init; }
    public bool Wrap { get; init; }
    public int Columns { get; init; } = 1;
    public int Rows { get; init; } = 1;
    public IReadOnlyList<UiNode> Children { get; init; } = Array.Empty<UiNode>();
}

/// <summary>One reusable preview document; no callbacks, item grants or save operations are encoded.</summary>
public sealed record NativeUiDocument
{
    public int SchemaVersion { get; init; } = 1;
    public LocalizedItemText Title { get; init; } = new("UI 布局预览", "UI layout preview");
    public UiNode Root { get; init; } = new();
}
/// <summary>Expected boxes, relative to document content origin at top left. Not a font/visual acceptance report.</summary>
public sealed record UiLayoutNode(string Id, UiKind Kind, UiBox Box, UiNode Definition, IReadOnlyList<UiLayoutNode> Children);

/// <summary>Offline deterministic layout and bounded strict JSON. 离线布局运算与严格JSON读取，不调用游戏。</summary>
public static class NativeUiLayout
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    /// <summary>Read at most 64 KiB, reject duplicate/unknown fields and integer enums before layout.</summary>
    public static NativeUiDocument Parse(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var output = new MemoryStream();
        var buffer = new byte[4096]; int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0) {
            if (output.Length + read > 65536) throw Error("JSON exceeds 64 KiB");
            output.Write(buffer, 0, read);
        }
        var bytes = output.ToArray(); ReadOnlyMemory<byte> json = bytes;
        if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) json = bytes[3..];
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 40 });
        Fields(doc.RootElement, "SchemaVersion Title Root", "document");
        Text(doc.RootElement, "Title");
        var roots = doc.RootElement.EnumerateObject().Where(p => p.Name.Equals("Root", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (roots.Length != 1) throw Error("root is required");
        var root = roots[0].Value;
        int count = 0; CheckJsonNode(root, 0, ref count);
        var result = JsonSerializer.Deserialize<NativeUiDocument>(json.Span, Options) ?? throw Error("document required");
        _ = Measure(result); return result;
    }
    private static void Fields(JsonElement e, string names, string context)
    {
        if (e.ValueKind != JsonValueKind.Object) throw Error(context + " must be an object");
        var allowed = new HashSet<string>(names.Split(' '), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject())
            if (!allowed.Contains(p.Name) || !seen.Add(p.Name)) throw Error(context + ": unknown/duplicate field " + p.Name);
    }
    private static void Text(JsonElement e, string name)
    {
        foreach (var p in e.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            Fields(p.Value, "Chinese English", name); // UI preview has no implicit pack owner for CSV keys.
    }
    private static void CheckJsonNode(JsonElement e, int depth, ref int count)
    {
        if (depth > 12 || ++count > 128) throw Error("maximum 128 nodes and depth 12");
        Fields(e, "Id Kind Width Height Gap Padding Horizontal Vertical Reverse Text FontSize Wrap Columns Rows Children", "node");
        Text(e, "Text");
        if (!e.EnumerateObject().Any(p => p.Name.Equals("Kind", StringComparison.OrdinalIgnoreCase))) throw Error("kind is required");
        foreach (var p in e.EnumerateObject()) {
            if (new[] { "Kind", "Horizontal", "Vertical" }.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) {
                var names = p.Name.Equals("Kind", StringComparison.OrdinalIgnoreCase) ? Enum.GetNames<UiKind>() : Enum.GetNames<UiAlign>();
                if (p.Value.ValueKind != JsonValueKind.String || !names.Contains(p.Value.GetString(), StringComparer.OrdinalIgnoreCase))
                    throw Error("enum fields require known names, not numbers");
            }
            if (p.Name.Equals("Children", StringComparison.OrdinalIgnoreCase)) {
                if (p.Value.ValueKind != JsonValueKind.Array) throw Error("children must be an array");
                foreach (var child in p.Value.EnumerateArray()) CheckJsonNode(child, depth + 1, ref count);
            }
        }
    }
    /// <summary>Validate and snapshot a document into independent read-only nodes and absolute logical boxes.</summary>
    public static UiLayoutNode Measure(NativeUiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1 || document.Title is null) throw Error("schemaVersion 1 and title required");
        document.Title.Validate(160);
        var ids = new HashSet<string>(StringComparer.Ordinal); int count = 0;
        return Place(Build(document.Root, 0, ids, ref count), 0, 0);
    }
    private static UiLayoutNode Build(UiNode n, int depth, HashSet<string> ids, ref int count)
    {
        if (n is null || depth > 12 || ++count > 128) throw Error("maximum 128 nodes and depth 12");
        if (string.IsNullOrWhiteSpace(n.Id) || n.Id.Length > 100 || n.Id.Any(c => !((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) && c is not ('.' or '_' or '-')) || !ids.Add(n.Id))
            throw Error("unique ASCII node ID required");
        if (!Enum.IsDefined(typeof(UiKind), n.Kind) || !Enum.IsDefined(typeof(UiAlign), n.Horizontal) || !Enum.IsDefined(typeof(UiAlign), n.Vertical)) throw Error(n.Id + ": invalid enum");
        if (n.Width is < 0 or > 1024 || n.Height is < 0 or > 768 || n.Gap is < 0 or > 64 || n.Padding is < 0 or > 64 || n.Children is null)
            throw Error(n.Id + ": invalid size/gap/padding");
        if (n.FontSize is { } size && (!float.IsFinite(size) || size is < 4 or > 24)) throw Error(n.Id + ": fontSize 4..24");
        if (n.Kind == UiKind.Tag && n.Height < 16) throw Error(n.Id + ": native tag height must be at least 16");
        var group = n.Kind is UiKind.Row or UiKind.Column or UiKind.Frame;
        var text = n.Kind is UiKind.Label or UiKind.Tag or UiKind.Button;
        if (!group && n.Children.Count != 0 || n.Kind == UiKind.Frame && n.Children.Count != 1 || group && n.Kind != UiKind.Frame && n.Children.Count == 0)
            throw Error(n.Id + ": invalid child count");
        if (text) { if (n.Text is null || n.Width < 5 || n.Height == 0) throw Error(n.Id + ": text and explicit width/height required"); n.Text.Validate(2048); }
        else if (n.Text is not null || n.FontSize is not null || n.Wrap) throw Error(n.Id + ": text options on non-text node");
        if (!group && (n.Padding != 0 || n.Reverse)) throw Error(n.Id + ": padding/reverse require a group");
        var childList = new List<UiLayoutNode>();
        foreach (var child in n.Reverse ? n.Children.Reverse() : n.Children) childList.Add(Build(child, depth + 1, ids, ref count));
        var children = childList.ToArray();
        int w = 0, h = 0;
        if (n.Kind == UiKind.Row) { w = children.Sum(c => c.Box.Width) + n.Gap * (children.Length - 1); h = children.Max(c => c.Box.Height); }
        if (n.Kind == UiKind.Column) { w = children.Max(c => c.Box.Width); h = children.Sum(c => c.Box.Height) + n.Gap * (children.Length - 1); }
        if (n.Kind == UiKind.Frame) { w = children[0].Box.Width; h = children[0].Box.Height; }
        if (n.Kind is UiKind.Inventory or UiKind.Slot) {
            if (n.Columns is < 1 or > 32 || n.Rows is < 1 or > 32) throw Error(n.Id + ": inventory size 1..32");
            w = n.Columns * 16; h = n.Rows * 16;
        }
        w += 2 * n.Padding; h += 2 * n.Padding;
        if (n.Width != 0 && n.Width < w || n.Height != 0 && n.Height < h) throw Error(n.Id + ": content exceeds fixed box");
        w = Math.Max(n.Width, w); h = Math.Max(n.Height, h);
        if (w <= 0 || h <= 0 || w > 1024 || h > 768) throw Error(n.Id + ": final box exceeds 1024x768 or is empty");
        var copy = n with { Reverse = false, Children = Array.AsReadOnly(children.Select(c => c.Definition).ToArray()) };
        return new(n.Id, n.Kind, new(0, 0, w, h), copy, Array.AsReadOnly(children));
    }
    private static UiLayoutNode Place(UiLayoutNode node, int x, int y)
    {
        var n = node.Definition; var children = node.Children; var pad = n.Padding;
        int innerW = node.Box.Width - 2 * pad, innerH = node.Box.Height - 2 * pad;
        int usedW = n.Kind == UiKind.Row ? children.Sum(c => c.Box.Width) + n.Gap * (children.Count - 1) : children.FirstOrDefault()?.Box.Width ?? 0;
        int usedH = n.Kind == UiKind.Column ? children.Sum(c => c.Box.Height) + n.Gap * (children.Count - 1) : children.FirstOrDefault()?.Box.Height ?? 0;
        int cursor = n.Kind == UiKind.Row ? Offset(innerW - usedW, n.Horizontal) : Offset(innerH - usedH, n.Vertical);
        var placed = new List<UiLayoutNode>();
        foreach (var child in children) {
            int cx = x + pad + (n.Kind == UiKind.Row ? cursor : Offset(innerW - child.Box.Width, n.Horizontal));
            int cy = y + pad + (n.Kind == UiKind.Column ? cursor : Offset(innerH - child.Box.Height, n.Vertical));
            placed.Add(Place(child, cx, cy));
            if (n.Kind == UiKind.Row) cursor += child.Box.Width + n.Gap;
            if (n.Kind == UiKind.Column) cursor += child.Box.Height + n.Gap;
        }
        return node with { Box = new(x, y, node.Box.Width, node.Box.Height), Children = placed.AsReadOnly() };
    }
    internal static int Offset(int space, UiAlign align) => align == UiAlign.End ? space : align == UiAlign.Center ? space / 2 : 0;
    private static InvalidDataException Error(string message) => new("EZAPI_UI_LAYOUT: " + message);
}

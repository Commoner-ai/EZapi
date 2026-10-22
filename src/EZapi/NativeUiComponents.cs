using Il2Cpp;
using Il2CppTMPro;
using UnityEngine;

namespace EZapi;

/// <summary>
/// Small native building blocks for caller-owned windows. Native graph ownership is retained;
/// never pass a control already attached elsewhere. No patches, callbacks or processing are installed.
/// 原生窗口积木：调用方持有窗口寿命，禁止重复挂接同一控件；不接管机器加工和保存。
/// </summary>
public static class NativeUiComponents
{
    /// <summary>Horizontal flow, native pixels, vertically centered by default.</summary>
    public static PixelElement Row(params PixelElement[] children) => Row(4, UiAlign.Center, children);
    /// <summary>Horizontal flow with explicit spacing and vertical alignment. 上下居中只需选择Center。</summary>
    public static PixelElement Row(int gap, UiAlign vertical, params PixelElement[] children)
    {
        Require(children, gap, vertical);
        var height = children.Max(c => c.heightPixels);
        var grid = new GridPixelElement(children.Length, 1, false).SetSpacing(gap, 0);
        for (var i = 0; i < children.Length; i++) Attach(grid, Frame(children[i], children[i].widthPixels, height, UiAlign.Start, vertical), i, 0);
        grid.Validate(); return grid.Cast<PixelElement>();
    }
    /// <summary>Vertical flow with default four-pixel spacing and left alignment.</summary>
    public static PixelElement Column(params PixelElement[] children) => Column(4, UiAlign.Start, children);
    /// <summary>Vertical flow with explicit spacing and horizontal alignment.</summary>
    public static PixelElement Column(int gap, UiAlign horizontal, params PixelElement[] children)
    {
        Require(children, gap, horizontal);
        var width = children.Max(c => c.widthPixels);
        var grid = new GridPixelElement(1, children.Length, false).SetSpacing(0, gap);
        for (var i = 0; i < children.Length; i++) Attach(grid, Frame(children[i], width, children[i].heightPixels, horizontal, UiAlign.Start), 0, i);
        grid.Validate(); return grid.Cast<PixelElement>();
    }
    /// <summary>Center one native element in a fixed box, horizontally and vertically. No scaling.</summary>
    public static PixelElement Center(PixelElement child, int width, int height) => Frame(child, width, height, UiAlign.Center, UiAlign.Center);
    /// <summary>Align in a fixed box using whole native pixels; rejects undersized boxes.</summary>
    public static PixelElement Frame(PixelElement child, int width, int height, UiAlign horizontal = UiAlign.Start, UiAlign vertical = UiAlign.Center)
    {
        GameSession.RequireThread();
        if (child is null || child.Pointer == IntPtr.Zero || width < child.widthPixels || height < child.heightPixels || width > 1024 || height > 768 ||
            !Enum.IsDefined(typeof(UiAlign), horizontal) || !Enum.IsDefined(typeof(UiAlign), vertical))
            throw new ArgumentException($"EZAPI_UI_LAYOUT: frame {width}x{height} cannot contain native child {child?.widthPixels}x{child?.heightPixels}; valid alignments required.");
        var left = NativeUiLayout.Offset(width - child.widthPixels, horizontal);
        var top = NativeUiLayout.Offset(height - child.heightPixels, vertical);
        var right = width - child.widthPixels - left; var bottom = height - child.heightPixels - top;
        // Do not depend on the game's zero-size spacer clamping. Only create positive spacers.
        var row = new List<PixelElement>();
        if (left > 0) row.Add(Spacer(left, child.heightPixels)); row.Add(child);
        if (right > 0) row.Add(Spacer(right, child.heightPixels));
        var middle = Pack(row, true, 0);
        var column = new List<PixelElement>();
        if (top > 0) column.Add(Spacer(width, top)); column.Add(middle);
        if (bottom > 0) column.Add(Spacer(width, bottom));
        return Pack(column, false, 0);
    }
    /// <summary>Uniform inner padding without resizing the control or its hitbox.</summary>
    public static PixelElement Inset(PixelElement child, int padding)
    {
        if (padding is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(padding));
        return Frame(child, checked(child.widthPixels + 2 * padding), checked(child.heightPixels + 2 * padding), UiAlign.Center, UiAlign.Center);
    }
    /// <summary>A native label above a content control, with centered headings and an explicit gap.</summary>
    public static PixelElement Section(string title, PixelElement content, int gap = 3)
        => Column(gap, UiAlign.Center, Label(title, Math.Max(content.widthPixels, 24), 16, UiAlign.Center), content);
    /// <summary>Empty logical pixels; a positive size avoids native minimum-size ambiguity.</summary>
    public static PixelElement Spacer(int width, int height)
    {
        GameSession.RequireThread();
        if (width is < 1 or > 1024 || height is < 1 or > 768) throw new ArgumentOutOfRangeException(nameof(width));
        return new EmptyPixelElement(width, height, false).Cast<PixelElement>();
    }
    /// <summary>
    /// Native text or compact TagElement. Defaults retain the game's locale font; explicit boxes and
    /// TMP vertical alignment fix bottom-aligned captions. Reapply after caller-owned native rebuilding.
    /// </summary>
    public static PixelElement Label(string text, int width = 80, int height = 16, UiAlign horizontal = UiAlign.Start,
        UiAlign vertical = UiAlign.Center, float? fontSize = null, bool wrap = false, bool tag = false)
    {
        GameSession.RequireThread(); ValidateText(text, width, height, horizontal, vertical, fontSize);
        if (tag && height < 16) throw new ArgumentException("EZAPI_UI_LAYOUT: native tag height must be at least 16.");
        PixelElement element; TreeNodeRender node;
        if (tag) { var label = new TagElement(width, false).SetText(text, width); element = label.Cast<PixelElement>(); node = label.textNode; }
        else { var label = new RichTextElement(false, width, height, false).SetText(text, width); label.SetPadding(0, 0, 0, 0); element = label.Cast<PixelElement>(); node = label.textNode; }
        element.ResizePixels(width, height); element.Validate();
        Style(node, text, width, height, horizontal, vertical, fontSize, wrap);
        return element;
    }
    /// <summary>Native ButtonElement with centered caption; caller supplies and owns an optional native callback.</summary>
    public static ButtonElement Button(string text, int width = 80, int height = 16, Il2CppSystem.Action? callback = null, float? fontSize = null)
    {
        GameSession.RequireThread(); ValidateText(text, width, height, UiAlign.Center, UiAlign.Center, fontSize);
        var button = new ButtonElement(text, width, height, callback); button.Validate();
        Style(button.textNode, text, width, height, UiAlign.Center, UiAlign.Center, fontSize, false);
        return button;
    }
    internal static void Style(TreeNodeRender node, string text, int width, int height, UiAlign horizontal, UiAlign vertical, float? fontSize, bool wrap)
    {
        TreeNodeRender.ApplyLocaleFont(node);
        var label = node.text ?? throw new InvalidOperationException("EZAPI_UI_NATIVE: native text node unavailable.");
        var rect = node.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(width, height);
        label.enableAutoSizing = false;
        if (fontSize is { } size) label.fontSize = size;
        label.richText = false; label.text = text; label.enableWordWrapping = wrap;
        label.margin = new Vector4(2, 0, 2, 0);
        label.alignment = (vertical, horizontal) switch {
            (UiAlign.Start, UiAlign.Start) => TextAlignmentOptions.TopLeft,
            (UiAlign.Start, UiAlign.Center) => TextAlignmentOptions.Top,
            (UiAlign.Start, UiAlign.End) => TextAlignmentOptions.TopRight,
            (UiAlign.Center, UiAlign.Start) => TextAlignmentOptions.Left,
            (UiAlign.Center, UiAlign.Center) => TextAlignmentOptions.Center,
            (UiAlign.Center, UiAlign.End) => TextAlignmentOptions.Right,
            (UiAlign.End, UiAlign.Start) => TextAlignmentOptions.BottomLeft,
            (UiAlign.End, UiAlign.Center) => TextAlignmentOptions.Bottom,
            _ => TextAlignmentOptions.BottomRight
        };
        label.ForceMeshUpdate(true, true);
    }
    internal static PixelElement Pack(IReadOnlyList<PixelElement> items, bool row, int gap)
    {
        if (items.Count == 1) return items[0];
        var grid = new GridPixelElement(row ? items.Count : 1, row ? 1 : items.Count, false).SetSpacing(row ? gap : 0, row ? 0 : gap);
        for (var i = 0; i < items.Count; i++) Attach(grid, items[i], row ? i : 0, row ? 0 : i);
        grid.Validate(); return grid.Cast<PixelElement>();
    }
    private static void Attach(GridPixelElement parent, PixelElement child, int x, int y)
    { if (!parent.AttachPos(child, x, y)) throw new InvalidOperationException("EZAPI_UI_NATIVE: native attachment refused."); }
    private static void Require(PixelElement[] children, int gap, UiAlign alignment)
    {
        GameSession.RequireThread();
        if (children is null || children.Length is < 1 or > 128 || children.Any(c => c is null || c.Pointer == IntPtr.Zero || c.widthPixels <= 0 || c.heightPixels <= 0) ||
            children.Select(c => c.Pointer).Distinct().Count() != children.Length || gap is < 0 or > 64 || !Enum.IsDefined(typeof(UiAlign), alignment))
            throw new ArgumentException("EZAPI_UI_LAYOUT: positive live unique children and gap 0..64 required.");
    }
    private static void ValidateText(string text, int width, int height, UiAlign horizontal, UiAlign vertical, float? fontSize)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2048 || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) ||
            width is < 5 or > 1024 || height is < 1 or > 768 || !Enum.IsDefined(typeof(UiAlign), horizontal) || !Enum.IsDefined(typeof(UiAlign), vertical) ||
            fontSize is { } size && (!float.IsFinite(size) || size is < 4 or > 24))
            throw new ArgumentException("EZAPI_UI_LAYOUT: readable bounded text, box and font required.");
    }
}

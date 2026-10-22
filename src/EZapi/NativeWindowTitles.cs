using Il2Cpp;
using MelonLoader;

namespace EZapi;

/// <summary>
/// PixelWindow.SetTitle estimates width from character count and one locale-wide cell.
/// Mixed ASCII/CJK titles can be cut on a narrow grid even when TMP can render them.
/// Measure the actual glyphs only for windows created and owned by EZapi.
/// </summary>
internal static class NativeWindowTitles
{
    private const float MinimumFontSize = 6f;
    private const float MinimumWidthScale = .75f;
    private static readonly HashSet<string> Warnings = new(StringComparer.Ordinal);

    internal static bool Matches(PixelWindow? window, string title)
        => window is not null && window.Pointer != IntPtr.Zero && window.titleString == title &&
            window.titleText?.text is { } label && label.text == title;

    internal static bool Set(PixelWindow? window, string title)
    {
        if (window is null || window.Pointer == IntPtr.Zero || title is null) return false;

        try {
            var node = window.titleText;
            var label = node?.text;
            if (label is null) return false;
            TreeNodeRender.ApplyLocaleFont(node!);
            if (!float.IsFinite(label.fontSize) || label.fontSize < MinimumFontSize) return false;

            // Native SetTitle still performs its normal window validation. It then
            // applies its lossy length estimate; replace only that title presentation.
            window.SetTitle(title);
            var available = window.widthPixels - 3 * window.xPadding - window.topCloseButton.rectTransform.sizeDelta.x;
            var originalSize = label.fontSize;
            // A loaded/previously fitted TMP label can already have auto sizing enabled.
            // GetPreferredValues then measures from fontSizeMax, ignoring the trial fontSize;
            // disable it during measurement so every trial uses the requested fixed size.
            var originalAutoSizing = label.enableAutoSizing;
            var originalScale = node!.rectTransform.localScale;
            label.enableAutoSizing = false;
            node.rectTransform.localScale = new UnityEngine.Vector3(1f,originalScale.y,originalScale.z);
            for (var size = originalSize; size >= MinimumFontSize; size -= 1f) {
                label.fontSize = size;
                var width = label.GetPreferredValues(title).x;
                if (!float.IsFinite(available) || available <= 0 || !float.IsFinite(width) || width < 0) continue;
                var widthScale=width<=available?1f:available/width;
                // Some cold-loaded native fonts need a small horizontal fit at the
                // minimum readable size. Keep the window, grid and hitboxes unchanged.
                // Bound compression rather than making arbitrary titles unreadable.
                if(widthScale<1f&&(size>MinimumFontSize||widthScale<MinimumWidthScale))continue;
                label.enableWordWrapping = false;
                node.rectTransform.localScale = new UnityEngine.Vector3(widthScale,originalScale.y,originalScale.z);
                label.fontSizeMin = MinimumFontSize;
                label.fontSizeMax = originalSize;
                // Fixed measured size avoids TMP choosing a larger size after fitting.
                label.enableAutoSizing = false;
                window.titleString = title;
                label.text = title;
                return Matches(window, title);
            }
            label.fontSize = originalSize;
            label.enableAutoSizing = originalAutoSizing;
            node.rectTransform.localScale = originalScale;
            window.SetTitle(title);
            Warn("EZAPI_WINDOW_TITLE_LIMIT", "Localized title does not fit at the minimum readable size; native truncation retained.");
            return false;
        } catch (Exception error) {
            Warn("EZAPI_WINDOW_TITLE_UPDATE", error.GetType().Name + ": " + error.Message);
            return false;
        }
    }

    internal static void Clear() => Warnings.Clear();

    private static void Warn(string key, string message)
    {
        if (Warnings.Count < 100 && Warnings.Add(key + ":" + message))
            MelonLogger.Warning("[EZapi] " + key + ": " + message);
    }
}

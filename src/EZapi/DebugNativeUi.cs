using System.Text;
using System.Text.Json;

namespace EZapi;

/// <summary>Explicit transient native fixtures. Uses the same embedded JSON as the SDK; no save or item writes.</summary>
internal static class DebugNativeUi
{
    internal static object Verify(string locale)
    {
        var checks = new List<object>(); var samples = new List<object>(); var passed = 0; var failed = 0;
        void Check(bool ok, string name) { checks.Add(new { name, passed = ok }); if (ok) passed++; else failed++; }
        try {
            foreach (var name in new[] { "computer", "furnace", "aquaculture" }) {
                using var json = NativeUiPreviewApi.Template(name);
                var doc = NativeUiLayout.Parse(json);
                var report = NativeUiPreviewApi.Open(doc);
                samples.Add(new { template = name, report });
                Check(report.Visible && report.Locale == locale, name + ": actual native preview uses selected locale");
                Check(report.Issues.Count == 0, name + ": native boxes and actual font fit");
                Check(report.Controls.Count > 5 && report.Inspection.Text.Count > 3, name + ": actual controls and font measurements exist");
                Check(report.Inspection.Text.Any(t => t.Alignment == "Center"), name + ": native caption centers on both axes");
            }
            var overflow = new NativeUiDocument { Root = new() { Id = "overflow", Kind = UiKind.Label, Width = 16, Height = 16,
                Text = new("这段测试文字必须超出小框以验证检测", "This deliberately long caption must overflow its tiny box") } };
            var bad = NativeUiPreviewApi.Open(overflow);
            samples.Add(new { template = "intentional-overflow", report = bad });
            Check(bad.Inspection.Text.Any(t => t.Overflows) && bad.Issues.Count > 0, "actual font overflow is detected");
            using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("{\"root\":{\"id\":\"bad\",\"kind\":\"Unknown\"}}"));
            var rejected = false;
            try { NativeUiPreviewApi.Open(invalid); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && NativeUiPreviewApi.IsVisible && NativeUiPreviewApi.Measure().Controls.Single().Id == "overflow",
                "invalid JSON preserves existing native preview");
        } finally { NativeUiPreviewApi.Close(); }
        var closed = JsonSerializer.SerializeToElement(NativeUiPreviewApi.Report());
        Check(!NativeUiPreviewApi.IsVisible && closed.GetProperty("current").ValueKind == JsonValueKind.Null && closed.GetProperty("retainedControls").GetInt32() == 0,
            "closing releases current graph; last report remains historical only");
        return new { passed, failed, checks, samples, scope = "explicit transient native layouts and actual locale fonts; no gameplay/save acceptance" };
    }
}

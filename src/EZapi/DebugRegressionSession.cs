using System.Text.Json;

namespace EZapi;

/// <summary>
/// Explicit disposable-run binding for transient localization fixtures. Loaded once at startup;
/// does not authorize persistent fixtures, saves or automatic test execution.
/// 临时本地化回归绑定，不授权保存夹具或自动执行。
/// </summary>
internal sealed record DebugRegressionBinding(int Slot, string RunId)
{
    internal bool Matches(int slot, string? runId) => Slot == slot && StringComparer.Ordinal.Equals(RunId, runId);
}

internal static class DebugRegressionSession
{
    internal static DebugRegressionBinding? Current { get; private set; }
    internal static string? Error { get; private set; }

    internal static bool Matches(int slot, string? runId) => Current?.Matches(slot, runId) == true;

    internal static void Load(string path, bool enabled, Action<string> warning)
    {
        Reset();
        if (!enabled) return;
        try {
            if (!File.Exists(path)) return;
            using var file = File.OpenRead(path);
            if (file.Length > 1024) throw new InvalidDataException("Regression session exceeds 1024 bytes.");
            var bytes = new byte[1025];
            int total = 0, read;
            while (total < bytes.Length && (read = file.Read(bytes, total, bytes.Length - total)) > 0) total += read;
            Current = Parse(bytes[..total]);
        } catch (Exception error) {
            Error = "EZAPI_REGRESSION_SESSION: " + error.GetType().Name;
            warning(Error + "; provide an explicit disposable Slot/RunId in UserData/EZapi/regression-session.json.");
        }
    }

    internal static DebugRegressionBinding Parse(byte[] bytes)
    {
        if (bytes.Length > 1024) throw new InvalidDataException("Regression session exceeds 1024 bytes.");
        ReadOnlyMemory<byte> content = bytes;
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) content = bytes[3..];
        using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 2 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected session object.");
        int? slot = null; string? runId = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject()) {
            if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate session field.");
            switch (property.Name) {
                case "Slot" when property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value):
                    slot = value; break;
                case "RunId" when property.Value.ValueKind == JsonValueKind.String:
                    runId = property.Value.GetString(); break;
                default: throw new InvalidDataException("Unknown or invalid session field.");
            }
        }
        if (slot is null or < 0 || runId is null || !Guid.TryParseExact(runId, "D", out var id) || id == Guid.Empty)
            throw new InvalidDataException("Nonnegative slot and nonempty canonical run GUID required.");
        // Identity strings compare exactly. Never silently normalize a different run ID.
        return new(slot.Value, runId);
    }

    internal static object Report() => new { configured = Current is not null, error = Error,
        scope = "explicit transient localization fixtures only; no save/persistent-fixture authorization" };
    internal static void Reset() { Current = null; Error = null; }
}

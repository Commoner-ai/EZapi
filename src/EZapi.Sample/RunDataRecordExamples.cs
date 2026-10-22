using System.Text.Json;

namespace EZapi.Sample;

/// <summary>调用者在当前游戏事件内使用；不要在加载事件里不加条件地重复推进进度。</summary>
public static class RunDataRecordExamples
{
    /// <summary>
    /// 显式把任务改为一个新状态。只示范暂存，不发物、不扣款、不发起保存。
    /// expected.Json 必须来自同一次当前事件的读取；未来版本和坏数据原样保留。
    /// </summary>
    public static RunDataResult SetQuestProgress(GameRunIdentity run, int progress)
    {
        if (progress < 0) return new(RunDataStatus.Invalid, Reason: "Progress must be nonnegative.");
        const string owner = "author.tools", key = "author.tools.quest";
        var expected = RunDataApi.Read(owner, key);
        if (expected.Status is not (RunDataStatus.Missing or RunDataStatus.Present)) return expected;
        if (expected.Status == RunDataStatus.Present)
        {
            using var document = JsonDocument.Parse(expected.Json!);
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (document.RootElement.EnumerateObject().Any(field => !names.Add(field.Name)) ||
                !document.RootElement.TryGetProperty("version", out var version) ||
                !version.TryGetInt32(out var number) || number != 1)
                return new(RunDataStatus.Invalid, Reason: "Unsupported stored version; do not overwrite.");
        }
        var nextJson = JsonSerializer.Serialize(new { version = 1, progress });
        // Conflict：重新读并检查业务；Indeterminate：先核对，不能自动重做交付/扣款。
        return RunDataApi.Stage(owner, key, run, expected.Json, nextJson);
    }
}

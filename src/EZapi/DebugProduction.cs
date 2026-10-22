namespace EZapi;

/// <summary>只读目录与纯计算演示；不注册效果、不抽取效果、不改价格、不生产或保存。</summary>
internal static class DebugProduction
{
    internal static object? LastReport { get; private set; }
    internal static void Clear() => LastReport = null;
    internal static string Run()
    {
        try {
            var perOutput = ProductionValueMath.PerOutputWithOverhead(100m, 20, 11m, 3);
            var powerBatch = MachinePowerMath.CalculateCost(7, 3, false);
            var powerPerOutput = MachinePowerMath.CalculateCost(7, 3, true);
            var effects = EffectApi.Snapshot();
            var randomGuardRequired = effects.Any(e => e.Status == NativeApplicationStatus.Applied) && EffectApi.GetRuntimeStatus().Targets.Count > 0;
            string? decodeError = SaveGraphApi.DecodeUnavailableReason;
            LastReport = new { at = DateTimeOffset.Now,
                example = new { inputValue = 100m, markupPercent = 20, batchOverhead = 11m, outputCount = 3, unitValue = perOutput,
                    cost = 7, powerBatch, powerPerOutput },
                effectRegistrations = effects, randomGuardRequired, randomHook = EffectApi.GetRuntimeStatus(),
                saveGraphDecodeSignatureAvailable = decodeError is null, saveGraphDecodeError = decodeError,
                scope = "managed calculation and read-only registration status; no effect RNG, item writes, production or save" };
            EZapiBootstrap.WriteDiagnostics();
            return $"计价示例44/件；耗电示例7/批、21/三件；效果{effects.Count}项。详情已写入诊断。";
        } catch (Exception e) { return "生产/效果诊断失败：" + e.Message; }
    }
}

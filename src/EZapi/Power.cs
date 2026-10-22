using Il2Cpp;
namespace EZapi;

public enum EnergyDebitStatus { Debited, Insufficient, Rejected, Restored, RecoveryFailed, Reentrant }
public sealed record EnergyDebitResult(EnergyDebitStatus Status, int Before, int After, string? Error = null)
{
    public bool Debited => Status == EnergyDebitStatus.Debited;
}

/// <summary>原生电耗/电量读取及单件电源扣电。单位沿用原生整数；外部电源查询委托给ExternalPowerApi。</summary>
public static class Power
{
    private static readonly HashSet<IntPtr> Debiting = new();
    public static int GetMachineCost(GameItem machine)
        => MachineryHelper.GetMachinePowerUsage(NativeAccess.RequireItem(machine));
    /// <summary>总可用电量，明确关闭带宽限制，与每周期功率不同。</summary>
    public static int GetEnergy(GameItem source)
        => ExternalPowerApi.IsConnector(NativeAccess.RequireItem(source)) ? ExternalPowerApi.GetEnergy(source) : PowerHelper.GetAvailableEnergyFromItem(source, false);

    /// <summary>
    /// 主线程、当前周目、拥有且位于库存中的原生电源（包括游戏允许的堆叠电源）。
    /// 调用原生 CanDraw/Draw，严格读回差额；堆叠数量只是身份快照，不按数量缩放电量。
    /// 写后异常尝试恢复原电量；实例/周目/父库存/堆叠数量变化则停止恢复并明确报告 RecoveryFailed。
    /// </summary>
    public static EnergyDebitResult TryDebit(GameItem source, int amount)
    {
        NativeAccess.RequireItem(source);
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "EZAPI_POWER_AMOUNT: nonnegative native units required.");
        if(ExternalPowerApi.IsConnector(source)){var energy=GetEnergy(source);return new(EnergyDebitStatus.Rejected,energy,energy,"EZAPI_EXTERNAL_POWER_CONTEXT: use a machine batch; standalone debit has no machine recovery context.");}
        var run = GameSession.Identity!;
        var pointer = source.Pointer;
        var instanceId = source.uniqueId;
        var itemId = source.identifier;
        var units = source.unitCount;
        if (!Debiting.Add(pointer)) return new(EnergyDebitStatus.Reentrant, 0, 0, "EZAPI_POWER_REENTRANT");
        var before = 0;
        var attempted = false;
        IntPtr parent = IntPtr.Zero;
        try {
            if (units <= 0 || source.parentInventory is null || source.parentInventory.Pointer == IntPtr.Zero || !GeneralHelper.IsItemOwned(source))
                return new(EnergyDebitStatus.Rejected, 0, 0, "EZAPI_POWER_SOURCE: owned source in an inventory required.");
            parent = source.parentInventory.Pointer;
            before = GetEnergy(source);
            // 非电源通常返回0；原生 CanDraw 还必须认可，即使 amount=0 也不冒充可扣电源。
            if (before < 0) return new(EnergyDebitStatus.Rejected, before, before, "EZAPI_POWER_STATE: negative energy.");
            if (before < amount) return new(EnergyDebitStatus.Insufficient, before, before);
            if (!PowerHelper.CanDrawPowerSource(source, amount)) return new(EnergyDebitStatus.Rejected, before, before);
            if (amount == 0) return new(EnergyDebitStatus.Debited, before, before);
            attempted = true;
            var accepted = PowerHelper.DrawPowerSource(source, amount);
            if (!SameSource()) return new(EnergyDebitStatus.RecoveryFailed, before, -1, "EZAPI_POWER_IDENTITY: identity changed during debit; no restore write attempted.");
            var after = GetEnergy(source);
            if (accepted && after == before - amount) return new(EnergyDebitStatus.Debited, before, after);
            return Restore("EZAPI_POWER_VERIFY: native result or actual energy differs from requested debit.");
        } catch (Exception e) {
            var error = e.GetType().Name + ": " + e.Message;
            return attempted ? Restore(error) : new(EnergyDebitStatus.Rejected, before, before, error);
        } finally { Debiting.Remove(pointer); }

        bool SameSource() => NativeAccess.SameRun(run) && source.Pointer == pointer && source.uniqueId == instanceId &&
            source.identifier == itemId && source.unitCount == units && source.parentInventory?.Pointer == parent &&
            GeneralHelper.IsItemOwned(source);
        EnergyDebitResult Restore(string error)
        {
            try {
                if (!SameSource()) return new(EnergyDebitStatus.RecoveryFailed, before, -1, error);
                if (GetEnergy(source) != before) PowerHelper.SetPowerSourceAt(source, before);
                if (!SameSource()) return new(EnergyDebitStatus.RecoveryFailed, before, -1, error);
                var after = GetEnergy(source);
                return new(after == before ? EnergyDebitStatus.Restored : EnergyDebitStatus.RecoveryFailed, before, after, error);
            } catch (Exception e) { return new(EnergyDebitStatus.RecoveryFailed, before, -1, error + "; restore: " + e.Message); }
        }
    }
}

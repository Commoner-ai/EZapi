namespace EZapi;

/// <summary>
/// 一步同步事务。Check 必须只读；Apply 返回前核对写后状态。
/// Restore 必须能恢复 Apply 部分完成的状态，也必须核对写后结果。
/// 委托只在本次 Run 内执行，不得把原生对象保存到异步任务。
/// </summary>
public sealed record TransactionStep(string Name, Func<bool> Check, Func<bool> Apply, Func<bool> Restore);
public enum TransactionStatus { Committed, Rejected, Restored, RecoveryFailed, Reentrant }
public sealed record TransactionResult(TransactionStatus Status, string? FailedStep,
    IReadOnlyList<string> AppliedSteps, IReadOnlyList<string> RecoveryFailures, string? Error)
{
    public bool Committed => Status == TransactionStatus.Committed;
    /// <summary>没有发生写入，或所有尝试写入的步骤均已确认恢复。</summary>
    public bool Restored => Status is TransactionStatus.Rejected or TransactionStatus.Restored;
}

/// <summary>
/// 有界同步补偿事务。不是游戏存档事务；不自动保存、不自动重试。
/// 步骤快照固定，整组只读预检后逐步复核。失败时包括当前部分写入步骤，反向恢复全部已尝试步骤。
/// </summary>
public static class Transactions
{
    private static bool _running;
    public static TransactionResult Run(IReadOnlyList<TransactionStep> steps)
        => RunCore(steps,128);

    // 购买在最多128个作者步骤之外增加付款和最终读回；不减少公共事务的作者步骤容量。
    internal static TransactionResult RunPurchase(IReadOnlyList<TransactionStep> steps)=>RunCore(steps,130);
    private static TransactionResult RunCore(IReadOnlyList<TransactionStep> steps,int maximum)
    {
        GameSession.RequireThread();
        ArgumentNullException.ThrowIfNull(steps);
        var snapshot = steps.ToArray();
        if (snapshot.Length < 1 || snapshot.Length > maximum || snapshot.Any(s => s is null || string.IsNullOrWhiteSpace(s.Name) ||
            s.Name.Length > 160 || s.Name.Any(char.IsControl) || s.Check is null || s.Apply is null || s.Restore is null) ||
            snapshot.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("EZAPI_TRANSACTION_STEPS: 1..128 unique named synchronous steps with Check/Apply/Restore required.");
        if (_running) return Result(TransactionStatus.Reentrant, null, Array.Empty<string>(), Array.Empty<string>(), "EZAPI_TRANSACTION_REENTRANT");
        _running = true;
        var attempted = new List<TransactionStep>();
        var applied = new List<string>();
        string? failed = null;
        string? error = null;
        try {
            foreach (var step in snapshot) {
                failed = step.Name;
                if (!step.Check()) return Result(TransactionStatus.Rejected, failed, applied, Array.Empty<string>(), null);
            }
            foreach (var step in snapshot) {
                failed = step.Name;
                // 前面步骤可能改变后面步骤的资源资格，提交前再次复核。
                if (!step.Check()) return Recover(attempted, applied, failed, null);
                attempted.Add(step); // Apply 可能写入一半后抛异常，当前步骤也必须恢复。
                if (!step.Apply()) return Recover(attempted, applied, failed, null);
                applied.Add(step.Name);
            }
            return Result(TransactionStatus.Committed, null, applied, Array.Empty<string>(), null);
        } catch (Exception e) {
            error = e.GetType().Name + ": " + e.Message;
            return attempted.Count == 0
                ? Result(TransactionStatus.Rejected, failed, applied, Array.Empty<string>(), error)
                : Recover(attempted, applied, failed, error);
        } finally { _running = false; }
    }

    private static TransactionResult Recover(List<TransactionStep> attempted, List<string> applied, string? failed, string? error)
    {
        var recoveryFailures = new List<string>();
        for (var i = attempted.Count - 1; i >= 0; i--) {
            var step = attempted[i];
            try { if (!step.Restore()) recoveryFailures.Add(step.Name); }
            catch (Exception e) { recoveryFailures.Add(step.Name + ": " + e.GetType().Name + ": " + e.Message); }
        }
        return Result(recoveryFailures.Count == 0 ? TransactionStatus.Restored : TransactionStatus.RecoveryFailed,
            failed, applied, recoveryFailures, error);
    }
    private static TransactionResult Result(TransactionStatus status, string? failed, IEnumerable<string> applied,
        IEnumerable<string> recoveryFailures, string? error)
        => new(status, failed, Array.AsReadOnly(applied.ToArray()), Array.AsReadOnly(recoveryFailures.ToArray()), error);
}

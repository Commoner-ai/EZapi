using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>恢复失败后停止该实例后续加工。保存标签与当前周目内存双重隔离；不自动清故障或退款。</summary>
internal static class MachineFaultGuard
{
    private const string Tag="EZAPI_MACHINE_FAULT_V1";
    private static readonly HashSet<int> Quarantined=new();private static GameRunIdentity? _run;
    private static void Observe(){var run=NativeAccess.RequireRun();if(run!=_run){_run=run;Quarantined.Clear();}}
    internal static bool Blocked(GameItem machine){Observe();return Quarantined.Contains(machine.uniqueId)||!string.IsNullOrEmpty(machine.GetTagReadonly(Tag)?.valueString);}
    internal static void Record(GameItem machine,MachineTransactionResult result)
    {
        Observe();Quarantined.Add(machine.uniqueId);
        try {
            var text=(result.Error??"RecoveryFailed");if(text.Length>2000)text=text[..2000];
            machine.state??=new TagSystem();machine.state.GetTag(Tag).Enable().SetString(text);machine.SyncModifiedState();
            if(machine.GetTagReadonly(Tag)?.valueString!=text)throw new InvalidOperationException("fault marker readback mismatch");
        }catch(Exception e){MelonLogger.Error("[EZapi] EZAPI_MACHINE_QUARANTINE: in-memory quarantine retained; save marker failed: "+e.Message);}
    }
}

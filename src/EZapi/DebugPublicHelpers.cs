namespace EZapi;

/// <summary>附加到通用注册按钮；纯托管状态与真实日志后端检查，不碰玩家库存/存档或安装输入补丁。</summary>
internal static class DebugPublicHelpers
{
    internal static void Run(Action<bool,string> check)
    {
        var entries=new List<LogEntry>();var log=new ModLog("ezapi.debug.helpers",LogLevel.Warn,entries.Add);
        log.Info("hidden");log.Warn("visible");log.Error("visible error in managed receiver only");
        check(entries.Count==2&&entries[0].Level==LogLevel.Warn&&entries[1].Level==LogLevel.Error,"helpers: per-mod logging filters structured receiver");
        int evaluated=0;log.Write(LogLevel.Debug,()=>{evaluated++;return "hidden";});
        check(evaluated==0&&!log.IsDebugEnabled,"helpers: disabled lazy formatter never runs");
        var native=new ModLog("ezapi.debug.helpers",LogLevel.Debug);
        native.Debug("EZAPI_PUBLIC_HELPERS native Debug route");native.Info("EZAPI_PUBLIC_HELPERS native Info route");
        native.Callback("EZAPI_PUBLIC_HELPERS native Callback route");
        check(native.IsDebugEnabled&&native.Level==LogLevel.Debug,"helpers: real MelonLoader backend methods completed");
        check(LogLevels.TryParse("警告",out var zh)&&LogLevels.TryParse("warn",out var en)&&zh==en,"helpers: bilingual log levels agree");
        var input=new WindowInputCapture();input.Open(0,true);
        check(input.IsOpen&&input.BlocksPointer(1,false,true),"helpers: opening gesture captured outside panel");
        input.Close(2,true);check(!input.IsOpen&&input.BlocksPointer(3,false,true),"helpers: closed panel retains own held gesture");
        check(input.BlocksPointer(4,false,false)&&input.BlocksPointer(4,false,false),"helpers: release frame survives duplicate query");
        check(!input.BlocksPointer(5,false,false),"helpers: closed capture ends after release frame");
        var independent=new WindowInputCapture();independent.Open(6,false);
        check(independent.IsOpen&&!input.IsOpen&&!input.BlocksPointer(6,false,false),"helpers: capture instances isolated");
        input.Reset();check(!input.BlocksPointer(0,false,false),"helpers: Reset releases local input state and frame scope");
        check(ManufacturingTerminal.NormalizeItemId(" "+ManufacturingTerminal.ItemId+" ")==ManufacturingTerminal.ItemId,"helpers: canonical terminal ID normalized");
        check(ManufacturingTerminal.NormalizeItemId("author.old.press",new[]{"author.old.press"})==ManufacturingTerminal.ItemId,"helpers: explicit caller alias normalized");
        check(ManufacturingTerminal.NormalizeItemId("other.mod.item")=="other.mod.item","helpers: unrelated ID preserved");
    }
}

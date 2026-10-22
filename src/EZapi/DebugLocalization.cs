using MelonLoader;
namespace EZapi;

/// <summary>只读取已排队的托管 CSV 快照；不切游戏语言，不写物品或存档。</summary>
internal static class DebugLocalization
{
    private static string L(string zh, string en) => NativeItemLocalization.IsEnglish ? en : zh;
    internal static object? LastReport { get; private set; }
    internal static void Clear() {LastReport=null;DebugGuideLocalization.Clear();}
    internal static string Run()
    {
        var catalog = Localization.GetPack("ezapi.sample");
        if (catalog is null) return L("示例本地化未排队；请导出诊断查看内容包错误", "Sample localization is not queued; export diagnostics for content-pack errors.");
        var zh = catalog.Resolve("zh", "debug_ready", "本地化示例不可用", catalog.Index.Files);
        var en = catalog.Resolve("en", "debug_ready", "Localization sample unavailable", catalog.Index.Files);
        LastReport = new { index = catalog.Index, zh, en, explicitLocales = new[] {"zh", "en"}, gameLocaleChanged = false };
        var text = zh.Text + " | " + en.Text;
        MelonLogger.Msg("[EZapi] Localization sample: " + text);
        if(DebugGuideLocalization.Start(allSurfaces:true))return text+" | "+L("正在临时切换中英文批量验证物品、机器和界面，结束恢复原语言。", "Checking items, machines and UI in English and Chinese; the original language will be restored.");
        return text+" | "+(DebugRegressionSession.Current is null?L("请绑定可丢弃测试档 regression-session.json 并重启后再运行批量检查。", "Bind a disposable run in regression-session.json and restart before batch checks."):L("批量检查未启动；请确认测试档、店铺与拖拽状态。", "Batch did not start; check the bound run, store and item-drag state."));
    }
}

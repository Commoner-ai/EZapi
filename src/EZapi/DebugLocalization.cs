using MelonLoader;
namespace EZapi;

/// <summary>只读取已排队的托管 CSV 快照；不切游戏语言，不写物品或存档。</summary>
internal static class DebugLocalization
{
    internal static object? LastReport { get; private set; }
    internal static void Clear() {LastReport=null;DebugGuideLocalization.Clear();}
    internal static string Run()
    {
        var catalog = Localization.GetPack("ezapi.sample");
        if (catalog is null) return "示例本地化未排队；请导出诊断查看内容包错误";
        var zh = catalog.Resolve("zh", "debug_ready", "本地化示例不可用", catalog.Index.Files);
        var en = catalog.Resolve("en", "debug_ready", "Localization sample unavailable", catalog.Index.Files);
        LastReport = new { index = catalog.Index, zh, en, explicitLocales = new[] {"zh", "en"}, gameLocaleChanged = false };
        var text = zh.Text + " | " + en.Text;
        MelonLogger.Msg("[EZapi] Localization sample: " + text);
        if(DebugGuideLocalization.Start(allSurfaces:true))return text+" | 正在临时切换中英文批量验证物品、机器和界面，结束恢复原语言。";
        return text;
    }
}

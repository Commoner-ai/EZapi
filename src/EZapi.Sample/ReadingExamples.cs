namespace EZapi.Sample;

/// <summary>在当前主线程游戏回调中传入身份；每次读档重新获取，勿缓存跨周目的句柄。</summary>
public static class ReadingExamples
{
    private const string Owner="author.example",Document=Owner+".welcome";
    public static PresentationResult Show(LifecycleContext context)
    {
        if(!ReadingApi.IsAvailable)return new(PresentationStatus.Unavailable,"原生阅读钩子尚未就绪");
        var run=context.Run; // 当前 LifecycleApi 回调提供当前周目/槽位；不要跨回调缓存 context。
        // Conflict 表示其它阅读内容/拖拽优先；不要循环强开或关闭别人的面板。
        return ReadingApi.Open(Owner,Document,run.RunId,run.SlotId,
            new LocalizedItemText("欢迎来到我的 Mod","Welcome to my Mod"),
            new LocalizedItemText("第一行正文。\n第二行正文。","First line of the document.\nSecond line of the document."),
            new LocalizedItemText("示例作者","Example author"));
    }
    public static PresentationResult Close()=>ReadingApi.Close(Owner,Document);
}

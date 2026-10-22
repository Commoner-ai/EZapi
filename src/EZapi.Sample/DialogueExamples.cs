namespace EZapi.Sample;

/// <summary>可复用不可变脚本；展示使用当前生命周期身份，业务自行处理余额/库存/持久幂等。</summary>
public static class DialogueExamples
{
    private const string Owner="author.example",Request=Owner+".conversation";
    private static readonly DialogueScript Script=DialogueApi.Create(Owner,new(
        Owner+".greeting","示例访客","你好，需要了解什么？",
        new[]{new DialogueChoice("about","了解这个 Mod","确认后展示介绍。",
            // Dynamic replies use the opening page's locale, including after a game-language switch.
            context=>new LocalizedItemText("欢迎使用 EZapi 内容 Mod。","Welcome to this EZapi content Mod.").ForLocale(context.LocaleCode)!){
                LocalizedLabel=new("了解这个 Mod","About this Mod"),
                LocalizedPreview=new("确认后展示介绍。","Confirm to read the introduction.")}}
    ){
        LocalizedSpeaker=new("示例访客","Example visitor"),
        LocalizedBody=new("你好，需要了解什么？","Hello. What would you like to know?")
        // Default navigation labels already follow the current game language.
    });
    public static PresentationResult Show(LifecycleContext context)
    {
        if(!DialogueApi.IsAvailable)return new(PresentationStatus.Unavailable,"原生对话钩子未就绪");
        // Conflict 让当前原版/其它 Mod 对话优先；勿循环强开。
        return DialogueApi.Open(Owner,Request,context.Run.RunId,context.Run.SlotId,Script);
    }
    public static PresentationResult Close()=>DialogueApi.Close(Owner,Request);
}

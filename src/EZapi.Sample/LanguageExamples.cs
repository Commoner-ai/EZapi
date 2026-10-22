namespace EZapi.Sample;

/// <summary>中英互通：电话名实时查询；对话每次打开重新构建；不在静态初始化缓存翻译结果。</summary>
public static class LanguageExamples
{
    private const string Owner="author.language";
    private static readonly LocalizedItemText Adviser=new("维修顾问","Repair adviser");
    private static readonly LocalizedItemText Prompt=new("需要了解什么？","What would you like to know?");
    private static readonly LocalizedItemText Help=new("查看帮助","Show help");
    private static readonly LocalizedItemText Preview=new("确认后显示说明。","Confirm to show instructions.");
    private static readonly LocalizedItemText Instructions=new("请将维修材料放入机器的输入槽。","Place repair materials in the machine input slots.");

    // 由作者Mod的初始化调用一次。电话按当前语言显示名称；每次通话按当时语言创建脚本。
    public static PresentationResult RegisterPhone()=>PhoneApi.Register(Owner,new(
        Owner+".help",9000000018,_=>true,_=>GameLanguage.Resolve(Adviser),_=>CreateHelp()));

    public static DialogueScript CreateHelp()
    {
        // 声明保留双语，框架在实际打开图时统一解析；回复使用该图的语言。
        // 即使玩家在确认之前切换设置，当前页仍使用同一种语言。
        return DialogueApi.Create(Owner,new(Owner+".greeting","Repair adviser","What would you like to know?",new[]{
            new DialogueChoice("help","Show help","Confirm to show instructions.",context=>
                new GameLanguageSnapshot(context.LocaleCode,false).Resolve(Instructions)){
                    LocalizedLabel=Help,LocalizedPreview=Preview}
        }) {
            LocalizedSpeaker=Adviser,LocalizedBody=Prompt
        });
    }

    // 已排队包的CSV目录只取一次快照；每次渲染只解析内存，不重复读取文件。
    public static string DescribeCatalog(LocalizationCatalog catalog,int files)=>GameLanguage.Resolve(catalog,
        "debug_ready",GameLanguage.Resolve(new("语言资源暂不可用","Language resources unavailable")),files).Text;
}

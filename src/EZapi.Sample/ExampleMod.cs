using MelonLoader;
using EZapi;
[assembly:MelonInfo(typeof(EZapi.Sample.ExampleMod),"EZapi CSharp Sample","0.1.15","EZapi")]
[assembly:MelonGame(null,null)]
[assembly:MelonAdditionalDependencies("EZapi")]
namespace EZapi.Sample;
/// <summary>
/// 可选 C# 调用示例；初始化只提交定义。EZapi 会在官方目录事件内登记工厂。
/// 图像内容包请调用 ItemRegistry.RegisterPack(绝对目录)，不要手写游戏目录/tooltip 补丁。
/// 此示例使用独立 ID，不与默认 JSON 示例重复。
/// </summary>
public sealed class ExampleMod:MelonMod
{
    private IDisposable? _loaded;
    public override void OnInitializeMelon()
    {
        var result=ItemRegistry.Register(new ItemDefinition {
            Id="ezapi.code.marker",Name="EZapi C# 示例",Description="激活时记录一次日志；不消耗物品，不自动发放。",Width=1,Height=1,Value=25,
            LocalizedName=new("EZapi C# 示例","EZapi C# example"),
            LocalizedDescription=new("激活时记录一次日志；不消耗物品，不自动发放。","Activation logs once; no consumption or automatic delivery."),
            ActivateDefault=true});
        LoggerInstance.Msg(result.ItemId+": "+result.Message);
        _loaded=GameEvents.Subscribe("ezapi.code",GameEvent.GameLoadedNormal,context=> {
            // 显示时读取游戏当前语言；CSV 保留作者提供的更多语言。
            var catalog=Localization.GetPack("ezapi.sample");
            if(catalog is not null)LoggerInstance.Msg(GameLanguage.Resolve(catalog,"debug_ready","Localization sample unavailable",catalog.Index.Files).Text);
            if(!RunData.TryOpen("ezapi.code",out var data))return;
            // 读取自己的周目数据，不自动创建计数/发奖。要暂存可用 data.SetInt，等待游戏正常保存。
            LoggerInstance.Msg("EZapi lifecycle example: slot="+data!.Identity.SaveSlotId+", counter="+data.GetInt("counter"));
        });
        if(result.Accepted) {
            // 独立测试表，不自动接入原版拾荒/供应商。实际提交等待官方目录/掉落表就绪。
            var loot=LootTables.Register("ezapi.code",new LootDefinition {
                Tables=new[]{new LootListDefinition {Id="ezapi.code.markers",
                    Entries=new[]{new LootWeightDefinition {Id=result.ItemId,Weight=1}}}}
            });
            LoggerInstance.Msg("EZapi C# loot: "+loot.Message);
            // 查询回调必须无副作用；框架已检查当前玩家、所有权、所属库存与禁用门控。
            // 上下文只在本次同步回调内有效，不能保存到静态字段或异步任务。
            var behavior=ItemActivation.Register(result.ItemId,context=>true,
                context=>LoggerInstance.Msg("EZapi C# activation received."));
            LoggerInstance.Msg(behavior.ItemId+": activation "+behavior.Message);
        }

        // 可选测试/教学调用者：验证 DLL owner 来源名、动态模块 tooltip 与效果双语文本。
        foreach(var registration in PresentationExamples.Register())
            LoggerInstance.Msg("EZapi presentation: "+(registration.Accepted?"staged":"rejected")+" "+registration.Reason);
        const string effectId="ezapi.sample.stable-effect";
        var effect=ProductionEffectExamples.RegisterNamedEffect();
        LoggerInstance.Msg("EZapi presentation effect: "+(effect.Accepted?"staged":"rejected")+" "+effect.Reason);
        if(effect.Accepted) {
            var effectText=PresentationApi.RegisterEffectText("ezapi.sample",effectId,
                new("稳定效果","Stable effect"),
                new("双语效果说明示例；不提供额外数值。","Bilingual effect text example; no gameplay bonus."));
            LoggerInstance.Msg("EZapi presentation effect text: "+(effectText.Accepted?"staged":"rejected")+" "+effectText.Reason);
        }
        LoggerInstance.Msg("EZapi presentation lookup: "+
            PresentationApi.GetRegisteredItemName("author.tools.sensor")+" / "+
            PresentationApi.GetRegisteredItemDescription("author.tools.sensor"));
    }
    public override void OnDeinitializeMelon()=>_loaded?.Dispose();
}


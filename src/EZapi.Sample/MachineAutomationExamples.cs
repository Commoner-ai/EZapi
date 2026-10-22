using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>复制到自己的Mod后显式调用；此类不会自行注册或生成物品。订阅句柄由调用者持有并在关闭时释放。</summary>
public static class MachineAutomationExamples
{
    /// <summary>初始化主线程注册。材料/产物须事先声明；夜间默认一批，不需要自行订阅EndNight或请求保存。</summary>
    public static ContentSubmissionResult Register(string materialId,string productId)
        =>MachineRegistrationApi.RegisterMachine("ezapi.sample",new("ezapi.sample.night_press",
            new(){Title="夜间压制机",ItemInput=new(4,4),Output=new(){Size=new(4,4)}},new[]{
                new MachineRecipe("ezapi.sample.night_recipe",new[]{new MachineIngredient(materialId)},new(productId))
            }){Power=new(0),ItemOptions=new("夜间压制机"),EnableNightly=true});

    /// <summary>只读业务条件决定选择或阻止。不要在条件中消费材料、创建物品、保存或缓存context。</summary>
    public static IDisposable SelectWhen(Func<GameItem,bool> enabled)
        =>MachineAutomationApi.Subscribe("ezapi.sample","ezapi.sample.night_press",MachineAutomationPhase.BeforeBatch,ctx=>{
            if(!enabled(ctx.Machine)){ctx.Block();return;}
            ctx.SelectRecipe("ezapi.sample.night_recipe");
        });

    /// <summary>仅实际执行事务后触发，含拒绝/已恢复/恢复失败；先检查Committed。通知异常不会撤销已提交结果。</summary>
    public static IDisposable Observe(Action<MachineBatchResult> onResult)
        =>MachineAutomationApi.Subscribe("ezapi.sample","ezapi.sample.night_press",MachineAutomationPhase.AfterBatch,ctx=>{
            if(ctx.BatchResult is {} result)onResult(result);
        });
}

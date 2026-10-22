using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>可复制的一次声明示例；此示例显式关闭夜间加工；不会自动运行、发放或保存。材料/产物ID须事先存在。</summary>
public static class MachineRegistrationExamples
{
    /// <summary>在自己Mod的OnInitializeMelon中调用。读取Accepted/Reason，失败时停止此机器的后续操作。</summary>
    public static ContentSubmissionResult Register(string materialId,string productId)
        =>MachineRegistrationApi.RegisterMachine("ezapi.sample",new("ezapi.sample.simple_press",
            new(){Title="简易压制机",ItemInput=new(4,4),Output=new(){Size=new(4,4)}},new[]{
                new MachineRecipe("ezapi.sample.press_recipe",new[]{new MachineIngredient(materialId,2)},
                    new(productId,PrepareItem:(plan,item)=>ProductionValueApi.SetBasis(item,ProductionValueApi.Calculate(plan.Context,20))))
            }) {
                EnableNightly=false, // 手动示例；需要每晚自动加工时启用并参考MachineAutomationExamples。
                Power=new(0), // 明确无电池机器；需要电池时在Template声明Battery并指定真实电耗。
                ItemOptions=new("简易压制机","放入两份材料，再调用ProcessBatch加工。"),
                ConfigureItem=item=>item.SetValue(100),Guide=new("synthesis","两份材料压成一件产物。"),ProductionMarkupPercent=20
            });
    /// <summary>另一位作者追加自己的配方；按调用顺序排在基础配方之后，不能覆盖已有ID。</summary>
    public static ContentSubmissionResult AddRecipe(string ingredientId,string productId)
        =>MachineRegistrationApi.RegisterAdditionalRecipes("other.author","ezapi.sample.simple_press",new[]{
            new MachineRecipe("other.author.press_alternative",new[]{new MachineIngredient(ingredientId)},new(productId)) {
                Guide=new("recycling","替代材料配方，由other.author维护。")
            }
        });
    /// <summary>进入周目后显式调用一次；ownedMachine必须属于玩家。RecoveryFailed不能自动重试。</summary>
    public static MachineBatchResult Process(GameItem ownedMachine)=>MachineRecipeApi.ProcessBatch(ownedMachine);
    /// <summary>自定义窗口无需改造成EZapi布局；传入当前机器真实子库存，框架核对父图/Template并执行同一标准事务。</summary>
    public static MachineBatchProcessor CreateCustomWindowProcessor(string machineId,string materialId,string productId)
        =>MachineRuntimeApi.CreateBatchProcessor("ezapi.sample",new(machineId,
            new(){ItemInput=new(4,4),Output=new(){Size=new(4,4)}},
            new[]{new MachineRecipe("ezapi.sample.custom_window_recipe",new[]{new MachineIngredient(materialId)},new(productId))}){Power=new(0)});
    /// <summary>仅在当前周目同步调用；inventory来自ownedMachine的真实窗口；换场景/读档后重新获取。</summary>
    public static MachineBatchResult ProcessCustomWindow(MachineBatchProcessor processor,GameItem ownedMachine,MachineInventory inventory)
        =>processor.Process(ownedMachine,inventory);
    /// <summary>仅捕获稳定ID，不捕获周目物品。材料/容器/液体须已注册；玩家自行投料，本例不自动生成或保存。</summary>
    public static ContentSubmissionResult RegisterLiquidMachine(string materialId,string vesselId,string liquidId)
        =>MachineRegistrationApi.RegisterMachine("ezapi.sample",new("ezapi.sample.conditional_liquid_machine",
            new(){ItemInput=new(3,3),Output=new(){Kind="container",Size=new(3,3),ContainerCondition=item=>item.identifier==vesselId},
                LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Size=new(3,3),Condition=item=>item.identifier==vesselId}},Battery=new(new(2,2))},
            new[]{new MachineRecipe("ezapi.sample.conditional_liquid_recipe",new[]{new MachineIngredient(materialId,2)},
                new MachineContainerOutput(new[]{new MachineLiquidAmount(liquidId,1)})){LiquidInputs=new[]{new MachineLiquidIngredient("water",1)}}}) {
                    EnableNightly=false,Guide=null,Power=new(7),ItemOptions=new("液体条件示例机")
                });
}

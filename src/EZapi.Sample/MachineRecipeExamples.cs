using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>省略Transaction使用框架标准消费；复杂规则才提供自定义事务。示例不自动生产或保存。</summary>
public static class MachineRecipeExamples
{
    /// <summary>明确消费整个容器材料及全部内部物品。失败退回原实例与内容物；成功后不返还空容器，不拆分含内容物堆叠。</summary>
    public static ContentSubmissionResult RegisterWholeContainer(string machineId,string containerMaterialId)
        =>MachineRecipeApi.RegisterRecipes("ezapi.sample",machineId,new[]{
            new MachineRecipe("ezapi.sample.whole_container_material",new[]{new MachineIngredient(containerMaterialId){WholeStack=true}},
                new MachineItemOutput("ezapi.sample.field_kit")){
                    MaterialContents=MachineMaterialContentsPolicy.ConsumeGraph
                }
        },new MachinePowerRule(7));
    /// <summary>标准材料消费：机器须有EZapi布局，材料须已注册。保留余堆，失败原路退回；不自动保存或跨夜。</summary>
    public static ContentSubmissionResult RegisterStandard(string machineId,string materialId)
        =>MachineRecipeApi.RegisterRecipes("ezapi.sample",machineId,new[]{
            new MachineRecipe("ezapi.sample.standard_field_kit",new[]{new MachineIngredient(materialId,2)},
                new MachineItemOutput("ezapi.sample.field_kit",PrepareItem:(plan,item)=>
                    ProductionValueApi.SetBasis(item,ProductionValueApi.Calculate(plan.Context,20))))
        });
    /// <summary>
    /// 只扣water槽中的指定液体1ml（1000原生parts），其他成分保留。
    /// liquidId须来自当前游戏真实液体目录；材料2件、电量7点、输出及失败恢复均由标准事务处理。
    /// 机器和材料ID须已注册；同一机器整组配方会被替换，作者应自行合并目录。
    /// </summary>
    public static ContentSubmissionResult RegisterComponentLiquid(string machineId,string materialId,string liquidId)
        =>MachineRecipeApi.RegisterRecipes("ezapi.sample",machineId,new[]{
            new MachineRecipe("ezapi.sample.component_field_kit",new[]{new MachineIngredient(materialId,2)},
                new MachineItemOutput("ezapi.sample.field_kit")) {
                LiquidInputs=new[]{new MachineLiquidIngredient("water",1,liquidId)}
            }
        },new MachinePowerRule(7));
    /// <summary>
    /// machineId对应作者已注册并创建EZapi布局的机器；materialId对应真实已注册材料。
    /// transactionFactory必须实现真实消费/液体/电力/交付与部分失败恢复。plan只在这次调用有效。
    /// 本例没有液体投入；默认使用官方机器电耗，避免重复乘效率。
    /// </summary>
    public static ContentSubmissionResult Register(string machineId,string materialId,
        Func<MachineRecipePlan,IMachineBatchTransaction> transactionFactory,
        Action<MachineRecipePlan,IReadOnlyList<GameItem>>? notify=null)
    {
        ArgumentNullException.ThrowIfNull(transactionFactory);
        return MachineRecipeApi.RegisterRecipes("ezapi.sample",machineId,new[]{
            new MachineRecipe("ezapi.sample.field_kit_recipe",new[]{new MachineIngredient(materialId,2)},
                new MachineItemOutput("ezapi.sample.field_kit",PrepareItem:(plan,product)=>
                    ProductionValueApi.SetBasis(product,ProductionValueApi.Calculate(plan.Context,20)))) {
                RuleRevision="1",Transaction=transactionFactory,AfterCommitted=notify
            }
        },productionMarkupPercent:20);
    }
    /// <summary>机器输出布局Kind="container"，玩家先放入自己的液体容器。标准事务新增2ml，不清空原有成分。</summary>
    public static ContentSubmissionResult RegisterLiquidOutput(string machineId,string materialId,string liquidId)
        =>MachineRecipeApi.RegisterRecipes("ezapi.sample",machineId,new[]{
            new MachineRecipe("ezapi.sample.liquid_product",new[]{new MachineIngredient(materialId,2)},
                new MachineContainerOutput(new[]{new MachineLiquidAmount(liquidId,2,ResolveValue:_=>10){ResolveQualityBasis=_=>3}}))
        },new MachinePowerRule(7));
    /// <summary>按需显式调用一次；失败不能循环补发，RecoveryFailed需先处理故障。</summary>
    public static MachineBatchResult Process(GameItem machine)=>MachineRecipeApi.ProcessBatch(machine);
    /// <summary>登记后安装；调用方保存并释放句柄。目录替换后规则自动读取新定义。</summary>
    public static IDisposable FilterInputs(string machineId)=>MachineRecipeApi.InstallInputAdmission("ezapi.sample",machineId);
}

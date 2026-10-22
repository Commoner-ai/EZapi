using EZapi;
namespace EZapi.Sample;

/// <summary>可编译示例；由内容Mod初始化显式调用，不在示例DLL中自动提交或发放。</summary>
public static class ManufacturingTerminalExamples
{
    /// <summary>inputId/outputId须由调用者或其依赖在目录初始化前声明；注册不会创建任何物品。</summary>
    public static ContentSubmissionResult Register(string inputId,string outputId)
        =>MachineRegistrationApi.RegisterAdditionalRecipes("author.tools",ManufacturingTerminal.ItemId,new[]{
            new MachineRecipe("author.tools.terminal_product",new[]{new MachineIngredient(inputId,2)},new(outputId)) {
                Output=new(outputId) {
                    // 配置只提供价值计算依据；框架不会覆盖作者自己的定价策略。
                    // 使用实际投入总价值、共享加价和真实产量计算一次，不再手工乘数量或重复加价。
                    PrepareItem=(plan,item)=>ProductionValueApi.SetBasis(item,
                        ProductionValueApi.Calculate(plan.Context,plan.Context.ProductionMarkupPercent))
                },
                Guide=new("synthesis","每晚至多一批；原生效率模块影响电耗。")
            }
        });

    /// <summary>可选。游戏初始化后主线程调用并检查Accepted；其他作者已取得配置权时会拒绝。</summary>
    public static ContentSubmissionResult ConfigureMarkup(int percent)
        =>MachineRegistrationApi.ConfigureManufacturingTerminalMarkup("author.tools",percent);
}

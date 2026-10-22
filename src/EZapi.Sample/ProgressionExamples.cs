using EZapi;
using System.IO;
namespace EZapi.Sample;

/// <summary>知识库扩展示例：只保存定义，实际状态每次重新查询。</summary>
public static class ProgressionExamples
{
    public static RequirementDefinition UnlockGate=>new() {
        Message=new("开放加工功能的条件","Requirements for processing"),
        Children=new[] {
            new RequirementDefinition{Kind="day-at-least",Amount=3,Message=new("第3天起开放","Available from day3")},
            new RequirementDefinition{Kind="credits-at-least",Amount=100,Message=new("至少100信用点","At least100 credits")}
        }
    };
    // 在已载入周目的主线程执行，例如工坊快照/配方MachineCondition/对话Preview回调。
    public static bool CanOffer()=>RequirementApi.Evaluate(UnlockGate).Satisfied;
    public static RequirementDefinition LoadConditions(Stream json)=>Requirements.Parse(json);
    public static string DescribeService(string id)
    {
        var result=ProgressionApi.ReadService(id);
        return result.Status==ProgressionReadStatus.Ready
            ?$"{result.Value!.NativeDisplayName}: {result.Value.Cost} / active={result.Value.Active}"
            :result.Reason??result.Status.ToString();
    }
}

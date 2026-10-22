using System.Text.Json.Serialization;
using Il2Cpp;
namespace EZapi;

public sealed partial class MachineContainerSlot
{
    private Func<GameItem,bool>? _condition;
    /// <summary>C#只读准入条件，与原生容器条件及AllowedIds/AllowedTags取交集。不可捕获周目原生实例；不进入布局JSON。</summary>
    [JsonIgnore] public Func<GameItem,bool>? Condition {get=>_condition;init=>_condition=value;}
    internal void CopyCondition(MachineContainerSlot source)=>_condition=source._condition;
}
public sealed partial class MachineOutputTemplate
{
    private Func<GameItem,bool>? _containerCondition;
    /// <summary>仅Kind=container可用。限制已有输出容器；与配方Condition取交集，加工前重新检查。不可修改游戏状态。</summary>
    [JsonIgnore] public Func<GameItem,bool>? ContainerCondition {get=>_containerCondition;init=>_containerCondition=value;}
    internal void CopyCondition(MachineOutputTemplate source)=>_containerCondition=source._containerCondition;
}
public static partial class JsonMachineLayout
{
    static partial void ValidateRuntimeConditions(MachineLayoutDefinition definition)
    {
        if(definition.Output.Kind!="container"&&definition.Output.ContainerCondition is not null)
            throw new ArgumentException("EZAPI_MACHINE_LAYOUT: ContainerCondition requires container output.");
    }
    static partial void CopyRuntimeConditions(MachineLayoutDefinition source,MachineLayoutDefinition target)
    {
        target.Output.CopyCondition(source.Output);
        for(var i=0;i<source.LiquidInputs.Count;i++)target.LiquidInputs[i].CopyCondition(source.LiquidInputs[i]);
    }
}

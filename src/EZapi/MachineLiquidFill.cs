using System.Reflection;
using Il2Cpp;
namespace EZapi;

/// <summary>
/// 标准机器已有容器输出。只调用当前构建AddLiquid正数并逐成分读回；
/// 不替换容器、不清空旧液体。冻结和原标签补偿归DefaultMachineTransaction管理。
/// </summary>
internal static class MachineLiquidFill
{
    internal static void RequireAvailable()
    {
        if(!GameBuildFingerprint.IsKnown||typeof(WaterHelper).GetMethod(nameof(WaterHelper.AddLiquid),BindingFlags.Public|BindingFlags.Static,null,
            new[]{typeof(GameItem),typeof(string),typeof(int)},null)?.ReturnType!=typeof(void))
            throw Error("known build and exact AddLiquid(GameItem,string,int):void required");
    }
    internal static void Apply(MachineRecipePlan plan)
    {
        RequireAvailable();
        var item=plan.OutputContainer??throw Error("existing output vessel required");
        var run=NativeAccess.RequireRun();var pointer=item.Pointer;var instance=item.uniqueId;var state=item.state.Pointer;var dict=item.state.dict.Pointer;
        bool Current()=>NativeAccess.SameRun(run)&&item.Pointer==pointer&&item.uniqueId==instance&&item.state.Pointer==state&&item.state.dict.Pointer==dict&&
            item.parentInventory?.Pointer==plan.Inventory.Output.Pointer&&InventoryFreezeApi.IsFrozen(item);
        var before=plan.OutputContainerBefore??throw Error("output before snapshot required");
        if(!Current()||!LiquidMetadata.Same(before,Liquids.CaptureProduction(item)))throw Error("output changed before fill");
        // 全部加入量在写前已验证；每次只加本成分，原生回调若改变其他液体则停止并补偿。
        var expected=before;
        foreach(var part in plan.OutputContents) {
            expected=LiquidCompositionMath.Add(expected,new[]{part});
            if(part.Parts>0)WaterHelper.AddLiquid(item,part.LiquidId,part.Parts);
            if(!Current()||!LiquidMetadata.SamePhysical(expected,MachineLiquidSnapshot.From(Liquids.Capture(item))))throw Error("native fill readback differs");
        }
        if(!Current()||!LiquidMetadata.Same(expected,plan.OutputContainerAfter!))throw Error("planned output differs");
        LiquidMetadata.Store(item,expected);
    }
    private static InvalidOperationException Error(string reason)=>new("EZAPI_MACHINE_LIQUID_OUTPUT: "+reason);
}

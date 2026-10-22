using System.Reflection;
using Il2Cpp;
using Il2CppInterop.Runtime;
namespace EZapi;

/// <summary>
/// 标准机器事务内部的精确成分扣取。物品标签按官方ModifyTag入口写入，
/// 再执行当前构建Remove使用的OnLiquidContainerPourOutOf；不清空/重建容器，不使用负AddLiquid。
/// 调用者必须持有完整图冻结和逐标签备份；失败交回默认事务统一补偿。
/// </summary>
internal static class MachineLiquidDebit
{
    private static readonly Lazy<string?> SignatureError=new(CheckSignatures);
    internal static void RequireAvailable()
    {
        if(!GameBuildFingerprint.IsKnown)throw Error("component debit requires the verified game build");
        if(SignatureError.Value is { } error)throw Error(error);
    }
    private static string? CheckSignatures()
    {
        var modify=typeof(GameItem).GetMethod(nameof(GameItem.ModifyTag),BindingFlags.Public|BindingFlags.Instance,null,
            new[]{typeof(string),typeof(Il2CppSystem.Action<TagState>),typeof(bool)},null);
        var finish=typeof(WaterFeatureHelper).GetMethod(nameof(WaterFeatureHelper.OnLiquidContainerPourOutOf),BindingFlags.Public|BindingFlags.Static,null,
            new[]{typeof(GameItem)},null);
        return modify?.ReturnType!=typeof(GameItem)||finish?.ReturnType!=typeof(void)
            ?"exact ModifyTag/OnLiquidContainerPourOutOf signatures unavailable":null;
    }
    internal static void Apply(MachineLiquidTake take)
    {
        RequireAvailable();
        var item=NativeAccess.RequireItem(take.Container);var run=NativeAccess.RequireRun();
        if(!InventoryFreezeApi.IsFrozen(item)||!LiquidMetadata.Same(take.Before,Liquids.CaptureProduction(item)))
            throw Error("frozen unchanged source required");
        // 同容量、完整目录、每成分不增加；纯计算同时验证价值/品质的守恒方向。
        _=LiquidCompositionMath.Consumed(take.Before,take.After);
        var remaining=take.After.Contents.ToDictionary(p=>p.LiquidId,StringComparer.Ordinal);
        var writes=new List<(string Tag,int Before,int After)>();
        var catalog=Liquid.Liquids??throw Error("liquid catalog not ready");
        foreach(var part in take.Before.Contents) {
            var after=remaining[part.LiquidId].Parts;
            if(after==part.Parts)continue;
            var matches=catalog.ToArray().Where(l=>l is not null&&l.identifier==part.LiquidId).ToArray();
            if(matches.Length!=1||string.IsNullOrWhiteSpace(matches[0].currentPart))throw Error("ambiguous liquid tag mapping");
            var tag=matches[0].currentPart;
            if(item.GetTagReadonly(tag)?.GetInt()!=part.Parts)throw Error("native liquid tag differs before debit");
            writes.Add((tag,part.Parts,after));
        }
        if(writes.Select(w=>w.Tag).Distinct(StringComparer.Ordinal).Count()!=writes.Count)throw Error("aliased liquid tags");
        var pointer=item.Pointer;var instance=item.uniqueId;var parent=item.parentInventory?.Pointer;
        var state=item.state.Pointer;var dict=item.state.dict.Pointer;
        void Current() {
            if(!NativeAccess.SameRun(run)||item.Pointer!=pointer||item.uniqueId!=instance||item.parentInventory?.Pointer!=parent||
                item.state.Pointer!=state||item.state.dict.Pointer!=dict||!InventoryFreezeApi.IsFrozen(item))throw Error("source identity changed");
        }
        foreach(var write in writes) {
            Current();
            if(item.GetTagReadonly(write.Tag)?.GetInt()!=write.Before)throw Error("source component changed during debit");
            var calls=0;
            var callback=DelegateSupport.ConvertDelegate<Il2CppSystem.Action<TagState>>((Action<TagState>)(tag=>{
                if(++calls!=1||tag.GetInt()!=write.Before)throw Error("unexpected native tag callback");
                tag.SetInt(write.After);
            }))??throw Error("native tag callback conversion failed");
            try{item.ModifyTag(write.Tag,callback,true);}finally{GC.KeepAlive(callback);}
            Current();
            if(calls!=1||item.GetTagReadonly(write.Tag)?.GetInt()!=write.After)throw Error("component write readback differs");
        }
        if(writes.Count>0)WaterFeatureHelper.OnLiquidContainerPourOutOf(item);
        Current();
        if(!LiquidMetadata.SamePhysical(take.After,MachineLiquidSnapshot.From(Liquids.Capture(item))))
            throw Error("native physical contents differ after component debit");
        LiquidMetadata.Store(item,take.After);
    }
    private static InvalidOperationException Error(string reason)=>new("EZAPI_MACHINE_LIQUID_DEBIT: "+reason);
}

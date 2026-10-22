using Il2Cpp;
namespace EZapi;

public sealed record LiquidTransferResult(int PartsMoved,bool Indeterminate,string Status)
{
    public string? Error {get;init;}
    public bool Poured=>!Indeterminate&&PartsMoved>0&&Status is "Poured" or "PouredReadback";
}

/// <summary>当前周目的同步倒液；不缓存游戏对象，不自动保存或重试。1000 parts=1ml。</summary>
public static class LiquidTransferApi
{
    private static readonly HashSet<IntPtr> Active=new();

    internal static TransactionResult SetMetadata(GameItem item,MachineLiquidSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var run=NativeAccess.RequireRun();NativeAccess.RequireItem(item);var pointer=item.Pointer;
        TransactionResult Reject(string reason,TransactionStatus status=TransactionStatus.Rejected)=>new(status,"liquid.metadata",Array.Empty<string>(),Array.Empty<string>(),reason);
        if(!Active.Add(pointer))return Reject("EZAPI_LIQUID_REENTRANT",TransactionStatus.Reentrant);
        try {
            if(!Eligible(item,true)||!Eligible(item,false))return Reject("EZAPI_LIQUID_OWNERSHIP_OR_LOCK");
            // 显式设置允许作者修复已过期/损坏的自有元数据；原生数量必须仍精确匹配。
            var vessel=new Vessel(item,run);var before=MachineLiquidSnapshot.From(Liquids.Capture(item));
            var hadMetadata=item.state.dict.TryGetValue(LiquidMetadata.Tag,out var originalMetadata);
            var originalText=originalMetadata?.valueString;
            bool SameMetadata()=>item.state.dict.TryGetValue(LiquidMetadata.Tag,out var tag)==hadMetadata&&tag?.valueString==originalText;
            if(!LiquidMetadata.SamePhysical(before,snapshot))return Reject("EZAPI_LIQUID_METADATA: native contents differ; metadata cannot change quantities.");
            using var lease=InventoryFreezeApi.TryAcquire(new[]{item});
            if(lease is null)return Reject("EZAPI_LIQUID_FREEZE");
            vessel.Backup();
            bool Check()=>vessel.Current()&&Eligible(item,true,true)&&Eligible(item,false,true)&&InventoryFreezeApi.IsAvailable&&
                InventoryFreezeApi.IsFrozen(item)&&SameMetadata()&&LiquidMetadata.SamePhysical(before,MachineLiquidSnapshot.From(Liquids.Capture(item)));
            return Transactions.Run(new[]{new TransactionStep("liquid.metadata",Check,()=>{
                LiquidMetadata.Store(item,snapshot);
                return vessel.Current()&&Eligible(item,true,true)&&Eligible(item,false,true)&&InventoryFreezeApi.IsAvailable&&
                    InventoryFreezeApi.IsFrozen(item)&&LiquidMetadata.Same(snapshot,LiquidMetadata.Capture(item));
            },()=>vessel.Restore(before,true)&&SameMetadata())});
        }finally{Active.Remove(pointer);}
    }

    /// <summary>走原生TransferLiquid（含原生过滤规则），核对实际成分守恒；副作用无法证明恢复时报告Indeterminate。</summary>
    public static LiquidTransferResult Pour(GameItem source,GameItem target)=>Execute(source,target,null,null);
    /// <summary>限定最多parts；拒绝过滤器，冻结双容器，用原生Remove/AddLiquid写入并读回。失败仅在原周目/状态/身份未变时补偿。</summary>
    public static LiquidTransferResult Pour(GameItem source,GameItem target,int maximumParts,Func<bool>? stillAllowed=null)
    {
        if(maximumParts<1)throw new ArgumentOutOfRangeException(nameof(maximumParts),"EZAPI_LIQUID_AMOUNT: positive native parts required.");
        return Execute(source,target,maximumParts,stillAllowed);
    }

    private static LiquidTransferResult Execute(GameItem source,GameItem target,int? maximum,Func<bool>? allowed)
    {
        var run=NativeAccess.RequireRun();NativeAccess.RequireItem(source);NativeAccess.RequireItem(target);
        if(source.Pointer==target.Pointer)return Result(0,false,"Rejected","EZAPI_LIQUID_SAME_CONTAINER");
        var sourcePointer=source.Pointer;var targetPointer=target.Pointer;
        if(Active.Contains(sourcePointer)||Active.Contains(targetPointer))return Result(0,false,"Reentrant","EZAPI_LIQUID_REENTRANT");
        Active.Add(sourcePointer);Active.Add(targetPointer);
        var transactionEntered=false;
        try {
            if(!Eligible(source,true)||!Eligible(target,false))return Result(0,false,"Rejected","EZAPI_LIQUID_OWNERSHIP_OR_LOCK");
            var a=new Vessel(source,run);var b=new Vessel(target,run);
            var beforeA=LiquidMetadata.Capture(source);var beforeB=LiquidMetadata.Capture(target);
            var count=Math.Min(beforeA.TotalParts,beforeB.CapacityParts-beforeB.TotalParts);
            if(maximum is {} cap)count=Math.Min(count,cap);
            if(count==0)return Result(0,false,"EmptyOrFull");
            if(!maximum.HasValue)return Native(a,b,beforeA,beforeB,count);
            if(WaterHelper.GetFilterItem(source) is not null||WaterHelper.GetFilterItem(target) is not null)
                return Result(0,false,"Rejected","EZAPI_LIQUID_FILTER: bounded pour cannot compensate filter durability or reactions.");
            if(!a.Current()||!b.Current())return Result(0,false,"Rejected","EZAPI_LIQUID_STALE");
            using var lease=InventoryFreezeApi.TryAcquire(new[]{source,target});
            if(lease is null)return Result(0,false,"Unavailable","EZAPI_LIQUID_FREEZE: native freeze hooks unavailable or containers busy.");
            a.Backup();b.Backup();
            // 原生整数分配决定实际移出成分，不能用托管舍入假装等价。
            var nativeParts=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(beforeA.Contents.Select(p=>p.Parts).ToArray());
            var split=WaterHelper.SplitProportionally(nativeParts,count);
            if(split is null||split.Length!=beforeA.Contents.Count)return Result(0,false,"Rejected","EZAPI_LIQUID_SPLIT: native allocation unavailable.");
            var afterParts=new List<MachineLiquidPart>();long allocated=0;
            for(var i=0;i<split.Length;i++) {
                var take=split[i];var p=beforeA.Contents[i];
                if(take<0||take>p.Parts)return Result(0,false,"Rejected","EZAPI_LIQUID_SPLIT: invalid native allocation.");
                allocated+=take;afterParts.Add(LiquidCompositionMath.Remainder(p,p.Parts-take));
            }
            if(allocated!=count)return Result(0,false,"Rejected","EZAPI_LIQUID_SPLIT: native allocation total differs.");
            var afterA=new MachineLiquidSnapshot(beforeA.CapacityParts,afterParts);
            var moved=LiquidCompositionMath.Consumed(beforeA,afterA);
            var afterB=LiquidCompositionMath.Add(beforeB,moved);
            bool Check()
            {
                if(!a.Current()||!b.Current()||!Eligible(source,true,true)||!Eligible(target,false,true)||
                    !InventoryFreezeApi.IsAvailable||!InventoryFreezeApi.IsFrozen(source)||!InventoryFreezeApi.IsFrozen(target))return false;
                if(!LiquidMetadata.Same(beforeA,LiquidMetadata.Capture(source))||!LiquidMetadata.Same(beforeB,LiquidMetadata.Capture(target)))return false;
                if(allowed is not null&&!allowed())return false;
                // 资格回调只能查询；返回true也必须再核对两件实例和全部输入。
                return a.Current()&&b.Current()&&Eligible(source,true,true)&&Eligible(target,false,true)&&
                    InventoryFreezeApi.IsAvailable&&InventoryFreezeApi.IsFrozen(source)&&InventoryFreezeApi.IsFrozen(target)&&
                    LiquidMetadata.Same(beforeA,LiquidMetadata.Capture(source))&&LiquidMetadata.Same(beforeB,LiquidMetadata.Capture(target));
            }
            // 一个补偿步骤同时拥有两件容器，源写入期间回调即使改动目标，也不会漏掉目标恢复。
            transactionEntered=true;
            var result=Transactions.Run(new[]{new TransactionStep("liquid.pour",Check,()=>{
                WaterHelper.Remove(source,count);
                if(!a.Current()||!b.Current()||!LiquidMetadata.SamePhysical(afterA,MachineLiquidSnapshot.From(Liquids.Capture(source)))||
                    !LiquidMetadata.Same(beforeB,LiquidMetadata.Capture(target)))return false;
                LiquidMetadata.Store(source,afterA);
                if(allowed is not null&&!allowed())return false;
                if(!a.Current()||!b.Current()||!LiquidMetadata.Same(afterA,LiquidMetadata.Capture(source))||
                    !LiquidMetadata.Same(beforeB,LiquidMetadata.Capture(target)))return false;
                foreach(var part in moved) {
                    if(!a.Current()||!b.Current())return false;
                    WaterHelper.AddLiquid(target,part.LiquidId,part.Parts);
                }
                if(!a.Current()||!b.Current()||!LiquidMetadata.SamePhysical(afterB,MachineLiquidSnapshot.From(Liquids.Capture(target))))return false;
                LiquidMetadata.Store(target,afterB);
                if(allowed is not null&&!allowed())return false;
                return a.Current()&&b.Current()&&Eligible(source,true,true)&&Eligible(target,false,true)&&
                    InventoryFreezeApi.IsAvailable&&InventoryFreezeApi.IsFrozen(source)&&InventoryFreezeApi.IsFrozen(target)&&
                    LiquidMetadata.Same(afterA,LiquidMetadata.Capture(source))&&LiquidMetadata.Same(afterB,LiquidMetadata.Capture(target));
            },()=>{
                // 一个容器恢复抛错也必须尝试另一个，不能让目标异常跳过源补偿。
                var restoredA=false;var restoredB=false;
                try{restoredB=b.Restore(beforeB);}finally{restoredA=a.Restore(beforeA);}
                return restoredA&&restoredB;
            })});
            if(result.Status==TransactionStatus.Rejected&&(!a.Current()||!b.Current()||
                !LiquidMetadata.Same(beforeA,LiquidMetadata.Capture(source))||!LiquidMetadata.Same(beforeB,LiquidMetadata.Capture(target))))
                return Result(0,true,"Indeterminate","EZAPI_LIQUID_PREDICATE_MUTATION: input changed before write; no compensating overwrite attempted.");
            return Result(result.Committed?count:0,result.Status==TransactionStatus.RecoveryFailed,
                result.Committed?"Poured":result.Status.ToString(),result.Error??(result.RecoveryFailures.Count>0?string.Join("; ",result.RecoveryFailures):null));
        }catch(Exception e){return Result(0,transactionEntered,transactionEntered?"Indeterminate":"Rejected",e.GetType().Name+": "+e.Message);}
        finally{Active.Remove(sourcePointer);Active.Remove(targetPointer);}
    }

    private static LiquidTransferResult Native(Vessel source,Vessel target,MachineLiquidSnapshot beforeA,MachineLiquidSnapshot beforeB,int maximum)
    {
        var attempted=false;string? error=null;
        try {
            // 原生路径保留所有原生过滤/反应，不能把过滤造成的损耗冒充守恒成功。
            if(!source.Current()||!target.Current())return Result(0,false,"Rejected","EZAPI_LIQUID_STALE");
            attempted=true;
            try{WaterHelper.TransferLiquid(source.Item,target.Item);}catch(Exception e){error=e.GetType().Name+": "+e.Message;}
            if(!source.Current()||!target.Current())return Result(0,true,"Indeterminate","EZAPI_LIQUID_STALE: native write changed identity.");
            var actualA=MachineLiquidSnapshot.From(Liquids.Capture(source.Item));var actualB=MachineLiquidSnapshot.From(Liquids.Capture(target.Item));
            var moved=beforeA.TotalParts-actualA.TotalParts;
            if(beforeA.CapacityParts!=actualA.CapacityParts||beforeB.CapacityParts!=actualB.CapacityParts||moved<0||moved>maximum||actualB.TotalParts-beforeB.TotalParts!=moved)
                return Result(0,true,"Indeterminate","EZAPI_LIQUID_READBACK: capacity or volume mismatch; inspect native filtering/reaction side effects.");
            var remaining=beforeA.Contents.Select(p=>LiquidCompositionMath.Remainder(p,
                actualA.Contents.FirstOrDefault(q=>q.LiquidId==p.LiquidId)?.Parts??throw new InvalidOperationException("catalog changed"))).ToArray();
            var afterA=new MachineLiquidSnapshot(beforeA.CapacityParts,remaining);
            var portions=LiquidCompositionMath.Consumed(beforeA,afterA);var afterB=LiquidCompositionMath.Add(beforeB,portions);
            if(!LiquidMetadata.SamePhysical(actualA,afterA)||!LiquidMetadata.SamePhysical(actualB,afterB))
                return Result(0,true,"Indeterminate","EZAPI_LIQUID_READBACK: components are not conserved.");
            if(moved==0)return Result(0,true,"Indeterminate",error??"EZAPI_LIQUID_NO_MOVE: native call ran; filter/reaction side effects are not proven unchanged.");
            if(beforeA.Contents.Concat(beforeB.Contents).Any(p=>p.Value.HasValue||p.QualityBasis.HasValue)) {
                LiquidMetadata.Store(source.Item,afterA);
                if(!source.Current()||!target.Current())return Result(0,true,"Indeterminate","EZAPI_LIQUID_STALE");
                LiquidMetadata.Store(target.Item,afterB);
            }
            if(!source.Current()||!target.Current()||!LiquidMetadata.Same(afterA,LiquidMetadata.Capture(source.Item))||!LiquidMetadata.Same(afterB,LiquidMetadata.Capture(target.Item)))
                return Result(0,true,"Indeterminate","EZAPI_LIQUID_READBACK: metadata or identity changed.");
            return Result(moved,false,error is null?"Poured":"PouredReadback",error);
        }catch(Exception e){return Result(0,attempted,attempted?"Indeterminate":"Rejected",e.GetType().Name+": "+e.Message);}
    }

    private static bool Eligible(GameItem item,bool source,bool held=false)=>item.unitCount==1&&GeneralHelper.IsItemOwned(item)&&
        item.parentInventory is {} parent&&parent.Pointer!=IntPtr.Zero&&!(source?parent.IsRemoveLocked():parent.IsInsertLocked())&&
        !InventoryApi.IsItemDragActive&&(held||!InventoryFreezeApi.IsFrozen(item));
    private static LiquidTransferResult Result(int moved,bool uncertain,string status,string? error=null)=>new(moved,uncertain,status){Error=error};

    private sealed class Vessel
    {
        internal readonly GameItem Item;
        private readonly GameRunIdentity _run;
        private readonly IntPtr _pointer,_parent,_state,_dict;
        private readonly int _instance;private readonly string _id;
        private NativeTagBackup? _backup;private readonly long _value;
        internal Vessel(GameItem item,GameRunIdentity run)
        {
            Item=item;_run=run;_pointer=item.Pointer;_parent=item.parentInventory!.Pointer;
            _instance=item.uniqueId;_id=item.identifier;_state=item.state?.Pointer??IntPtr.Zero;_dict=item.state?.dict?.Pointer??IntPtr.Zero;_value=item.unitValue;
        }
        internal bool Current()=>NativeAccess.SameRun(_run)&&Item.Pointer==_pointer&&Item.uniqueId==_instance&&Item.identifier==_id&&
            Item.unitCount==1&&Item.parentInventory?.Pointer==_parent&&GeneralHelper.IsItemOwned(Item)&&
            Item.state?.Pointer==_state&&Item.state?.dict?.Pointer==_dict;
        internal void Backup()
        {
            if(!Current()||Item.state is null)throw new InvalidOperationException("EZAPI_LIQUID_STALE");
            _backup=NativeTagBackup.Capture(Item.state);
        }
        internal bool Restore(MachineLiquidSnapshot expected,bool physicalOnly=false)
        {
            if(_backup is null||!Current())return false;
            // 保留原state/dict对象身份，仅恢复标签集合；不跨场景或覆盖外部替换的状态。
            _backup.Restore(Item.state);
            Item.SyncModifiedState();if(!Current())return false;Item.unitValue=_value;
            return Current()&&Item.unitValue==_value&&(physicalOnly
                ?LiquidMetadata.SamePhysical(expected,MachineLiquidSnapshot.From(Liquids.Capture(Item)))
                :LiquidMetadata.Same(expected,LiquidMetadata.Capture(Item)));
        }
    }
}

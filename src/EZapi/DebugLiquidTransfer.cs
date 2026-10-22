using Il2Cpp;
namespace EZapi;

/// <summary>先预览再手动执行≤1ml；只缓存身份、文字和托管快照，换场景清空，不自动保存。</summary>
internal static class DebugLiquidTransfer
{
    private sealed record Selection(GameRunIdentity Run,int SourceInstance,string SourceId,string SourceName,
        int TargetInstance,string TargetId,string TargetName,MachineLiquidSnapshot BeforeSource,MachineLiquidSnapshot BeforeTarget);
    private static Selection? _selection;
    internal static object? LastReport {get;private set;}
    internal static void Clear(){_selection=null;LastReport=null;}
    internal static string Preview(GameRunIdentity? expected)
    {
        _selection=null;
        try {
            if(expected is null||!NativeAccess.SameRun(expected))return "请在测试档店铺重新预览倒液。";
            var candidates=Find(expected);GameItem? source=null,target=null;
            foreach(var item in candidates) {
                if(!Eligible(item)||WaterHelper.GetFilterItem(item) is not null)continue;
                var snapshot=Liquids.CaptureProduction(item);
                if(source is null&&snapshot.TotalParts>0){source=item;continue;}
                if(target is null&&snapshot.CapacityParts>snapshot.TotalParts)target=item;
            }
            if(source is null||target is null)return "需要两件已拥有的单件液体容器：一件有液体、另一件有空位；不含过滤器。";
            _selection=new(expected,source.uniqueId,source.identifier,source.name,target.uniqueId,target.identifier,target.name,
                Liquids.CaptureProduction(source),Liquids.CaptureProduction(target));
            LastReport=new{state="preview",selection=_selection,maximumParts=1000,scope="preview only; execution requires separate button; no save"};
            EZapiBootstrap.WriteDiagnostics();
            return $"倒液预览：{source.name} #{source.uniqueId} → {target.name} #{target.uniqueId}；执行按钮最多转移1ml，不自动保存。";
        }catch(Exception e){return Error(e);}
    }
    internal static string Execute(GameRunIdentity? expected)
    {
        var selection=_selection;_selection=null; // 一次预览只允许一次提交，不因失败重试。
        try {
            if(selection is null||expected!=selection.Run||!NativeAccess.SameRun(selection.Run))return "预览已失效，请重新预览倒液。";
            var items=Find(selection.Run);
            var source=items.SingleOrDefault(p=>p.uniqueId==selection.SourceInstance&&p.identifier==selection.SourceId);
            var target=items.SingleOrDefault(p=>p.uniqueId==selection.TargetInstance&&p.identifier==selection.TargetId);
            if(source is null||target is null||!Eligible(source)||!Eligible(target)||
                !LiquidMetadata.Same(selection.BeforeSource,Liquids.CaptureProduction(source))||
                !LiquidMetadata.Same(selection.BeforeTarget,Liquids.CaptureProduction(target)))return "容器或内容已变化，请重新预览；本次没有倒液。";
            var result=LiquidTransferApi.Pour(source,target,1000);
            object Read(GameItem item){try{return Liquids.CaptureProduction(item);}catch(Exception e){return new{error=e.GetType().Name+": "+e.Message};}}
            LastReport=new{state="executed",selection,result,afterSource=Read(source),afterTarget=Read(target),noAutomaticSave=true};
            EZapiBootstrap.WriteDiagnostics();
            return $"倒液 {result.Status}：{result.PartsMoved} parts（1000=1ml）；{result.Error??"已导出前后成分"}";
        }catch(Exception e){return Error(e);}
    }
    private static GameItem[] Find(GameRunIdentity run)
    {
        if(InventoryApi.IsItemDragActive)throw new InvalidOperationException("请先放下正在拖动的物品。");
        var items=GameSession.Resolve(run).FindAllItem(true);
        if(items is null||items.Count>4096)throw new InvalidOperationException("物品未就绪或超过4096件读取上限。");
        var result=new List<GameItem>();var seen=new HashSet<IntPtr>();
        for(var i=0;i<items.Count;i++) {
            var item=items[i];if(item is not null&&item.Pointer!=IntPtr.Zero&&seen.Add(item.Pointer)&&item.unitCount>0)result.Add(item);
        }
        return result.ToArray();
    }
    private static bool Eligible(GameItem item)=>item.unitCount==1&&item.parentInventory is {} parent&&
        !parent.IsRemoveLocked()&&!parent.IsInsertLocked()&&GeneralHelper.IsItemOwned(item)&&!InventoryFreezeApi.IsFrozen(item)&&Liquids.IsContainer(item);
    private static string Error(Exception e)
    {
        LastReport=new{state="error",error=e.GetType().Name+": "+e.Message};EZapiBootstrap.WriteDiagnostics();
        return "倒液检查："+e.Message;
    }
}

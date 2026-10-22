using MelonLoader;
namespace EZapi;

/// <summary>一次点击排队多项检查，每帧执行一组；只保存托管报告，不跨帧保留游戏对象。</summary>
internal static class DebugBatch
{
    private sealed record Entry(string Group,string Status,string Message);
    private static readonly List<Entry> Entries=new();
    private static GameRunIdentity? _run;
    private static int _next;
    private static DateTimeOffset _started;
    private static string _state="idle";
    internal static bool Running=>_state=="running";
    internal static object Report()=>new{state=_state,startedAt=_started,run=_run,completedGroups=Entries.Count,
        entries=Entries.ToArray(),scope="manual main-thread batches; temporary factory items and read-only observations; no player item delivery, movement, consumption or save",
        pendingGameplay=new[]{"real drag/drop and container window layout","food/liquid consumption","sleep and return lifecycle observations","save and new load persistence","native custom effect registration and random exclusion"}};
    internal static string Start(GameRunIdentity? expected)
    {
        try{return StartCore(expected);}
        catch(Exception e){return "批量检查未开始："+e.GetType().Name+": "+e.Message;}
    }
    private static string StartCore(GameRunIdentity? expected)
    {
        if(Running)return "批量检查正在运行，结果会自动导出。";
        if(expected is null||!NativeAccess.SameRun(expected))return "请先进入测试档店铺，再启动批量检查。";
        if(!VisibleStoreInventory.IsReady)
            return "进入店铺并显示主库存后，才可以启动批量检查。";
        if(ItemRegistry.AllDefinitions().Count>64)return "批量检查最多64个物品定义；请使用单独的测试内容包。";
        if(InventoryApi.IsItemDragActive)return "请先放下正在拖拽的物品。";
        Entries.Clear();_run=expected;_next=0;_started=DateTimeOffset.Now;_state="running";
        EZapiBootstrap.WriteDiagnostics();
        return "批量检查已开始：物品/容器、库存、资源、计价/效果状态、阶段记录；结束自动导出。";
    }
    internal static void Update()
    {
        if(!Running)return;
        try{UpdateCore();}
        catch(Exception e){_state="error";Entries.Add(new("session","error",e.GetType().Name+": "+e.Message));EZapiBootstrap.WriteDiagnostics();MelonLogger.Error("[EZapi] Batch session: "+e);}
    }
    private static void UpdateCore()
    {
        if(_run is null||!NativeAccess.SameRun(_run)){Cancel("周目或场景变化，尚未执行的组已取消。");return;}
        // 拖动期间停止本批次，避免临时物品/tooltip检查与玩家输入交错。
        if(InventoryApi.IsItemDragActive){Cancel("玩家开始拖动，尚未执行的组已取消。");return;}
        var group=_next++;
        var name=group switch{0=>"lifecycle",1=>"items-containers-tooltip",2=>"inventory",3=>"resources",_=>"production-effects"};
        try {
            var message=group switch{
                0=>DebugLifecycle.Recording?"阶段记录已经开启。":DebugLifecycle.Toggle(),
                1=>ItemDiagnostics.Run(),
                2=>DebugInventory.Run(_run),
                3=>DebugResources.Run(_run),
                _=>DebugProduction.Run()};
            // 这里只证明检查函数执行过；领域结果必须读取各自报告，不用正常返回伪装PASS。
            Entries.Add(new(name,"collected",message));
        } catch(Exception e){Entries.Add(new(name,"error",e.GetType().Name+": "+e.Message));MelonLogger.Error("[EZapi] Batch "+name+": "+e);}
        if(_next>=5){_state="collected";MelonLogger.Msg("[EZapi] Batch collected 5 groups; inspect individual reports. Gameplay/save checks remain pending.");}
        EZapiBootstrap.WriteDiagnostics();
    }
    internal static void SceneChanged(){if(Running)Cancel("场景切换，尚未执行的组已取消。");}
    private static void Cancel(string reason){_state="cancelled";Entries.Add(new("session","cancelled",reason));EZapiBootstrap.WriteDiagnostics();}
    internal static void Clear(){_state="idle";_run=null;_next=0;Entries.Clear();}
}

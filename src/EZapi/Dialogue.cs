using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
namespace EZapi;

/// <summary>独立原生对话。Create 可离线调用；展示、关闭和状态读取仅限已初始化的游戏主线程。</summary>
public static class DialogueApi
{
    private static readonly NativeHookGroup Hooks=new("ezapi.presentation.dialogue");
    private static NativeGraph? _graph;
    private static long _generation;
    private static bool _started,_busy,_fault;
    private static int _selectDepth;
    private static Permit? _permit;
    private static readonly Queue<object> Resets=new();
    private static readonly Queue<object> Trace=new();
    private static void Event(string cause,object? data=null){Trace.Enqueue(new{cause,data,at=DateTimeOffset.Now});while(Trace.Count>30)Trace.Dequeue();}
    private sealed record Permit(NativeGraph Graph,long Source,int Index);
    internal static void Start()=>_started=true;
    public static DialogueScript Create(string ownerId,DialogueDefinition definition)=>DialogueContract.Freeze(ownerId,definition);
    public static bool IsAvailable=>_started&&GameSession.OnThread&&!_fault&&Hooks.Snapshot().Ready;
    public static NativeHookStatus GetRuntimeStatus(){GameSession.RequireThread();return Hooks.Snapshot();}
    /// <summary>不覆盖原版或其它作者对话。requestId 是本次展示请求名；不提供保存、支付或业务回滚。</summary>
    public static PresentationResult Open(string ownerId,string requestId,string runId,int slotId,DialogueScript script)
    {
        if(!GameSession.OnThread||!IsAvailable)return new(PresentationStatus.Unavailable,"EZAPI_DIALOGUE_UNAVAILABLE");
        if(script is null||ownerId!=script.OwnerId||!PresentationRules.Owned(ownerId,requestId)||!PresentationRules.Text(runId,160)||string.IsNullOrEmpty(runId)||slotId<0)
            return new(PresentationStatus.Invalid,"EZAPI_DIALOGUE_REQUEST");
        if(_busy||_selectDepth!=0)return new(PresentationStatus.Conflict,"EZAPI_DIALOGUE_REENTRY");
        _busy=true;
        try {
            MaintainCore();
            if(!GameSession.TryResolve(null,out _))return new(PresentationStatus.Unavailable,"EZAPI_DIALOGUE_RUN");
            var identity=GameSession.Identity!;
            if(identity.RunId!=runId||identity.SaveSlotId!=slotId)return new(PresentationStatus.Conflict,"EZAPI_DIALOGUE_SCOPE");
            var manager=DialogUIManager.Instance;
            if(manager==null||manager.dialogChoiceBubbles is null)return new(PresentationStatus.Unavailable,"EZAPI_DIALOGUE_UI");
            if(_graph is not null||manager.currentDialog is not null||manager.displayingUncompletedDialog is not null||manager.isConversationActiveFlag!=0||
                manager.isSecMonologuePlaying||manager.isEndPending||manager.isAutoAdvancePending||manager.scheduledEndActionLines?.Count>0||InventoryApi.IsItemDragActive)
                return new(PresentationStatus.Conflict,"EZAPI_DIALOGUE_OCCUPIED");
            var count=manager.dialogChoiceBubbles.Count;
            if(count<3||count>128)return new(PresentationStatus.Unavailable,"EZAPI_DIALOGUE_BUTTONS");
            // 发布句柄早于构建：工厂/本地回调重入或部分失败时仍有唯一清理所有者。
            var graph=new NativeGraph(script,++_generation,identity,requestId,manager.Pointer.ToInt64(),count);_graph=graph;
            graph.Build((source,index,edge)=>Confirm(graph,source,index,edge),
                (source,index,edge)=>Leave(graph,source,index,edge),
                (source,index,edge)=>Continue(graph,source,index,edge));
            if(!CurrentScope(graph)||!graph.Execution.Active)return new(PresentationStatus.Indeterminate,"EZAPI_DIALOGUE_INTERRUPTED");
            graph.AcquireMode(manager);
            Event("Start",new{mode=manager.isSecMonologuePlaying});
            var accepted=manager.StartDialogue(graph.Pages[graph.Plan.Root],false);
            if(accepted&&graph.Owns(manager.currentDialog)&&graph.Execution.Active&&CurrentScope(graph))
                return new(PresentationStatus.Opened);
            graph.Execution.Invalidate();MaintainCore();
            return new(PresentationStatus.Indeterminate,"EZAPI_DIALOGUE_START_READBACK");
        }catch(Exception e){_graph?.Execution.Invalidate();Warn("EZAPI_DIALOGUE_OPEN",e);return new(PresentationStatus.Indeterminate,"EZAPI_DIALOGUE_OPEN");}
        finally{_busy=false;Maintain();}
    }
    /// <summary>先失效业务回调，再中断本作者的实际原生对话；外部对话接管后只清理自有图。</summary>
    public static PresentationResult Close(string ownerId,string requestId)
    {
        if(!GameSession.OnThread)return new(PresentationStatus.Unavailable,"EZAPI_DIALOGUE_THREAD");
        if(!PresentationRules.Owned(ownerId,requestId))return new(PresentationStatus.Invalid,"EZAPI_DIALOGUE_REQUEST");
        if(_busy||_selectDepth!=0)return new(PresentationStatus.Conflict,"EZAPI_DIALOGUE_REENTRY");
        if(_graph is null)return new(PresentationStatus.Closed);
        if(_graph.Execution.Script.OwnerId!=ownerId||_graph.Execution.Request!=requestId)return new(PresentationStatus.Conflict,"EZAPI_DIALOGUE_OWNER");
        _busy=true;
        try{ResetCore("Close");return new(_graph is null?PresentationStatus.Closed:PresentationStatus.Indeterminate,_graph is null?null:"EZAPI_DIALOGUE_RETAINED");}
        catch(Exception e){Warn("EZAPI_DIALOGUE_CLOSE",e);return new(PresentationStatus.Indeterminate,"EZAPI_DIALOGUE_CLOSE");}
        finally{_busy=false;}
    }
    internal static void DirectoryReady()
    {
        if(!_started)return;
        Hooks.Install(typeof(DialogueApi),
            new(typeof(DialogUIManager),nameof(DialogUIManager.StartDialogue),typeof(bool),new[]{typeof(Dialogue),typeof(bool)},Prefix:nameof(BeforeStart),Finalizer:nameof(AfterStart)),
            new(typeof(DialogUIManager),nameof(DialogUIManager.DisplayClientText),typeof(void),new[]{typeof(Dialogue)},Postfix:nameof(AfterClientText)),
            new(typeof(DialogUIManager),nameof(DialogUIManager.SelectChoice),typeof(void),new[]{typeof(int)},Prefix:nameof(BeforeSelect),Finalizer:nameof(AfterSelect)),
            new(typeof(PlayerStore),nameof(PlayerStore.LoadGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeLoad)),
            new(typeof(PlayerStore),nameof(PlayerStore.StartNewGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeNew)),
            new(typeof(MainMenuUIController),nameof(MainMenuUIController.Awake),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeMenu)));
    }
    // 原生协程含内联推进逻辑，模式必须覆盖本次展示寿命；外部 StartDialogue 在原方法之前归还模式。
    private static bool BeforeStart(DialogUIManager __instance,Dialogue __0,out object? __state)
    {
        __state=null;
        if(!_started||!GameSession.OnThread||!Hooks.Ready||_graph is null||__instance.Pointer.ToInt64()!=_graph.Manager||_graph.Owns(__0))return true;
        // 原生 SelectChoice 在作者回调后还会重新读取 currentDialog.choices[index]。
        // 在这个自有调用栈内替换 currentDialog 会让原生读取外部图的错误索引。
        // 调用方收到 false，可在下一帧显式重新请求；框架不自动重放外部操作。
        if(_selectDepth!=0&&_graph.Owns(__instance.currentDialog))return false;
        Event("ForeignStart",new{title=__0?.title,text=__0?.plainText});
        __state=_graph;_graph.Suspended=true;_graph.RestoreMode(__instance);_permit=null;return true;
    }
    private static Exception? AfterStart(DialogUIManager __instance,bool __result,Exception? __exception,object? __state)
    {
        if(__state is not NativeGraph graph)return __exception;
        try {
            graph.Suspended=false;
            // 被优先级规则拒绝的外部请求没有取得界面，不取消仍在展示的原对话。
            if(__exception is null&&!__result&&_graph==graph&&graph.Execution.Active&&CurrentScope(graph)&&__instance!=null&&graph.Owns(__instance.currentDialog))graph.AcquireMode(__instance);
            else graph.Execution.Invalidate();
        }catch(Exception e){graph.Execution.Invalidate();Warn("EZAPI_DIALOGUE_TAKEOVER",e);}
        return __exception;
    }
    private static void AfterClientText(DialogUIManager __instance,Dialogue __0,bool __runOriginal)
    {
        if(!_started||!GameSession.OnThread||!Hooks.Ready||!__runOriginal)return;
        try {
            var graph=_graph;
            if(graph is null||!graph.Execution.Active||!CurrentScope(graph)||!graph.Owns(__0)||__instance.Pointer.ToInt64()!=graph.Manager)return;
            if(__instance.clientNameTMP!=null)__instance.clientNameTMP.text=graph.Execution.Script.Definition.Speaker;
            if(__instance.clientNameBubble!=null)__instance.clientNameBubble.SetActive(true);
        }catch(Exception e){Warn("EZAPI_DIALOGUE_SPEAKER",e);}
    }
    // 仅在确切 SelectChoice 调用栈内给当前原生源节点/索引一次执行许可。
    // 直接 Invoke 旧 delegate、嵌套选择、重放确认均不能取得许可。
    private static void BeforeSelect(DialogUIManager __instance,int __0,bool __runOriginal,out bool __state)
    {
        __state=false;
        if(!_started||!GameSession.OnThread||!Hooks.Ready)return;
        __state=true;_selectDepth++;
        if(_selectDepth!=1){_permit=null;return;}
        try {
            var graph=_graph;var source=__instance.currentDialog;
            if(__runOriginal&&graph is not null&&graph.Execution.Active&&CurrentScope(graph)&&__instance.Pointer.ToInt64()==graph.Manager&&
                source is not null&&graph.Owns(source)&&graph.Edges.ContainsKey((source.Pointer.ToInt64(),__0))&&source.IsChoiceEnabledAt(__0))
                _permit=new(graph,source.Pointer.ToInt64(),__0);
        }catch(Exception e){_permit=null;Warn("EZAPI_DIALOGUE_SELECT",e);}
    }
    private static Exception? AfterSelect(Exception? __exception,bool __state)
    {
        if(__state){_permit=null;_selectDepth=Math.Max(0,_selectDepth-1);}
        return __exception;
    }
    private static void Confirm(NativeGraph graph,long source,int index,DialogueEdge edge)
    {
        try {
            var permit=_permit;
            if(permit is null||permit.Graph!=graph||permit.Source!=source||permit.Index!=index||_selectDepth!=1||_graph!=graph)return;
            _permit=null; // 消费许可，作者重入和同一原生委托的重复调用都不能再执行。
            bool Valid()=>_started&&Hooks.Ready&&graph.Execution.Active&&!graph.Suspended&&_graph==graph&&CurrentScope(graph)&&DialogUIManager.Instance!=null&&DialogUIManager.Instance.Pointer.ToInt64()==graph.Manager&&graph.Owns(DialogUIManager.Instance.currentDialog);
            var reply=graph.Execution.Commit(edge.Choice,Valid,message=>MelonLogger.Warning("[EZapi] "+message));
            if(Valid())graph.Pages[edge.Target].SetText(graph.Execution.Script.Definition.Speaker,reply);
        }catch(Exception e){graph.Execution.Invalidate();Warn("EZAPI_DIALOGUE_CONFIRM",e);}
    }
    private static void Continue(NativeGraph graph,long source,int index,DialogueEdge edge)
    {
        try{
            var permit=_permit;
            if(permit is null||permit.Graph!=graph||permit.Source!=source||permit.Index!=index||_selectDepth!=1||_graph!=graph)return;
            _permit=null;
            bool Valid()=>_started&&Hooks.Ready&&graph.Execution.Active&&!graph.Suspended&&_graph==graph&&CurrentScope(graph)&&
                DialogUIManager.Instance!=null&&DialogUIManager.Instance.Pointer.ToInt64()==graph.Manager&&graph.Owns(DialogUIManager.Instance.currentDialog);
            graph.Execution.Continue(edge.Choice,Valid,message=>MelonLogger.Warning("[EZapi] "+message));
        }catch(Exception e){graph.Execution.Invalidate();Warn("EZAPI_DIALOGUE_CONTINUE",e);}
    }
    private static void Leave(NativeGraph graph,long source,int index,DialogueEdge edge)
    {
        try{
            var permit=_permit;
            if(permit is null||permit.Graph!=graph||permit.Source!=source||permit.Index!=index||_selectDepth!=1||_graph!=graph)return;
            _permit=null;
            bool Valid()=>_started&&Hooks.Ready&&graph.Execution.Active&&!graph.Suspended&&_graph==graph&&CurrentScope(graph)&&
                DialogUIManager.Instance!=null&&DialogUIManager.Instance.Pointer.ToInt64()==graph.Manager&&graph.Owns(DialogUIManager.Instance.currentDialog);
            graph.Execution.Leave(Valid,message=>MelonLogger.Warning("[EZapi] "+message));
        }catch(Exception e){graph.Execution.Invalidate();Warn("EZAPI_DIALOGUE_LEAVE",e);}
    }

    internal static bool IsActive(string ownerId,string requestId)
    {
        var graph=_graph;
        return GameSession.OnThread&&_started&&graph is not null&&!graph.Suspended&&graph.Execution.Active&&
            graph.Execution.Script.OwnerId==ownerId&&graph.Execution.Request==requestId&&CurrentScope(graph);
    }

    internal static bool IsChoiceVisibleById(string ownerId,string requestId,string choiceId)
        =>TryVisibleChoice(ownerId,requestId,choiceId,out _,out _);

    internal static bool TrySelectChoiceById(string ownerId,string requestId,string choiceId)
    {
        if(!TryVisibleChoice(ownerId,requestId,choiceId,out var manager,out var index)||manager is null)return false;
        try{manager.SelectChoice(index);return true;}
        catch(Exception e){Warn("EZAPI_DIALOGUE_AUTO_SELECT",e);return false;}
    }

    private static bool TryVisibleChoice(string ownerId,string requestId,string choiceId,out DialogUIManager? manager,out int index)
    {
        manager=null;index=-1;
        if(!GameSession.OnThread||!_started||_busy||_selectDepth!=0||!Hooks.Ready||InventoryApi.IsItemDragActive)return false;
        var graph=_graph;
        if(graph is null||!graph.Execution.Active||graph.Suspended||graph.Execution.Script.OwnerId!=ownerId||
            graph.Execution.Request!=requestId||!CurrentScope(graph))return false;
        var current=DialogUIManager.Instance;
        if(current is null||current.Pointer.ToInt64()!=graph.Manager||current.currentDialog is not { } source||
            !graph.Owns(source)||!graph.TryFindChoice(source,choiceId,out index)||!source.IsChoiceEnabledAt(index)||
            current.dialogChoiceBubbles is null||index<0||index>=current.dialogChoiceBubbles.Count)return false;
        var bubble=current.dialogChoiceBubbles[index];
        if(bubble is null||bubble.gameObject is null||!bubble.gameObject.activeInHierarchy)return false;
        var handnote=HandnoteUIManager.Instance;
        if(handnote!=null&&handnote.panel!=null&&handnote.panel.activeInHierarchy)return false;
        var calendar=AdvCalendarUIManager.Instance;
        if(calendar!=null&&calendar.UIPanel!=null&&calendar.UIPanel.activeInHierarchy)return false;
        manager=current;
        return true;
    }

    private static bool CurrentScope(NativeGraph graph)=>GameSession.TryResolve(graph.Identity.Epoch,out _)&&GameSession.Identity==graph.Identity;
    internal static void Maintain()
    {
        if(!_started||!GameSession.OnThread||_busy||_selectDepth!=0||_graph?.Execution.Executing==true)return;
        try{MaintainCore();}catch(Exception e){if(_graph is not null&&!_graph.InspectFault){_graph.InspectFault=true;Warn("EZAPI_DIALOGUE_INSPECT_RETAINED",e);}}
    }
    private static void MaintainCore()
    {
        var graph=_graph;if(graph is null||graph.ReleaseFault||graph.Suspended)return;
        var manager=DialogUIManager.Instance;
        var same=manager!=null&&manager.Pointer.ToInt64()==graph.Manager;
        var owned=same&&graph.Owns(manager!.currentDialog);
        if(same&&graph.Execution.Active)graph.LastNativeState=new{mode=manager!.isSecMonologuePlaying,current=manager.currentDialog?.plainText,owned,pending=manager.isAutoAdvancePending,coroutines=manager.activeCoroutines?.Count};
        // activeCoroutines 是已发放的句柄列表，完成后仍可保留，Count 不能代表协程仍在执行。
        // 真正尚待推进/结束的原生标记与当前/延迟节点链共同构成引用屏障。
        var pending=same&&(manager!.isAutoAdvancePending||manager.isEndPending);
        if(!CurrentScope(graph)||!Hooks.Ready||same&&manager!.currentDialog is not null&&!owned||!same){if(graph.Execution.Active)Event("Invalidate",graph.LastNativeState);graph.Execution.Invalidate();}
        if(!graph.Execution.Active&&same)graph.RestoreMode(manager!);
        if(!graph.Execution.Active&&owned){manager!.InterruptDialog();owned=graph.Owns(manager.currentDialog);pending=manager.isAutoAdvancePending||manager.isEndPending;}
        if(_selectDepth!=0||graph.Execution.Executing)return;
        if(same&&graph.ReferencedBy(manager!))return;
        if(pending)return;
        graph.Execution.Invalidate();
        if(same)graph.RestoreMode(manager!);
        try{graph.Release();_graph=null;}
        catch(Exception e){graph.ReleaseFault=true;_fault=true;Warn("EZAPI_DIALOGUE_RELEASE_BLOCKED",e);}
    }
    private static void BeforeLoad()=>Reset("LoadGame");
    private static void BeforeNew()=>Reset("StartNewGame");
    private static void BeforeMenu()=>Reset("MainMenu.Awake");
    private static void Reset(string cause)
    {
        if(!_started||!GameSession.OnThread)return;
        try{ResetCore(cause);}catch(Exception e){Warn("EZAPI_DIALOGUE_RESET_RETAINED",e);}
    }
    private static void ResetCore(string cause)
    {
        _permit=null;var before=_graph?.Items.Count??0;_graph?.Execution.Invalidate();
        // 回调栈内只撤销许可，不销毁仍被 SelectChoice 使用的图；下一帧再清理。
        if(_selectDepth==0&&_graph?.Execution.Executing!=true)MaintainCore();
        Resets.Enqueue(new{cause,before,after=_graph?.Items.Count??0,at=DateTimeOffset.Now});while(Resets.Count>16)Resets.Dequeue();
    }
    internal static void SceneChanged()=>Reset("SceneUnloaded");
    internal static void Stop(){Reset("Stop");if(_graph is not null)MelonLogger.Warning("[EZapi] EZAPI_DIALOGUE_STOP_RETAINED: native references retained until process exit.");_started=false;Hooks.Stop();}
    private static void Warn(string code,Exception e)=>MelonLogger.Warning("[EZapi] "+code+": "+e);
    internal static object Report()=>new{ready=IsAvailable,native=Hooks.Snapshot(),generation=_generation,active=_graph?.Execution.Active??false,
        owner=_graph?.Execution.Script.OwnerId,requestId=_graph?.Execution.Request,retainedItems=_graph?.Items.Count??0,retainedNodes=_graph?.Nodes.Count??0,
        confirmations=_graph?.Execution.Confirmations??0,selectDepth=_selectDepth,permit=_permit is not null,releaseFault=_fault,lastNativeState=_graph?.LastNativeState,trace=Trace.ToArray(),resets=Resets.ToArray()};
    internal static int RetainedItems=>_graph?.Items.Count??0;

    private sealed class NativeGraph:NativeDialogueGraph
    {
        private bool _modeLease;
        internal void AcquireMode(DialogUIManager manager){if(!manager.isSecMonologuePlaying){manager.isSecMonologuePlaying=true;_modeLease=true;}}
        internal void RestoreMode(DialogUIManager manager){if(!_modeLease)return;_modeLease=false;if(manager.Pointer.ToInt64()==Manager&&manager.isSecMonologuePlaying)manager.isSecMonologuePlaying=false;}
        internal NativeGraph(DialogueScript script,long generation,GameRunIdentity identity,string request,long manager,int count):base(script,generation,identity,request,manager,count){}
        internal bool ReferencedBy(DialogUIManager manager)
        {
            var roots=new List<Dialogue?>{manager.currentDialog,manager.displayingUncompletedDialog};
            if(manager.scheduledEndActionLines is not null)for(int i=0;i<manager.scheduledEndActionLines.Count;i++)roots.Add(manager.scheduledEndActionLines[i]);
            return base.ReferencedBy(roots);
        }
    }
}

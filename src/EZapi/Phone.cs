using Il2Cpp;
using UnityEngine;
using MelonLoader;
namespace EZapi;

/// <summary>独立电话联系人。登记可早于目录；可见性、名称与通话均使用当前载入身份。</summary>
public static class PhoneApi
{
    private static readonly NativeHookGroup Hooks=new("ezapi.presentation.phone");
    private static readonly PhoneCatalog Catalog=new();
    private sealed record Binding(PhoneEntry Entry,StorePhoneClient Client);
    private static readonly Dictionary<long,Binding> Bindings=new();
    private static GameRunIdentity? _identity;
    private static long _dictionary,_generation,_phoneManager;
    private static bool _started,_busy,_fault;
    private static double _nextRefresh;
    private static NativeDialogueGraph? _graph;
    private static Binding? _call;
    private static long? _dialPermit;
    private static bool _factoryUsed;
    private static int _selectDepth;
    private sealed record ChoicePermit(NativeDialogueGraph Graph,long Source,int Index);
    private static ChoicePermit? _choicePermit;
    private static readonly Queue<object> Resets=new();

    internal static void Start()=>_started=true;
    public static bool IsAvailable=>_started&&GameSession.OnThread&&!_fault&&Hooks.Snapshot().Ready;
    public static NativeHookStatus GetRuntimeStatus(){GameSession.RequireThread();return Hooks.Snapshot();}
    /// <summary>Queued 只表示声明已接受；Snapshot.Applied 才表示当前原生目录已经读回本联系人。</summary>
    public static PresentationResult Register(string ownerId,PhoneContact contact)
    {
        if(!_started||!GameSession.OnThread)return new(PresentationStatus.Unavailable,"EZAPI_PHONE_THREAD");
        if(_busy||_selectDepth!=0||_dialPermit.HasValue)return new(PresentationStatus.Conflict,"EZAPI_PHONE_REENTRY");
        var result=Catalog.Register(ownerId,contact);
        if(result.Status!=PresentationStatus.Queued)return result;
        _nextRefresh=0;Refresh();return result;
    }
    /// <summary>移除本作者声明并撤销其通话回调；不删除同号码下被外部替换的对象。</summary>
    public static PresentationResult Unregister(string ownerId,string contactId)
    {
        if(!GameSession.OnThread)return new(PresentationStatus.Unavailable,"EZAPI_PHONE_THREAD");
        if(!PresentationRules.Owned(ownerId,contactId))return new(PresentationStatus.Invalid,"EZAPI_PHONE_CONTACT");
        if(_busy||_selectDepth!=0||_dialPermit.HasValue)return new(PresentationStatus.Conflict,"EZAPI_PHONE_REENTRY");
        var entry=Catalog.Entries.Values.FirstOrDefault(e=>e.Owner==ownerId&&e.Definition.Id==contactId);
        if(entry is null)return new(PresentationStatus.Closed);
        Catalog.Entries.Remove(entry.Definition.Number);
        if(_call?.Entry==entry)_graph?.Execution.Invalidate();
        try{RemoveBinding(entry);Maintain();return _call?.Entry==entry&&_graph is not null?new(PresentationStatus.Indeterminate,"EZAPI_PHONE_RETAINED"):new(PresentationStatus.Closed);}
        catch(Exception e){Warn("EZAPI_PHONE_UNREGISTER",e);return new(PresentationStatus.Indeterminate,"EZAPI_PHONE_UNREGISTER");}
    }
    public static IReadOnlyList<PhoneContactView> Snapshot(){GameSession.RequireThread();return Array.AsReadOnly(Catalog.Entries.Values.Select(e=>e.View()).ToArray());}
    private static bool Scope(GameRunIdentity identity)=>GameSession.TryResolve(identity.Epoch,out _)&&GameSession.Identity==identity;
    private static StorePhoneClient? At(PlayerStore player,long number)=>player.PhoneClientDict.ContainsKey(number)?player.PhoneClientDict[number]:null;
    private static bool Same(StorePhoneClient? a,StorePhoneClient? b)=>a is not null&&b is not null&&a.Pointer==b.Pointer;
    private static bool Bound(Binding binding)
    {
        if(_identity is null||!Scope(_identity)||!Catalog.Entries.TryGetValue(binding.Entry.Definition.Number,out var entry)||entry!=binding.Entry||
            !GameSession.TryResolve(_identity.Epoch,out var player)||player.PhoneClientDict is null||player.PhoneClientDict.Pointer.ToInt64()!=_dictionary)return false;
        return Same(At(player,entry.Definition.Number),binding.Client);
    }
    private static Binding? Find(StorePhoneClient client)=>Bindings.Values.FirstOrDefault(b=>Same(b.Client,client));
    private static void RemoveBinding(PhoneEntry entry)
    {
        if(!Bindings.TryGetValue(entry.Definition.Number,out var binding))return;
        // 仅核对当前周目/字典和确切对象；不可写已失效的旧字典。
        if(_identity is not null&&Scope(_identity)&&GameSession.TryResolve(_identity.Epoch,out var player)&&player.PhoneClientDict is not null&&
            player.PhoneClientDict.Pointer.ToInt64()==_dictionary&&Same(At(player,entry.Definition.Number),binding.Client))player.PhoneClientDict.Remove(entry.Definition.Number);
        Bindings.Remove(entry.Definition.Number);entry.Applied=false;
    }
    internal static void Refresh()
    {
        if(!IsAvailable||_busy||_selectDepth!=0||_dialPermit.HasValue)return;
        _busy=true;
        try {
            if(!GameSession.TryResolve(null,out var player)||player.PhoneClientDict is null)return;
            var identity=GameSession.Identity!;var dict=player.PhoneClientDict;
            if(_identity!=identity||_dictionary!=dict.Pointer.ToInt64()) {
                _graph?.Execution.Invalidate();Bindings.Clear();_identity=identity;_dictionary=dict.Pointer.ToInt64();
                foreach(var entry in Catalog.Entries.Values){entry.Applied=entry.Visible=false;entry.Error="";}
            }
            foreach(var entry in Catalog.Entries.Values.ToArray()) {
                try {
                    var existing=At(player,entry.Definition.Number);
                    Bindings.TryGetValue(entry.Definition.Number,out var binding);
                    if(existing is not null&&!Same(existing,binding?.Client)&&existing.dialogFuncId!=entry.Marker) {
                        entry.Applied=entry.Visible=false;entry.Error="EZAPI_PHONE_NUMBER_CONFLICT";continue;
                    }
                    var context=new PhoneContext(identity.Epoch,identity.RunId,identity.SaveSlotId);
                    var visible=entry.Definition.Visible(context);
                    var name=visible?entry.Definition.DisplayName(context):entry.Name;
                    if(!Scope(identity)||player.PhoneClientDict.Pointer!=dict.Pointer)return;
                    if(visible&&(!PresentationRules.Text(name,512)||string.IsNullOrWhiteSpace(name)))throw new InvalidOperationException("EZAPI_PHONE_NAME");
                    entry.Visible=visible;entry.Error="";
                    if(!visible){RemoveBinding(entry);continue;}
                    entry.Name=name;
                    if(existing is null) {
                        existing=new StorePhoneClient{phoneClientType=StorePhoneClient.PhoneClientType.None,phoneState=StorePhoneClient.PhoneState.Regular,
                            dialogFuncId=entry.Marker,locID=entry.Marker,displayName=name,dialedBefore=true,cooldownDuration=0,currentCooldown=0,
                            alias=new Il2CppSystem.Collections.Generic.List<string>()};
                        dict.Add(entry.Definition.Number,existing);
                    }
                    // 旧存档同一稳定标记可重新绑定；相同ID之外的对象绝不接管。
                    Bindings[entry.Definition.Number]=new(entry,existing);
                    if(!Same(At(player,entry.Definition.Number),existing))throw new InvalidOperationException("EZAPI_PHONE_APPLY_READBACK");
                    entry.Applied=true;
                }catch(Exception e){entry.Applied=entry.Visible=false;entry.Error="EZAPI_PHONE_QUERY";Warn(entry.Error,e);RemoveBinding(entry);}
            }
        }catch(Exception e){Warn("EZAPI_PHONE_REFRESH",e);}
        finally{_busy=false;_nextRefresh=Time.realtimeSinceStartupAsDouble+1;}
    }
    internal static void DirectoryReady()
    {
        if(!_started)return;
        Hooks.Install(typeof(PhoneApi),
            new(typeof(StorePhoneClient),nameof(StorePhoneClient.GetLocalizedDisplayName),typeof(string),Type.EmptyTypes,Prefix:nameof(BeforeName)),
            new(typeof(StorePhoneClient),nameof(StorePhoneClient.ShownInPhoneBook),typeof(bool),Type.EmptyTypes,Prefix:nameof(BeforeShown)),
            new(typeof(StorePhoneClient),nameof(StorePhoneClient.GetCallDialog),typeof(Dialogue),Type.EmptyTypes,Prefix:nameof(BeforeFactory)),
            new(typeof(PhoneBookUIManager),nameof(PhoneBookUIManager.OpenUI),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeOpen)),
            new(typeof(PhoneUIManager),nameof(PhoneUIManager.OpenUI),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeOpen)),
            new(typeof(PhoneUIManager),nameof(PhoneUIManager.StartPhoneDialog),typeof(void),new[]{typeof(long)},Prefix:nameof(BeforeDial),Finalizer:nameof(AfterDial)),
            new(typeof(PhoneUIManager),nameof(PhoneUIManager.OnPhoneDialogEnd),typeof(void),new[]{typeof(bool)},Prefix:nameof(BeforeEnd)),
            new(typeof(PhoneUIManager),nameof(PhoneUIManager.CloseUI),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeEnd)),
            new(typeof(DialoguePhoneUIManager),nameof(DialoguePhoneUIManager.StartDialogue),typeof(void),new[]{typeof(Dialogue)},Prefix:nameof(BeforeStart)),
            new(typeof(DialoguePhoneUIManager),nameof(DialoguePhoneUIManager.DisplayClientText),typeof(void),new[]{typeof(Dialogue)},Postfix:nameof(AfterText)),
            new(typeof(DialoguePhoneUIManager),nameof(DialoguePhoneUIManager.SelectChoice),typeof(void),new[]{typeof(int)},Prefix:nameof(BeforeSelect),Finalizer:nameof(AfterSelect)),
            new(typeof(PlayerStore),nameof(PlayerStore.LoadGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeLoad)),
            new(typeof(PlayerStore),nameof(PlayerStore.StartNewGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeNew)),
            new(typeof(MainMenuUIController),nameof(MainMenuUIController.Awake),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeMenu)));
    }
    private static bool Live=>_started&&GameSession.OnThread&&Hooks.Ready;
    private static bool BeforeName(StorePhoneClient __instance,ref string __result)
    {if(!Live)return true;var binding=Find(__instance);if(binding is null)return true;__result=binding.Entry.Name;return false;}
    private static bool BeforeShown(StorePhoneClient __instance,ref bool __result)
    {
        if(!Live)return true;var binding=Find(__instance);if(binding is null)return true;
        __result=false;try{__result=binding.Entry.Visible&&binding.Entry.Applied&&Bound(binding);}catch(Exception e){Warn("EZAPI_PHONE_SHOWN",e);}return false;
    }
    private static void BeforeOpen()=>Refresh();
    private static bool BeforeDial(PhoneUIManager __instance,long __0,out bool __state)
    {
        __state=false;
        try{return DialCore(__instance,__0,out __state);}
        catch(Exception e){_dialPermit=null;_graph?.Execution.Invalidate();Warn("EZAPI_PHONE_DIAL",e);return false;}
    }
    private static bool DialCore(PhoneUIManager __instance,long __0,out bool __state)
    {
        __state=false;if(!Live)return true;
        if(!Catalog.Entries.ContainsKey(__0))return _selectDepth==0||_graph is null;
        if(PhoneUIManager.Instance==null||__instance.Pointer!=PhoneUIManager.Instance.Pointer)return false;
        if(_busy||_dialPermit.HasValue||_selectDepth!=0)return false;
        Refresh();Maintain();
        if(!Bindings.TryGetValue(__0,out var binding)||!Bound(binding)) {
            // 声明号码被外部对象占用时，电话仍属于外部；不截断它的原生呼叫。
            if(GameSession.TryResolve(null,out var player)&&player.PhoneClientDict is not null) {
                var actual=At(player,__0);return actual is not null&&actual.dialogFuncId!=Catalog.Entries[__0].Marker;
            }
            return false;
        }
        if(!IsAvailable||_graph is not null||!binding.Entry.Visible||
            __instance.phonePanel==null||!__instance.phonePanel.activeInHierarchy||InventoryApi.IsItemDragActive)return false;
        var manager=DialoguePhoneUIManager.Instance;
        if(manager==null||manager.currentDialog is not null||manager.displayingUncompletedDialog is not null||manager.isAutoAdvancePending||manager.isEndPending||manager.isConversationActiveFlag!=0)return false;
        _dialPermit=__0;_factoryUsed=false;__state=true;return true;
    }
    private static Exception? AfterDial(Exception? __exception,bool __state)
    {
        if(!__state)return __exception;
        _dialPermit=null;_factoryUsed=false;
        if(__exception is not null)_graph?.Execution.Invalidate();
        Maintain();return __exception;
    }
    private static bool BeforeFactory(StorePhoneClient __instance,ref Dialogue? __result)
    {
        if(!Live)return true;var binding=Find(__instance);if(binding is null)return true;
        __result=null;
        if(!_dialPermit.HasValue||_factoryUsed||_busy||!Bindings.TryGetValue(_dialPermit.Value,out var expected)||expected!=binding||!Bound(binding)||!binding.Entry.Visible||_graph is not null)return false;
        _factoryUsed=true;_busy=true;
        try {
            var identity=_identity!;var definition=binding.Entry.Definition;
            var phone=PhoneUIManager.Instance;var manager=DialoguePhoneUIManager.Instance;
            if(phone==null||manager==null)throw new InvalidOperationException("EZAPI_PHONE_UI");
            var script=definition.Call(new(identity.Epoch,identity.RunId,identity.SaveSlotId));
            if(!Scope(identity)||!Bound(binding)||script is null||script.OwnerId!=binding.Entry.Owner)throw new InvalidOperationException("EZAPI_PHONE_SCRIPT");
            if(PhoneUIManager.Instance==null||PhoneUIManager.Instance.Pointer!=phone.Pointer||DialoguePhoneUIManager.Instance==null||DialoguePhoneUIManager.Instance.Pointer!=manager.Pointer||
                phone.phonePanel==null||!phone.phonePanel.activeInHierarchy||manager.currentDialog is not null||manager.displayingUncompletedDialog is not null||manager.isAutoAdvancePending||manager.isEndPending)
                throw new InvalidOperationException("EZAPI_PHONE_CHANGED_DURING_FACTORY");
            var count=manager.dialogChoiceBubbles.Count;_phoneManager=phone.Pointer.ToInt64();
            var graph=new NativeDialogueGraph(script,++_generation,identity,definition.Id,manager.Pointer.ToInt64(),count);_graph=graph;_call=binding;
            graph.Build((source,index,edge)=>Confirm(graph,source,index,edge),(source,index,edge)=>Leave(graph,source,index));
            if(!Scope(identity)||!Bound(binding))throw new InvalidOperationException("EZAPI_PHONE_SCOPE");
            __result=graph.Pages[graph.Plan.Root];
        }catch(Exception e){_graph?.Execution.Invalidate();Warn("EZAPI_PHONE_FACTORY",e);}
        finally{_busy=false;}
        return false;
    }
    private static bool BeforeStart(DialoguePhoneUIManager __instance,Dialogue __0)
    {
        if(!Live||_graph is null||__instance.Pointer.ToInt64()!=_graph.Manager||_graph.Owns(__0))return true;
        if(_selectDepth!=0&&_graph.Owns(__instance.currentDialog))return false;
        _graph.Execution.Invalidate();_choicePermit=null;return true;
    }
    private static bool Current(NativeDialogueGraph graph)
    {
        var manager=DialoguePhoneUIManager.Instance;var phone=PhoneUIManager.Instance;
        return Live&&!_fault&&_graph==graph&&graph.Execution.Active&&Scope(graph.Identity)&&_call is not null&&Bound(_call)&&_call.Entry.Visible&&
            manager!=null&&manager.Pointer.ToInt64()==graph.Manager&&graph.Owns(manager.currentDialog)&&phone!=null&&phone.Pointer.ToInt64()==_phoneManager&&Same(phone.currentPhoneClient,_call.Client)&&phone.isTalking&&phone.phonePanel!=null&&phone.phonePanel.activeInHierarchy;
    }
    private static void AfterText(DialoguePhoneUIManager __instance,Dialogue __0,bool __runOriginal)
    {
        if(!Live||!__runOriginal||_graph is null||!_graph.Owns(__0)||__instance.Pointer.ToInt64()!=_graph.Manager)return;
        try {
            if(__instance.clientNameTMP!=null)__instance.clientNameTMP.text=_graph.Execution.Script.Definition.Speaker;
            if(__instance.clientNameBubble!=null)__instance.clientNameBubble.SetActive(true);
        }catch(Exception e){Warn("EZAPI_PHONE_SPEAKER",e);}
    }
    private static void BeforeSelect(DialoguePhoneUIManager __instance,int __0,bool __runOriginal,out bool __state)
    {
        __state=false;if(!Live)return;__state=true;_selectDepth++;_choicePermit=null;
        try {
            var graph=_graph;var source=__instance.currentDialog;
            if(__runOriginal&&_selectDepth==1&&graph is not null&&__instance.Pointer.ToInt64()==graph.Manager&&Current(graph)&&source is not null&&graph.Edges.ContainsKey((source.Pointer.ToInt64(),__0))&&source.IsChoiceEnabledAt(__0))_choicePermit=new(graph,source.Pointer.ToInt64(),__0);
        }catch(Exception e){_choicePermit=null;Warn("EZAPI_PHONE_SELECT",e);}
    }
    private static Exception? AfterSelect(Exception? __exception,bool __state)
    {if(__state){_choicePermit=null;_selectDepth=Math.Max(0,_selectDepth-1);}return __exception;}
    private static void Confirm(NativeDialogueGraph graph,long source,int index,DialogueEdge edge)
    {
        try {
            var permit=_choicePermit;
            if(permit is null||permit.Graph!=graph||permit.Source!=source||permit.Index!=index||_selectDepth!=1)return;
            _choicePermit=null;var reply=graph.Execution.Commit(edge.Choice,()=>Current(graph),message=>MelonLogger.Warning("[EZapi] "+message));
            if(Current(graph))graph.Pages[edge.Target].SetText(graph.Execution.Script.Definition.Speaker,reply);
        }catch(Exception e){graph.Execution.Invalidate();Warn("EZAPI_PHONE_CONFIRM",e);}
    }
    private static void BeforeEnd(PhoneUIManager __instance){if(!Live||__instance.Pointer.ToInt64()!=_phoneManager)return;_choicePermit=null;_graph?.Execution.Invalidate();}
    private static void Leave(NativeDialogueGraph graph,long source,int index)
    {
        try {
            var permit=_choicePermit;
            if(permit is null||permit.Graph!=graph||permit.Source!=source||permit.Index!=index||_selectDepth!=1||!Current(graph))return;
            _choicePermit=null;
            // 原生文字结束只清对话UI；真实挂断必须通知电话管理器。
            // 同步入口只清通话身份，不在仍在读取choices的选择栈中中断对话，也不排程可能误挂下个电话的延时操作。
            PhoneUIManager.Instance!.OnPhoneDialogEnd(true);
        }catch(Exception e){graph.Execution.Invalidate();Warn("EZAPI_PHONE_END",e);}
    }
    internal static void Maintain()
    {
        if(!_started||!GameSession.OnThread||_busy||_selectDepth!=0||_dialPermit.HasValue)return;
        try {
            var graph=_graph;
            if(graph is not null&&!graph.ReleaseFault) {
                var manager=DialoguePhoneUIManager.Instance;var same=manager!=null&&manager.Pointer.ToInt64()==graph.Manager;
                if(!Current(graph))graph.Execution.Invalidate();
                var ownedOrEnded=same&&(manager!.currentDialog is null||graph.Owns(manager.currentDialog));
                if(same&&!graph.Execution.Active&&graph.Owns(manager!.currentDialog))manager.InterruptDialog();
                // InterruptDialog 仅清原生文字；撤销本次通话还须清准确的电话身份。
                // 外部图接管或电话manager替换后，不挂断对方的通话。
                var phone=PhoneUIManager.Instance;
                if(!graph.Execution.Active&&ownedOrEnded&&_call is not null&&phone!=null&&phone.Pointer.ToInt64()==_phoneManager&&Same(phone.currentPhoneClient,_call.Client)&&phone.isTalking)
                    phone.OnPhoneDialogEnd(false);
                if(!same||!graph.ReferencedBy(new[]{manager!.currentDialog,manager.displayingUncompletedDialog})&&!manager.isAutoAdvancePending&&!manager.isEndPending) {
                    graph.Execution.Invalidate();
                    try{graph.Release();_graph=null;_call=null;}
                    catch(Exception e){graph.ReleaseFault=true;_fault=true;Warn("EZAPI_PHONE_RELEASE_BLOCKED",e);}
                }
            }
            if(Time.realtimeSinceStartupAsDouble>=_nextRefresh)Refresh();
        }catch(Exception e){if(_graph is not null&&!_graph.InspectFault){_graph.InspectFault=true;Warn("EZAPI_PHONE_RETAINED",e);}}
    }
    private static void BeforeLoad()=>Reset("LoadGame");
    private static void BeforeNew()=>Reset("StartNewGame");
    private static void BeforeMenu()=>Reset("MainMenu.Awake");
    internal static void SceneChanged()=>Reset("SceneUnloaded");
    private static void Reset(string cause)
    {
        _choicePermit=null;_graph?.Execution.Invalidate();var before=_graph?.Items.Count??0;
        _nextRefresh=double.PositiveInfinity; // 重置过程中不再调用作者查询或重新写入旧周目的目录。
        Maintain();Bindings.Clear();_identity=null;_dictionary=0;_nextRefresh=0;
        foreach(var entry in Catalog.Entries.Values){entry.Visible=entry.Applied=false;entry.Error="";}
        Resets.Enqueue(new{cause,before,after=_graph?.Items.Count??0,at=DateTimeOffset.Now});while(Resets.Count>16)Resets.Dequeue();
    }
    internal static void Stop()
    {
        _graph?.Execution.Invalidate();_nextRefresh=double.PositiveInfinity;
        foreach(var entry in Catalog.Entries.Values.ToArray())try{RemoveBinding(entry);}catch(Exception e){Warn("EZAPI_PHONE_STOP",e);}
        Reset("Stop");
        if(_graph is not null)MelonLogger.Warning("[EZapi] EZAPI_PHONE_STOP_RETAINED");
        _started=false;Hooks.Stop();Catalog.Entries.Clear();
    }
    private static void Warn(string code,Exception e)=>MelonLogger.Warning("[EZapi] "+code+": "+e);
    internal static object Report()=>new{ready=IsAvailable,native=Hooks.Snapshot(),contacts=Catalog.Entries.Values.Select(e=>e.View()).ToArray(),
        generation=_generation,identity=_identity,active=_graph?.Execution.Active??false,retainedItems=_graph?.Items.Count??0,retainedNodes=_graph?.Nodes.Count??0,
        selectDepth=_selectDepth,dialPermit=_dialPermit,choicePermit=_choicePermit is not null,releaseFault=_fault,resets=Resets.ToArray()};
    internal static int RetainedItems=>_graph?.Items.Count??0;
}

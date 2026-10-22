using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>使用游戏原生纸条面板。只创建游离临时纸条，不放入背包、不保存；其它作者/原版阅读窗口优先。</summary>
public static class ReadingApi
{
    private static readonly NativeHookGroup Hooks=new("ezapi.presentation.reading");
    private static readonly ReadingCoordinator Coordinator=new(new NativeReadingHost());
    private static bool _started;
    private static readonly Queue<object> Resets=new();
    internal static void Start()=>_started=true;
    public static bool IsAvailable=>_started&&GameSession.OnThread&&Hooks.Snapshot().Ready;
    public static NativeHookStatus GetRuntimeStatus(){GameSession.RequireThread();return Hooks.Snapshot();}
    public static PresentationResult Open(string ownerId,string documentId,string runId,int slotId,string title,string body,string signature)
    {
        if(!GameSession.OnThread)return new(PresentationStatus.Unavailable,"EZAPI_READING_THREAD");
        return Coordinator.Open(ownerId,documentId,runId,slotId,title,body,signature);
    }
    /// <summary>Opens a document using one current-locale snapshot; all three strings resolve before native allocation.</summary>
    public static PresentationResult Open(string ownerId,string documentId,string runId,int slotId,
        LocalizedItemText title,LocalizedItemText body,LocalizedItemText signature)
    {
        if(!GameSession.OnThread)return new(PresentationStatus.Unavailable,"EZAPI_READING_THREAD");
        try {
            var text=ReadingTextResolution.Resolve(NativeItemLocalization.CurrentLocaleCode,title,body,signature);
            return Coordinator.Open(ownerId,documentId,runId,slotId,text.Title,text.Body,text.Signature);
        }catch(ArgumentException error){return new(PresentationStatus.Invalid,"EZAPI_READING_LOCALIZED_TEXT: "+error.Message);}
    }
    public static PresentationResult Close(string ownerId,string documentId)
    {
        if(!GameSession.OnThread)return new(PresentationStatus.Unavailable,"EZAPI_READING_THREAD");
        return Coordinator.Close(ownerId,documentId);
    }
    internal static void DirectoryReady()
    {
        if(!_started)return;
        Hooks.Install(typeof(ReadingApi),
            new(typeof(HandnoteUIManager),nameof(HandnoteUIManager.OpenPanel),typeof(void),new[]{typeof(GameItem)},Postfix:nameof(AfterOpen)),
            new(typeof(HandnoteUIManager),nameof(HandnoteUIManager.ClosePanel),typeof(void),Type.EmptyTypes,Postfix:nameof(AfterClose)),
            new(typeof(PlayerStore),nameof(PlayerStore.LoadGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeLoad)),
            new(typeof(PlayerStore),nameof(PlayerStore.StartNewGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeNewGame)),
            new(typeof(MainMenuUIController),nameof(MainMenuUIController.Awake),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeMenu)));
    }
    private static void AfterOpen(HandnoteUIManager __instance,GameItem __0,bool __runOriginal)
    {
        if(!_started||!GameSession.OnThread||!Hooks.Ready||!__runOriginal)return;
        Coordinator.Opened(__instance.Pointer.ToInt64(),__0?.Pointer.ToInt64()??0);
    }
    private static void AfterClose(HandnoteUIManager __instance,bool __runOriginal)
    {
        if(!_started||!GameSession.OnThread||!Hooks.Ready||!__runOriginal)return;
        if(__instance!=null&&(__instance.panel==null||!__instance.panel.activeInHierarchy))Coordinator.Closed(__instance.Pointer.ToInt64());
    }
    private static void BeforeLoad()=>BeforeReset("PlayerStore.LoadGame");
    private static void BeforeNewGame()=>BeforeReset("PlayerStore.StartNewGame");
    private static void BeforeMenu()=>BeforeReset("MainMenuUIController.Awake");
    private static void BeforeReset(string cause){if(_started&&GameSession.OnThread&&Hooks.Ready)Reset(cause);}
    private static void Reset(string cause)
    {
        var before=Coordinator.RetainedDocuments;Coordinator.Reset();
        Resets.Enqueue(new{cause,before,after=Coordinator.RetainedDocuments,at=DateTimeOffset.Now});while(Resets.Count>16)Resets.Dequeue();
    }
    internal static void Maintain(){if(_started&&GameSession.OnThread)Coordinator.Maintain();}
    internal static void SceneChanged()=>Reset("SceneUnloaded");
    internal static void Stop(){Coordinator.Reset();if(Coordinator.HasDocument)MelonLogger.Warning("[EZapi] EZAPI_READING_STOP_RETAINED: native close/readback failed; visible document retained until process exit.");_started=false;Hooks.Stop();}
    internal static int RetainedDocuments=>Coordinator.RetainedDocuments;
    internal static object Report()=>new{ready=IsAvailable,native=Hooks.Snapshot(),active=Coordinator.HasDocument,owner=Coordinator.Owner,documentId=Coordinator.DocumentId,
        retainedDocuments=Coordinator.RetainedDocuments,busy=Coordinator.IsBusy,releaseFailures=Coordinator.ReleaseFailures,resets=Resets.ToArray(),scope="native handnote panel; detached temporary item; no inventory or save writes; cleanup failure blocks further allocation until restart"};

    private sealed class NativeDocument:IReadingDocument
    {
        internal readonly GameItem Item;
        private bool _released;
        internal NativeDocument(GameItem item)=>Item=item;
        public long Token=>Item.Pointer.ToInt64();
        public void Release()
        {
            if(_released)return;
            if(Item.parentInventory is not null)throw new InvalidOperationException("EZAPI_READING_ATTACHED: temporary document unexpectedly acquired an inventory; not destroyed.");
            _released=true;Item.Destroy();
        }
    }
    private sealed class NativeReadingHost:IReadingHost
    {
        public ReadingEnvironment Inspect()
        {
            if(!GameSession.OnThread)return new(false,null,0,false);
            var manager=HandnoteUIManager.Instance;
            var panel=manager!=null?manager.panel:null;
            var identity=GameSession.TryResolve(null,out _)?GameSession.Identity:null;
            // 清理必须能在周目已失效之后执行；库存查询要求有效周目，菜单阶段绝不能调用。
            var dragging=identity is not null&&InventoryApi.IsItemDragActive;
            var valid=manager!=null&&panel!=null;
            return new(IsAvailable,identity is null?null:new(identity.RunId,identity.SaveSlotId,identity.Epoch),manager!=null&&panel!=null?manager.Pointer.ToInt64():0,
                valid&&panel!.activeInHierarchy,dragging,
                valid&&manager!.title!=null?manager.title.text:"",valid&&manager!.body!=null?manager.body.text:"",valid&&manager!.sign!=null?manager.sign.text:"");
        }
        public IReadingDocument Create(string title,string body,string signature)
        {
            var item=DirectoryMaster.Item("handnote",true)??throw new InvalidOperationException("EZAPI_READING_TEMPLATE: handnote unavailable.");
            try {
                if(item.parentInventory is not null)throw new InvalidOperationException("EZAPI_READING_TEMPLATE: factory returned attached item.");
                item.state??=new TagSystem();
                item.state.GetTag("HANDNOTE_TITLE").Enable().SetString(title);
                item.state.GetTag("HANDNOTE_BODY").Enable().SetString(body);
                item.state.GetTag("HANDNOTE_SIGN").Enable().SetString(signature);
                item.SyncModifiedState();return new NativeDocument(item);
            }catch{if(item.parentInventory is null)item.Destroy();throw;}
        }
        public void Open(long panel,IReadingDocument document)
        {
            var manager=HandnoteUIManager.Instance;
            if(manager==null||manager.Pointer.ToInt64()!=panel)throw new InvalidOperationException("EZAPI_READING_PANEL_CHANGED");
            manager.OpenPanel(((NativeDocument)document).Item);
        }
        public void Close(long panel)
        {
            var manager=HandnoteUIManager.Instance;
            if(manager!=null&&manager.Pointer.ToInt64()==panel)manager.ClosePanel();
        }
        public void Error(string message)=>MelonLogger.Warning("[EZapi] "+message);
    }
}

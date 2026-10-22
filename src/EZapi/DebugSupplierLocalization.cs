using Il2Cpp;
using UnityEngine;

namespace EZapi;

/// <summary>
/// Explicit disposable-run acceptance of the production supplier pipeline and real dialogue UI.
/// The fixture dispatches arrival itself; it does not claim a naturally walking customer arrival.
/// A temporary native client list isolates insertion from the player's actual customer queue.
/// </summary>
internal static class DebugSupplierLocalization
{
    private const string Owner="ezapi.debug.supplier_locale";
    private static readonly List<object> Checks=new();
    private static readonly Queue<(string Name,Func<bool> Ready,Action Run)> Steps=new();
    private static GameRunIdentity? _run;
    private static StoreClient? _client;
    private static string _locale="en",_request="";
    private static int _passed,_failed,_captures,_purchases,_dismisses,_cash;
    private static int[] _items=Array.Empty<int>();
    private static double _deadline,_next;
    internal static object? LastReport {get;private set;}
    internal static bool Running=>Steps.Count>0;
    private static DialogUIManager Manager=>DialogUIManager.Instance;
    private static string Text(string zh,string en)=>_locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?zh:en;
    private static string Body=>Text("供应商报价","Supplier offers");
    private static string Preview=>Text("仅测试，不扣款","Test only; no payment");
    private static string Reply=>Text("测试已确认","Test accepted");
    private static void Check(bool ok,string name){Checks.Add(new{name,passed=ok});if(ok)_passed++;else _failed++;}
    private static bool At(string text)=>Manager!=null&&Manager.currentDialog?.plainText==text&&Manager.currentDialog.choices?.Count>0&&
        !Manager.IsAnyBubbleTyping()&&Manager.dialogChoiceBubbles[0].gameObject.activeInHierarchy&&Manager.dialogChoiceBubbles[0].IsInteractable();
    private static string Label(int i)=>Manager.currentDialog!.choices[i].Item2.plainText;
    private static void Add(string name,Func<bool> ready,Action run)=>Steps.Enqueue((name,ready,run));

    internal static void Start()
    {
        if(Running)throw new InvalidOperationException("supplier locale fixture already running");
        _run=NativeAccess.RequireRun();
        if(_run.SaveSlotId!=534||_run.RunId!="e39e848c-36b7-40e9-ab7e-b6adcb9881b8"||InventoryApi.IsItemDragActive||
            Manager==null||Manager.currentDialog is not null||DialogueApi.RetainedItems!=0)
            throw new InvalidOperationException("exact disposable run and idle native dialogue required");
        Checks.Clear();LastReport=null;_passed=_failed=_captures=_purchases=_dismisses=0;
        _locale=GameLanguage.Current.LocaleCode;_request=Owner+".visit."+_locale;
        var store=GameSession.Resolve(_run);_cash=store.playerCash;
        _items=store.FindAllItem(true).ToArray().Select(i=>i.uniqueId).OrderBy(i=>i).ToArray();
        var definition=new SupplierDefinition(Owner+".definition","Fallback supplier",context=>{
            _captures++;Check(context.LocaleCode==_locale,"capture receives current locale");
            return new SupplierPage("Fallback offers",new[]{new SupplierOffer(Owner+".offer","Fallback offer","Fallback preview",context=>{
                _purchases++;Check(context.LocaleCode==_locale,"purchase receives opening graph locale");
                return new GameLanguageSnapshot(context.LocaleCode,false).Resolve(new("测试已确认","Test accepted"));
            }){LocalizedLabel=new("安全报价","Safe offer"),LocalizedPreview=new("仅测试，不扣款","Test only; no payment"),
                LocalizedConfirmLabel=new("确认报价","Accept offer")}}){LocalizedBody=new("供应商报价","Supplier offers")};
        },_=>_dismisses++){LocalizedSpeaker=new("测试供应商","Test supplier")};
        var manager=store.storeClientManager??throw new InvalidOperationException("native client manager missing");
        var sprite=StoreClientList.CreateInventor().spriteName;
        var result=SupplierApi.Queue(Owner,_request,_run.RunId,_run.SaveSlotId,Owner+".actor",sprite,definition);
        Check(result.Status==PresentationStatus.Queued,"production SupplierApi queues definition: "+result.Reason);
        if(result.Status!=PresentationStatus.Queued)throw new InvalidOperationException("supplier queue refused");
        // Only this synchronous insertion sees the fixture list. Never retain a swapped field across frames.
        var original=manager.clientStack;
        var isolated=new Il2CppSystem.Collections.Generic.List<StoreClient>();
        try{
            manager.clientStack=isolated;
            ClientVisitApi.OnGenerateCustomer(manager);
            if(isolated.Count!=1)throw new InvalidOperationException("isolated supplier insertion failed");
            _client=isolated[0];
        }finally{manager.clientStack=original;}
        Check(manager.clientStack.Pointer==original.Pointer,"native customer list restored before yielding");
        Check(_client.identifier==Owner+".actor"&&_client.displayName==Text("测试供应商","Test supplier")&&
            _client.clientIntent==StoreClient.ClientIntent.DIALOGUE&&_client.isRegularSellDisable,"localized dialogue-only native supplier created");
        _client.OnArrival.Invoke();
        Add("root",()=>At(Body),()=>{
            Check(Manager.clientNameTMP.text==Text("测试供应商","Test supplier")&&Manager.clientBubble.textfield.text.Contains(Body),"actual native supplier speaker and body TMP");
            Check(Label(0)==Text("安全报价","Safe offer")&&Label(1)==Text("离开","Leave"),"native offer and default leave labels");Manager.SelectChoice(0);
        });
        Add("preview-back",()=>At(Preview),()=>{
            Check(Label(0)==Text("确认报价","Accept offer")&&Label(1)==Text("返回","Back"),"offer-specific confirmation and default back labels");
            Check(_purchases==0,"opening preview does not invoke purchase");Manager.SelectChoice(1);
        });
        Add("back-root",()=>At(Body),()=>{Check(_purchases==0&&_captures==1,"back preserves offer snapshot without purchase");Manager.SelectChoice(0);});
        Add("confirm",()=>At(Preview),()=>Manager.SelectChoice(0));
        Add("reply",()=>At(Reply),()=>{
            Check(_purchases==1&&Manager.clientBubble.textfield.text.Contains(Reply),"actual supplier callback executes once and displays localized reply");
            Check(Label(0)==Text("继续交易","Continue trading")&&Label(1)==Text("离开","Leave"),"native result-page continue and leave labels");Manager.SelectChoice(0);
        });
        Add("refresh",()=>At(Body)&&_captures==2,()=>{
            Check(Manager.clientNameTMP.text==Text("测试供应商","Test supplier")&&_purchases==1,"deferred refresh opens fresh localized supplier page without repurchase");Manager.SelectChoice(1);
        });
        Add("closed",()=>Manager.currentDialog is null&&DialogueApi.RetainedItems==0,()=>{
            SupplierApi.Maintain();_client!.OnDismiss.Invoke();ClientVisitApi.Maintain();SupplierApi.Maintain();
            Check(_dismisses==1,"leave and later native dismissal invoke author once");
            Check(SupplierApi.Status(Owner,_request).Status==PresentationStatus.Closed,"supplier request closed");
            Check(store.playerCash==_cash&&store.FindAllItem(true).ToArray().Select(i=>i.uniqueId).OrderBy(i=>i).SequenceEqual(_items),"native player cash and item identity set unchanged");
            Check(DialogueApi.RetainedItems==0,"supplier graph and choice items released");_client=null;
        });
        _deadline=Time.realtimeSinceStartupAsDouble+30;_next=0;Save();
    }

    internal static void Update()
    {
        if(!Running||Time.realtimeSinceStartupAsDouble<_next)return;
        _next=Time.realtimeSinceStartupAsDouble+.3;
        try{
            if(_run is null||!NativeAccess.SameRun(_run))throw new InvalidOperationException("supplier fixture run changed");
            var step=Steps.Peek();
            if(step.Ready()){Steps.Dequeue();step.Run();_deadline=Time.realtimeSinceStartupAsDouble+30;Save();}
            else if(Time.realtimeSinceStartupAsDouble>_deadline)throw new TimeoutException(step.Name+": "+Manager.currentDialog?.plainText);
        }catch(Exception error){Check(false,error.ToString());Steps.Clear();ClearOwned();Save();}
    }
    private static void Save()=>LastReport=new{at=DateTimeOffset.Now,locale=_locale,running=Running,passed=_passed,failed=_failed,checks=Checks.ToArray(),
        captures=_captures,purchases=_purchases,dismisses=_dismisses,remaining=Steps.Select(s=>s.Name).ToArray(),
        scope="production supplier capture/client factory/native dialogue UI/preview/back/confirmation/reply/refresh/leave; fixture-dispatched arrival and dismissal callbacks; isolated customer insertion; no natural arrival, purchase delivery or disk-save claim"};
    private static void ClearOwned(){
        if(_run is not null&&NativeAccess.SameRun(_run)){
            DialogueApi.Close(Owner,_request);_client?.OnDismiss?.Invoke();ClientVisitApi.Maintain();SupplierApi.Maintain();
        }
        _client=null;
    }
    internal static void Clear(){Steps.Clear();ClearOwned();_run=null;LastReport=null;Checks.Clear();}
}

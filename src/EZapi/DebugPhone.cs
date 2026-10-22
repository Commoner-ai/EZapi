using Il2Cpp;
using UnityEngine;
using MelonLoader;
namespace EZapi;

/// <summary>显式电话回归；通过原生通讯录与拨号流程，不保存、不发放物品。</summary>
internal static class DebugPhone
{
    private const string Owner="ezapi.debug.phone",Id=Owner+".contact";
    private const long Number=9000000034;
    private static readonly List<object> Checks=new();
    private static readonly Queue<(string Name,Func<bool> Ready,Action Run)> Steps=new();
    private static int _passed,_failed,_factories,_calls;
    private static bool _visible=true;
    private static string _name="EZapi 测试联系人",_locale="zh";
    private static string Text(string zh,string en)=>_locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?zh:en;
    private static string ContactName=>Text("EZapi 测试联系人","EZapi test contact");
    private static string Speaker=>Text("EZapi 电话验证","EZapi phone check");
    private static string RootText=>Text("电话根节点","PHONE ROOT");
    private static string PreviewText(int index)=>Text("电话预览 ","PHONE PREVIEW ")+index;
    private static double _deadline,_next;
    private static GameRunIdentity? _identity,_interactive;
    private static PhoneContext? _context;
    private static Il2CppSystem.Action? _stale;
    private static Dictionary<long,long> _baseline=new();
    internal static object? LastReport{get;private set;}
    internal static object? InteractiveReport{get;private set;}
    private static PhoneUIManager Phone=>PhoneUIManager.Instance;
    private static DialoguePhoneUIManager Dialog=>DialoguePhoneUIManager.Instance;
    private static void Check(bool ok,string name){Checks.Add(new{name,passed=ok});if(ok)_passed++;else _failed++;}
    internal static bool Running=>Steps.Count>0;
    private static Dictionary<long,long> Directory()
    {
        GameSession.TryResolve(null,out var player);var result=new Dictionary<long,long>();var pairs=player.PhoneClientDict.GetEnumerator();
        while(pairs.MoveNext())result.Add(pairs.Current.Key,pairs.Current.Value.Pointer.ToInt64());return result;
    }
    private static PhoneContact Contact()=>new(Id,Number,_=>_visible,_=>_name,c=>{
        _factories++;_context=c;
        return DialogueApi.Create(Owner,new(Owner+".script",Speaker,RootText,Enumerable.Range(0,5).Select(i=>new DialogueChoice("c"+i,Text("电话选项 ","Phone choice ")+i,PreviewText(i),context=>{
            _calls++;Check(context.LocaleCode==_locale,"phone callback receives opening graph locale");_stale?.Invoke();return new GameLanguageSnapshot(context.LocaleCode,false).Resolve(new("电话回复 "+context.ChoiceId,"PHONE REPLY "+context.ChoiceId));
        }) { LocalizedLabel=new("电话选项 "+i,"Phone choice "+i), LocalizedPreview=new("电话预览 "+i,"PHONE PREVIEW "+i) }).ToArray()) { LocalizedSpeaker=new("EZapi 电话验证","EZapi phone check"), LocalizedBody=new("电话根节点","PHONE ROOT") });
    });
    private static bool At(string text)=>Dialog!=null&&Dialog.currentDialog?.plainText==text&&!Dialog.IsAnyBubbleTyping()&&Dialog.currentDialog.choices.Count>0&&Dialog.dialogChoiceBubbles[0].gameObject.activeInHierarchy&&Dialog.dialogChoiceBubbles[0].IsInteractable();
    private static void Add(string name,Func<bool> ready,Action run)=>Steps.Enqueue((name,ready,run));
    internal static string Run()
    {
        if(Running)return "电话检查正在运行。";
        if(!GameSession.TryResolve(null,out var player)||!VisibleStoreInventory.IsReady||Phone==null||Dialog==null||Phone.isTalking||Phone.isCalling||Dialog.currentDialog is not null)return "请进入测试档店铺，先结束当前通话。";
        PhoneApi.Unregister(Owner,Id);_identity=GameSession.Identity;_locale=GameLanguage.Current.LocaleCode;_visible=true;_name=ContactName;_passed=_failed=_factories=_calls=0;_context=null;_stale=null;Checks.Clear();_baseline=Directory();
        Check(PhoneApi.IsAvailable&&PhoneApi.GetRuntimeStatus().Targets.Count==14,"fourteen exact phone hooks ready");
        Check(PhoneApi.Register(Owner,Contact()with{Number=99999}).Status==PresentationStatus.Invalid,"number range rejected");
        Check(PhoneApi.Register(Owner,Contact()).Status==PresentationStatus.Queued,"contact declaration accepted");
        Check(PhoneApi.Register(Owner,Contact()).Status==PresentationStatus.Conflict,"duplicate declaration rejected");
        var view=PhoneApi.Snapshot().Single(e=>e.Id==Id);Check(view.Applied&&view.Visible,"contact applied to native directory");
        var client=player.PhoneClientDict[Number];Check(client.GetLocalizedDisplayName()==ContactName&&client.ShownInPhoneBook(),"native name and phone book predicate exact");
        Check(client.GetCallDialog()is null&&_factories==0,"GetCallDialog outside actual dial stack cannot call author");
        Phone.OpenUI();Phone.phoneBookUIManager.OpenUI();
        var elements=Phone.phoneBookUIManager.layoutGroup.GetComponentsInChildren<ContactElement>(true);bool found=false;
        for(int i=0;i<elements.Length;i++)if(elements[i].number==Number&&elements[i].titleTMP.text.Contains(ContactName))found=true;
        Check(found,"real phone book element displays registered name and number");
        Phone.phoneBookUIManager.CloseUI();Phone.AutoCall(Number);
        Add("root",()=>At(RootText),()=>{
            Check(Phone.isTalking&&Phone.currentPhoneClient.Pointer==client.Pointer,"actual native dial selects custom client");
            Check(_factories==1&&_context is not null&&_context.Generation==_identity!.Epoch&&_context.RunId==_identity.RunId&&_context.SlotId==_identity.SaveSlotId,"factory once with current phone identity");
            Check(Dialog.clientNameTMP.text==Speaker&&Dialog.clientBubble.textfield.text.Contains(RootText),"native phone speaker and body TMP exact");
            Check(Dialog.currentDialog!.choices.Count<=Dialog.dialogChoiceBubbles.Count,"phone pages respect native button pool");Dialog.dialogChoiceBubbles[0].OnDialogChoiceClick();
        });
        Add("preview",()=>At(PreviewText(0)),()=>{_stale=Dialog.currentDialog!.choices[0].Item3;_stale!.Invoke();Check(_calls==0,"raw phone delegate denied");Dialog.dialogChoiceBubbles[1].OnDialogChoiceClick();});
        Add("back",()=>At(RootText),()=>{Check(_calls==0,"phone preview back executes no business");Dialog.dialogChoiceBubbles[0].OnDialogChoiceClick();});
        Add("confirm",()=>At(PreviewText(0)),()=>Dialog.dialogChoiceBubbles[0].OnDialogChoiceClick());
        Add("reply",()=>At(Text("电话回复 c0","PHONE REPLY c0")),()=>{
            Check(_calls==1&&Dialog.clientBubble.textfield.text.Contains(Text("电话回复 c0","PHONE REPLY c0")),"native phone confirmation and localized reply exactly once");
            _stale!.Invoke();Check(_calls==1,"phone stale callback cannot replay");Dialog.dialogChoiceBubbles[0].OnDialogChoiceClick();
        });
        Add("ended",()=>PhoneApi.RetainedItems==0&&!Phone.isTalking,()=>{
            Check(true,"normal phone end releases graph");
            _name=Text("EZapi 动态名称","EZapi dynamic name");PhoneApi.Refresh();Check(player.PhoneClientDict[Number].GetLocalizedDisplayName()==_name,"dynamic display name refreshed through native name entry");_name=ContactName;
            var foreign=new StorePhoneClient{phoneState=StorePhoneClient.PhoneState.Regular,displayName="外部测试",locID="ezapi.debug.foreign",dialogFuncId="ezapi.debug.foreign",dialedBefore=true,alias=new Il2CppSystem.Collections.Generic.List<string>()};
            player.PhoneClientDict[Number]=foreign;PhoneApi.Refresh();
            Check(PhoneApi.Snapshot().Single(e=>e.Id==Id).Error=="EZAPI_PHONE_NUMBER_CONFLICT"&&player.PhoneClientDict[Number].Pointer==foreign.Pointer,"external number replacement preserved and conflict reported");
            Phone.StartPhoneDialog(Number);Check(Phone.currentPhoneClient?.Pointer==foreign.Pointer&&_factories==1,"occupied external number keeps native dialing and avoids own author");Phone.CloseUI();
            if(player.PhoneClientDict.ContainsKey(Number)&&player.PhoneClientDict[Number].Pointer==foreign.Pointer)player.PhoneClientDict.Remove(Number);
            PhoneApi.Refresh();Phone.OpenUI();
            Check(player.PhoneClientDict.ContainsKey(Number)&&PhoneApi.Snapshot().Single(e=>e.Id==Id).Applied,"own contact can bind after external slot becomes free");
            _visible=false;PhoneApi.Refresh();
            Check(!player.PhoneClientDict.ContainsKey(Number),"dynamic visibility removes owned native contact");_visible=true;PhoneApi.Refresh();
            Check(player.PhoneClientDict.ContainsKey(Number),"dynamic visibility restores contact");Phone.AutoCall(Number);
        });
        Add("second-call",()=>At(RootText),()=>{Check(_factories==2,"next actual call creates fresh graph");Dialog.SelectChoice(0);});
        Add("close-preview",()=>At(PreviewText(0)),()=>{_stale=Dialog.currentDialog!.choices[0].Item3;Phone.CloseUI();});
        Add("closed",()=>PhoneApi.RetainedItems==0,()=>{
            _stale!.Invoke();Check(_calls==1,"closing phone invalidates prior confirmation");
            Check(PhoneApi.Unregister(Owner,Id).Status==PresentationStatus.Closed,"unregister closes declaration");
            var after=Directory();Check(after.Count==_baseline.Count&&_baseline.All(p=>after.TryGetValue(p.Key,out var ptr)&&ptr==p.Value),"all original native contacts preserved by identity");
            Check(PhoneApi.RetainedItems==0&&!Phone.isTalking,"zero phone graph resources and native call ended");
        });
        _deadline=Time.realtimeSinceStartupAsDouble+25;_next=0;Save("running");return "电话批量检查已启动。";
    }
    private static void Save(string status){LastReport=new{at=DateTimeOffset.Now,status,locale=_locale,passed=_passed,failed=_failed,factories=_factories,calls=_calls,identity=_identity,remaining=Steps.Count,checks=Checks.ToArray(),runtime=PhoneApi.Report()};EZapiBootstrap.WriteDiagnostics();}
    internal static void Update()
    {
        if(_interactive is not null) {
            InteractiveReport=new{at=DateTimeOffset.Now,identityBefore=_interactive,identityAfter=GameSession.Identity,number=Number,factories=_factories,calls=_calls,runtime=PhoneApi.Report()};
            if(GameSession.Identity!=_interactive){_interactive=null;EZapiBootstrap.WriteDiagnostics();}
        }
        if(!Running||Time.realtimeSinceStartupAsDouble<_next)return;_next=Time.realtimeSinceStartupAsDouble+.3;
        try {
            if(GameSession.Identity!=_identity)throw new InvalidOperationException("phone fixture run changed");
            var step=Steps.Peek();if(step.Ready()){Steps.Dequeue();step.Run();_deadline=Time.realtimeSinceStartupAsDouble+25;Save(Running?"running":"complete");}
            else if(Time.realtimeSinceStartupAsDouble>_deadline)throw new InvalidOperationException("phone step timeout: "+step.Name+" current="+Dialog?.currentDialog?.plainText);
        }catch(Exception e){Check(false,"execution: "+e);Steps.Clear();Phone.CloseUI();PhoneApi.Unregister(Owner,Id);MelonLogger.Error("[EZapi] Phone fixture: "+e);Save("failed");}
    }
    internal static string Show()
    {
        if(Running||!GameSession.TryResolve(null,out _)||Phone==null||Phone.isTalking||Phone.isCalling)return "请先进入测试档，等待检查及当前通话结束。";
        _factories=_calls=0;_stale=null;_interactive=GameSession.Identity;
        _visible=true;var result=PhoneApi.Register(Owner,Contact());PhoneApi.Refresh();Phone.OpenUI();Phone.phoneBookUIManager.OpenUI();
        InteractiveReport=new{at=DateTimeOffset.Now,identity=GameSession.Identity,number=Number,result,runtime=PhoneApi.Report()};EZapiBootstrap.WriteDiagnostics();return "通讯录中的 EZapi 测试联系人可直接点击拨号。";
    }
    internal static string ClearContact(){if(Running)return "请等待电话检查结束。";var own=Phone.currentPhoneClient?.dialogFuncId=="ezapi.phone."+Id;var result=PhoneApi.Unregister(Owner,Id);if(own||!Phone.isTalking&&!Phone.isCalling)Phone.CloseUI();EZapiBootstrap.WriteDiagnostics();return "移除测试联系人："+result.Status;}
    /// <summary>显式生命周期夹具：暂停菜单会阻挡通讯录点击，故直接走同一原生拨号入口；不恢复时间、不改变菜单。</summary>
    internal static string DialForLifecycle()
    {
        if(Running||!GameSession.TryResolve(null,out _)||Phone==null||Dialog==null||Phone.isTalking||Phone.isCalling||Dialog.currentDialog is not null)return "请先进入测试档并结束当前通话。";
        Show();Phone.phoneBookUIManager.CloseUI();Phone.StartPhoneDialog(Number);EZapiBootstrap.WriteDiagnostics();
        return "已通过原生入口拨号，可返回主菜单检查资源释放。";
    }
    internal static void Clear(){Steps.Clear();Checks.Clear();LastReport=InteractiveReport=null;_identity=_interactive=null;_stale=null;}
}

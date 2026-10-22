using Il2Cpp;
using MelonLoader;
using UnityEngine;
namespace EZapi;

/// <summary>显式调试入口；仅操作本测试创建的对话，不发物品、不改存档。跨帧走游戏实际选择/过渡流程。</summary>
internal static class DebugDialogue
{
    private const string Owner="ezapi.debug.dialogue",Request=Owner+".request";
    private static string _locale="zh";
    private static string Text(string zh,string en)=>_locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?zh:en;
    private static string Speaker=>Text("EZapi 对话验证","EZapi dialogue check");
    private static string RootText=>Text("对话根节点","ROOT");
    private static string ChoiceText(int index)=>Text("选项 ","Choice ")+index;
    private static string PreviewText(int index)=>Text("预览 ","PREVIEW ")+index;
    private static readonly List<object> Checks=new();
    private static readonly List<object> Trace=new();
    private static readonly Queue<(string Name,Func<bool> Ready,Action Run)> Steps=new();
    private static GameRunIdentity? _identity,_interactive;
    private static int _passed,_failed,_calls,_interactiveCalls,_errorCalls;
    private static double _deadline,_next;
    private static DialogueContext? _context;
    private static Il2CppSystem.Action? _stale;
    private static Dialogue? _root,_foreign,_second;
    private static int Find(string label)=>Enumerable.Range(0,Manager.currentDialog!.choices.Count).Single(i=>Manager.currentDialog.choices[i].Item2.plainText==label);
    internal static object? LastReport{get;private set;}
    internal static object? InteractiveReport{get;private set;}
    private static DialogUIManager Manager=>DialogUIManager.Instance;
    internal static void Clear(){Steps.Clear();Checks.Clear();LastReport=null;InteractiveReport=null;_identity=null;_interactive=null;_stale=null;_root=null;_foreign=null;}
    private static void Check(bool ok,string name){Checks.Add(new{name,passed=ok});if(ok)_passed++;else _failed++;}
    private static bool At(string text)=>Manager!=null&&Manager.currentDialog is not null&&Manager.currentDialog.plainText==text&&Manager.currentDialog.choices?.Count>0&&!Manager.IsAnyBubbleTyping()&&Manager.dialogChoiceBubbles[0].gameObject.activeInHierarchy&&Manager.dialogChoiceBubbles[0].IsInteractable();
    internal static bool Running=>Steps.Count>0;
    private static bool Idle()=>DialogueApi.RetainedItems==0&&Manager!=null&&Manager.currentDialog is null;
    private static void Add(string name,Func<bool> ready,Action action)=>Steps.Enqueue((name,ready,action));
    private static DialogueScript Script(int count=9,bool confirm=true,Func<DialogueContext,string>? callback=null)=>DialogueApi.Create(Owner,new(Owner+".script",Speaker,"ROOT",Enumerable.Range(0,count).Select(i=>new DialogueChoice("choice"+i,ChoiceText(i),PreviewText(i),callback??(context=>{
        _calls++;_context=context;Check(context.LocaleCode==_locale,"callback receives the opening locale snapshot");
        Check(DialogueApi.Open(Owner,Request,_identity!.RunId,_identity.SaveSlotId,Script(1)).Status==PresentationStatus.Conflict,"callback reentrant open rejected");
        Check(!Manager.StartDialogue(new Dialogue().SetText("外部回调", "不得在选择栈内替换节点"),true),"native start inside owned selection stack rejected without replacing current choices");
        _stale?.Invoke();return new GameLanguageSnapshot(context.LocaleCode,false).Resolve(new("回复 "+context.ChoiceId,"REPLY "+context.ChoiceId));
    })) { LocalizedLabel=new("选项 "+i,"Choice "+i), LocalizedPreview=new("预览 "+i,"PREVIEW "+i) }).ToArray()) {RequireConfirmation=confirm, LocalizedSpeaker=new("EZapi 对话验证","EZapi dialogue check"), LocalizedBody=new("对话根节点","ROOT")});
    private static PresentationResult Open(DialogueScript script)=>DialogueApi.Open(Owner,Request,_identity!.RunId,_identity.SaveSlotId,script);
    internal static string Run()
    {
        if(Running)return "对话检查正在运行。";
        if(!VisibleStoreInventory.IsReady||!GameSession.TryResolve(null,out _)||Manager==null||Manager.currentDialog is not null)return "请进入测试档店铺并结束现有对话。";
        Trace.Clear();_locale=GameLanguage.Current.LocaleCode;_identity=GameSession.Identity;_errorCalls=_calls=_passed=_failed=0;Checks.Clear();_stale=null;_context=null;
        Check(DialogueApi.IsAvailable&&DialogueApi.GetRuntimeStatus().Targets.Count==6,"six exact required hooks ready");
        Check(DialogueApi.Open(Owner,Request,_identity!.RunId,_identity.SaveSlotId+1,Script()).Status==PresentationStatus.Conflict,"wrong slot rejected");
        Check(Open(Script()).Status==PresentationStatus.Opened,"native start accepted and current graph read back");
        Add("root",()=>At(RootText),()=>{
            _root=Manager.currentDialog;var choices=_root!.choices;
            Check(choices.Count<=Manager.dialogChoiceBubbles.Count,"native root uses existing button capacity");
            Check(Manager.clientNameTMP.text==Speaker,"native speaker TMP exact");
            Check(Manager.clientBubble.textfield.text.Contains(RootText),"native body TMP visible");
            Check(choices[0].Item2.title=="PLAYER"&&choices[0].Item2.plainText==ChoiceText(0),"native PLAYER transition contains selected text");
            Check(choices[0].Item1.parentInventory is null,"choice item detached from inventory");
            var rejected=new Dialogue().SetText("外部低优先级","应拒绝");rejected.priority=-1;
            Check(!Manager.StartDialogue(rejected,false),"external lower priority request rejected by native rules");
            Check(Manager.currentDialog!.Pointer==_root.Pointer&&Manager.isSecMonologuePlaying,"rejected external start preserves owned graph and native mode");
            var other=DialogueApi.Create("other.dialogue",new("other.dialogue.script","Other","Other",new[]{new DialogueChoice("c","C","P",_=>"R")}));
            Check(DialogueApi.Open("other.dialogue","other.dialogue.request",_identity.RunId,_identity.SaveSlotId,other).Status==PresentationStatus.Conflict,"other author cannot replace dialogue");
            Check(DialogueApi.Close("other.dialogue","other.dialogue.request").Status==PresentationStatus.Conflict,"other author cannot close dialogue");
            Manager.dialogChoiceBubbles[0].OnDialogChoiceClick();
        });
        Add("preview-back",()=>At(PreviewText(0)),()=>{
            _stale=Manager.currentDialog!.choices[0].Item3;_stale.Invoke();Check(_calls==0,"delegate Invoke outside SelectChoice cannot commit");
            Check(Manager.currentDialog!.choices.Count==2,"preview contains confirm and back");Manager.dialogChoiceBubbles[1].OnDialogChoiceClick();
        });
        Add("back-root",()=>At(RootText),()=>{Check(_calls==0,"preview/back execute no business");Manager.SelectChoice(Find(Text("下一页","Next page")));});
        Add("next-page",()=>At(RootText)&&Manager.currentDialog!.choices[0].Item2.plainText==ChoiceText(1),()=>{
            _second=Manager.currentDialog;Check(_second!.Pointer!=_root!.Pointer,"next page uses independent native node");
            Check(_calls==0&&Manager.currentDialog!.choices.Count<=Manager.dialogChoiceBubbles.Count,"pagination no callback and stays within native button pool");Manager.SelectChoice(0);
        });
        Add("page-two-preview",()=>At(PreviewText(1)),()=>Manager.SelectChoice(1));
        Add("page-two-back",()=>At(RootText)&&Manager.currentDialog!.Pointer==_second!.Pointer,()=>{Check(_calls==0,"preview back returns to exact second page without business");Manager.SelectChoice(Find(Text("上一页","Previous page")));});
        Add("previous-page",()=>At(RootText)&&Manager.currentDialog!.Pointer==_root!.Pointer,()=>{Check(true,"previous returns to first native page");Manager.SelectChoice(0);});
        Add("confirm",()=>At(PreviewText(0)),()=>Manager.SelectChoice(0));
        Add("reply",()=>At(Text("回复 choice0","REPLY choice0")),()=>{
            Check(_calls==1,"actual native confirmation calls author exactly once despite nested Invoke");
            Check(_context is not null&&_context.OwnerId==Owner&&_context.RequestId==Request&&_context.RunId==_identity.RunId&&_context.SlotId==_identity.SaveSlotId&&_context.Generation>0,"callback full scope context");
            Check(Manager.clientBubble.textfield.text.Contains(Text("回复 choice0","REPLY choice0")),"native localized reply TMP exact");
            _stale!.Invoke();Check(_calls==1,"replayed prior native delegate cannot commit");Manager.dialogChoiceBubbles[0].OnDialogChoiceClick();
        });
        Add("closed",Idle,()=>{Check(DialogueApi.RetainedItems==0,"normal end releases entire graph");Check(!Manager.isSecMonologuePlaying,"normal end returns native mode lease");Check(Open(Script(1,false,c=>{_calls++;return new GameLanguageSnapshot(c.LocaleCode,false).Resolve(new("直接回复","DIRECT"));})).Status==PresentationStatus.Opened,"direct choice script opens");});
        Add("direct-root",()=>At(RootText),()=>Manager.SelectChoice(0));
        Add("direct-reply",()=>At(Text("直接回复","DIRECT")),()=>{Check(_calls==2,"no-confirm choice executes exactly once");Check(DialogueApi.Close(Owner,Request).Status==PresentationStatus.Closed,"public close interrupts and releases actual graph");Check(!Manager.isSecMonologuePlaying,"explicit close returns native mode lease");});
        Add("error-open",Idle,()=>Check(Open(Script(1,true,_=>{_errorCalls++;throw new InvalidOperationException("intentional dialogue fixture");})).Status==PresentationStatus.Opened,"throwing callback script opens"));
        Add("error-root",()=>At(RootText),()=>Manager.SelectChoice(0));
        Add("error-preview",()=>At(PreviewText(0)),()=>{_stale=Manager.currentDialog!.choices[0].Item3;Manager.dialogChoiceBubbles[0].OnDialogChoiceClick();});
        Add("error-reply",()=>At(Text("操作未完成，请查看日志。","The action did not complete. Check the log.")),()=>{Check(true,"callback exception shows failure reply");_stale!.Invoke();Check(_errorCalls==1,"failed callback replay returns without invoking author");DialogueApi.Close(Owner,Request);});
        Add("takeover-open",Idle,()=>{Open(Script(1));_root=Manager.currentDialog;_foreign=new Dialogue().SetText("外部对话","原生接管");_foreign.SetNextDialogue(_root);Manager.StartDialogue(_foreign,true);Check(!Manager.isSecMonologuePlaying,"external StartDialogue receives restored native mode");DialogueApi.Maintain();});
        Add("takeover-referenced",()=>Manager.currentDialog?.Pointer==_foreign?.Pointer,()=>{
            Check(DialogueApi.RetainedItems>0,"external chain referencing owned graph retains items safely");
            Check(DialogueApi.Close(Owner,Request).Status==PresentationStatus.Indeterminate&&Manager.currentDialog!.Pointer==_foreign!.Pointer,"close preserves external current dialogue");
            _foreign!.nextDialogue=null;DialogueApi.Maintain();Check(DialogueApi.RetainedItems==0,"external chain detachment releases only owned graph");
            Manager.InterruptDialog();_foreign=null;_root=null;
        });
        Add("later-open",Idle,()=>Check(Open(Script(1)).Status==PresentationStatus.Opened,"later script opens"));
        Add("later-root",()=>At(RootText),()=>Manager.SelectChoice(Find(Text("稍后再说","Later"))));
        Add("finish",Idle,()=>{Check(_calls==2,"later exit executes no author business");Check(!Manager.isSecMonologuePlaying,"later exit returns native mode");Check(true,"all native graphs released");});
        _deadline=Time.realtimeSinceStartupAsDouble+20;_next=0;Save("running");return "对话批量检查已启动，约需一分钟。";
    }
    private static void Save(string status)
    {
        LastReport=new{at=DateTimeOffset.Now,status,locale=_locale,currentTitle=Manager!=null?Manager.currentDialog?.title:null,currentText=Manager!=null?Manager.currentDialog?.plainText:null,nativeName=Manager!=null?Manager.clientNameTMP?.text:null,nativeHasChoices=Manager!=null?Manager.currentDialog?.HasChoices():null,nativeChoiceCount=Manager!=null?Manager.currentDialog?.choices?.Count:null,passed=_passed,failed=_failed,calls=_calls,errorCalls=_errorCalls,checks=Checks.ToArray(),identity=_identity,remaining=Steps.Count,trace=Trace.ToArray(),runtime=DialogueApi.Report()};EZapiBootstrap.WriteDiagnostics();
    }
    internal static void Update()
    {
        if(_interactive is not null) {
            InteractiveReport=new{at=DateTimeOffset.Now,identityBefore=_interactive,identityAfter=GameSession.Identity,calls=_interactiveCalls,
                currentTitle=Manager!=null?Manager.currentDialog?.title:null,currentText=Manager!=null?Manager.currentDialog?.plainText:null,runtime=DialogueApi.Report()};
            if(DialogueApi.RetainedItems==0){_interactive=null;EZapiBootstrap.WriteDiagnostics();}
        }
        if(Steps.Count==0||Time.realtimeSinceStartupAsDouble<_next)return;
        _next=Time.realtimeSinceStartupAsDouble+.3;
        try {
            if(GameSession.Identity!=_identity)throw new InvalidOperationException("test run changed");
            var step=Steps.Peek();
            Trace.Add(new{step=step.Name,text=Manager!.currentDialog?.plainText,choices=Manager.currentDialog?.choices?.Count,hasChoices=Manager.currentDialog?.HasChoices(),next=Manager.currentDialog?.nextDialogue?.plainText,active=Manager.isConversationActiveFlag,coroutines=Manager.activeCoroutines?.Count,waiting=Manager.isWaitingForManualAdvance,auto=Manager.isAutoAdvancePending,endPending=Manager.isEndPending,displaying=Manager.displayingUncompletedDialog?.plainText,runtime=DialogueApi.Report()});if(Trace.Count>100)Trace.RemoveAt(0);
            if(step.Ready()) {
                Steps.Dequeue();step.Run();_deadline=Time.realtimeSinceStartupAsDouble+20;Save(Steps.Count==0?"complete":"running");return;
            }
            // 等待游戏自己的打字机/PLAYER 过渡，不注入额外推进。
            var current=Manager!.currentDialog;
            if(current is not null){/* 原生选择已排程 PLAYER 过渡；不注入额外推进。 */}
            if(Time.realtimeSinceStartupAsDouble>_deadline)throw new InvalidOperationException("native step timed out: "+step.Name+" current="+current?.plainText+" title="+current?.title);
        }catch(Exception e){Check(false,"execution: "+e);Steps.Clear();DialogueApi.Close(Owner,Request);MelonLogger.Error("[EZapi] Dialogue fixture: "+e);Save("failed");}
    }
    internal static string Show()
    {
        if(Running)return "请等待当前对话检查结束。";
        if(!VisibleStoreInventory.IsReady||!GameSession.TryResolve(null,out _))return "请先进入测试档。";
        var identity=GameSession.Identity!;_interactiveCalls=0;
        var script=DialogueApi.Create(Owner,new(Owner+".interactive",Speaker,"这是游戏原生对话。可测试预览、返回、分页和确认。",Enumerable.Range(0,9).Select(i=>new DialogueChoice("choice"+i,"测试选项 "+i,"确认选项 "+i+" 后回调只执行一次。",c=>{
            _interactiveCalls++;return "已确认 "+c.ChoiceId+"；执行次数："+_interactiveCalls;
        })).ToArray()));
        var result=DialogueApi.Open(Owner,Request,identity.RunId,identity.SaveSlotId,script);
        if(result.Status==PresentationStatus.Opened)_interactive=identity;
        EZapiBootstrap.WriteDiagnostics();return "原生对话："+result.Status;
    }
    internal static string Close(){var result=DialogueApi.Close(Owner,Request);Update();EZapiBootstrap.WriteDiagnostics();return "关闭对话："+result.Status;}
}

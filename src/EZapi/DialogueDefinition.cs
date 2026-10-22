namespace EZapi;

/// <summary>本次对话调用的身份快照；Generation 为本次展示，不是可保存的业务幂等键。</summary>
public sealed record DialogueContext(long Generation,string RunId,int SlotId,string OwnerId,string RequestId,string DialogueId,string ChoiceId)
{
    /// <summary>The locale snapshot used by the active page; callbacks can use it for dynamic response text.</summary>
    public string LocaleCode{get;init;}="en";
}
/// <summary>Confirm 在用户真正确认时至多调用一次，返回要显示的回复；不替作者提交或回滚业务。</summary>
public sealed record DialogueChoice(string Id,string Label,string Preview,Func<DialogueContext,string> Confirm,string? ConfirmLabel=null)
{
    /// <summary>当前游戏语言的可见选项文字；旧Label作为缺失回退。</summary>
    public LocalizedItemText? LocalizedLabel{get;init;}
    /// <summary>当前游戏语言的确认前说明；旧Preview作为缺失回退。</summary>
    public LocalizedItemText? LocalizedPreview{get;init;}
    /// <summary>该选项的双语确认按钮；省略时使用对话通用确认按钮。</summary>
    public LocalizedItemText? LocalizedConfirmLabel{get;init;}
}
public sealed record DialogueDefinition(string Id,string Speaker,string Body,IReadOnlyList<DialogueChoice> Choices,
    string ConfirmLabel="确认",string BackLabel="返回",string LeaveLabel="离开",string LaterLabel="稍后再说",string FailureText="操作未完成，请查看日志。")
{
    /// <summary>在展示时按当前游戏语言解析；创建脚本时不会缓存当前语言。</summary>
    public LocalizedItemText? LocalizedSpeaker{get;init;}
    /// <summary>Optional inline Chinese/English Body text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedBody{get;init;}
    /// <summary>Optional inline Chinese/English ConfirmLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedConfirmLabel{get;init;}
    /// <summary>Optional inline Chinese/English BackLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedBackLabel{get;init;}
    /// <summary>Optional inline Chinese/English LeaveLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedLeaveLabel{get;init;}
    /// <summary>Optional inline Chinese/English LaterLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedLaterLabel{get;init;}
    /// <summary>Optional inline Chinese/English FailureText text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedFailureText{get;init;}
    /// <summary>Optional inline Chinese/English NextPageLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedNextPageLabel{get;init;}
    /// <summary>Optional inline Chinese/English PreviousPageLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedPreviousPageLabel{get;init;}
    public bool RequireConfirmation{get;init;}=true;
    public bool AllowLater{get;init;}=true;
    public string NextPageLabel{get;init;}="下一页";
    public string PreviousPageLabel{get;init;}="上一页";
}
/// <summary>创建时复制不可变定义；本对象不持有原生对象，可用于后续当前周目的展示、电话或访客。</summary>
public sealed class DialogueScript
{
    public string OwnerId{get;}
    public DialogueDefinition Definition{get;}
    internal string? ContinueLabel{get;}
    internal LocalizedItemText? LocalizedContinueLabel{get;}
    internal string? LocaleCode{get;}
    internal string? AutomaticChoiceId{get;}
    internal Action<DialogueContext>? OnContinue{get;}
    internal Action<DialogueContext>? OnLeave{get;}
    internal DialogueScript(string owner,DialogueDefinition definition,string? continueLabel=null,
        Action<DialogueContext>? onContinue=null,Action<DialogueContext>? onLeave=null,string? automaticChoiceId=null,
        LocalizedItemText? localizedContinueLabel=null,string? localeCode=null)
        =>(OwnerId,Definition,ContinueLabel,OnContinue,OnLeave,AutomaticChoiceId,LocalizedContinueLabel,LocaleCode)=
            (owner,definition,continueLabel,onContinue,onLeave,automaticChoiceId,localizedContinueLabel,localeCode);
}
internal static class DialogueContract
{
    internal static DialogueScript Freeze(string owner,DialogueDefinition definition)
    {
        if(definition is null||!PresentationRules.Owned(owner,definition.Id)||
            !Text(definition.Speaker,512,definition.LocalizedSpeaker is null)||!Text(definition.Body,131072,false)||definition.Choices is null||
            definition.Choices.Count>128||definition.Choices.Count==0&&!definition.AllowLater)
            throw new ArgumentException("EZAPI_DIALOGUE_DEFINITION: owned ID, speaker, body and up to 128 choices required; empty menus need a leave choice.");
        foreach(var (label,localized) in new[]{(definition.ConfirmLabel,definition.LocalizedConfirmLabel),
            (definition.BackLabel,definition.LocalizedBackLabel),(definition.LeaveLabel,definition.LocalizedLeaveLabel),
            (definition.LaterLabel,definition.LocalizedLaterLabel),(definition.NextPageLabel,definition.LocalizedNextPageLabel),
            (definition.PreviousPageLabel,definition.LocalizedPreviousPageLabel)})
            if(!Text(label,512,localized is null))throw new ArgumentException("EZAPI_DIALOGUE_LABEL: bounded nonempty labels required.");
        if(!Text(definition.FailureText,131072,definition.LocalizedFailureText is null))throw new ArgumentException("EZAPI_DIALOGUE_FAILURE_TEXT");
        foreach(var text in new[]{definition.LocalizedSpeaker,definition.LocalizedConfirmLabel,
            definition.LocalizedBackLabel,definition.LocalizedLeaveLabel,definition.LocalizedLaterLabel,
            definition.LocalizedNextPageLabel,definition.LocalizedPreviousPageLabel})ValidateLocalized(text,512);
        ValidateLocalized(definition.LocalizedBody,131072);ValidateLocalized(definition.LocalizedFailureText,131072);
        var choices=new List<DialogueChoice>();var ids=new HashSet<string>(StringComparer.Ordinal);
        for(var i=0;i<definition.Choices.Count;i++) {
            var choice=definition.Choices[i];
            if(choice is null||!PresentationRules.Owner(choice.Id)||!ids.Add(choice.Id)||!Text(choice.Label,512,choice.LocalizedLabel is null)||
                choice.ConfirmLabel is not null&&!Text(choice.ConfirmLabel,512,choice.LocalizedConfirmLabel is null)||
                !Text(choice.Preview,131072,false)||choice.Confirm is null)throw new ArgumentException("EZAPI_DIALOGUE_CHOICE: unique IDs, bounded text and callback required.");
            ValidateLocalized(choice.LocalizedLabel,512);ValidateLocalized(choice.LocalizedPreview,131072);ValidateLocalized(choice.LocalizedConfirmLabel,512);
            choices.Add(choice with{});
        }
        return new(owner,definition with{Choices=Array.AsReadOnly(choices.ToArray())});
    }
    internal static string ResolveText(LocalizedItemText? text,string? fallback,string locale)
        =>text?.ForLocale(locale)??fallback??string.Empty;
    internal static DialogueScript ResolveLocale(DialogueScript script,string locale)
    {
        ArgumentNullException.ThrowIfNull(script);ArgumentNullException.ThrowIfNull(locale);
        var source=script.Definition;
        string Resolve(LocalizedItemText? text,string? fallback,string chineseDefault,string englishDefault)
            =>text?.ForLocale(locale)??(fallback==chineseDefault&& !locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?englishDefault:fallback??string.Empty);
        var choices=source.Choices.Select(choice=>choice with {
            Label=Resolve(choice.LocalizedLabel,choice.Label,"",""),
            Preview=Resolve(choice.LocalizedPreview,choice.Preview,"",""),
            ConfirmLabel=choice.LocalizedConfirmLabel is null?choice.ConfirmLabel:Resolve(choice.LocalizedConfirmLabel,choice.ConfirmLabel,"",""),
            LocalizedLabel=null,LocalizedPreview=null,LocalizedConfirmLabel=null
        }).ToArray();
        var definition=source with {
            Speaker=Resolve(source.LocalizedSpeaker,source.Speaker,"",""),
            Body=Resolve(source.LocalizedBody,source.Body,"",""),
            ConfirmLabel=Resolve(source.LocalizedConfirmLabel,source.ConfirmLabel,"确认","Confirm"),
            BackLabel=Resolve(source.LocalizedBackLabel,source.BackLabel,"返回","Back"),
            LeaveLabel=Resolve(source.LocalizedLeaveLabel,source.LeaveLabel,"离开","Leave"),
            LaterLabel=Resolve(source.LocalizedLaterLabel,source.LaterLabel,"稍后再说","Later"),
            FailureText=Resolve(source.LocalizedFailureText,source.FailureText,"操作未完成，请查看日志。","The action did not complete. Check the log."),
            NextPageLabel=Resolve(source.LocalizedNextPageLabel,source.NextPageLabel,"下一页","Next page"),
            PreviousPageLabel=Resolve(source.LocalizedPreviousPageLabel,source.PreviousPageLabel,"上一页","Previous page"),
            Choices=Array.AsReadOnly(choices),LocalizedSpeaker=null,LocalizedBody=null,LocalizedConfirmLabel=null,
            LocalizedBackLabel=null,LocalizedLeaveLabel=null,LocalizedLaterLabel=null,LocalizedFailureText=null,
            LocalizedNextPageLabel=null,LocalizedPreviousPageLabel=null
        };
        var continueLabel=script.LocalizedContinueLabel?.ForLocale(locale)??ResolveText(null,script.ContinueLabel,locale);
        return new(script.OwnerId,definition,continueLabel,script.OnContinue,script.OnLeave,script.AutomaticChoiceId,
            localeCode:locale);
    }
    private static void ValidateLocalized(LocalizedItemText? text,int limit)
    {if(text is null)return;text.Validate(limit);}
    private static bool Text(string? value,int limit,bool nonempty)=>PresentationRules.Text(value,limit)&&(!nonempty||!string.IsNullOrWhiteSpace(value));
}

/// <summary>独立于原生 UI 的展示执行寿命。先消费执行权再调用作者；异常也不会自动再次执行业务。</summary>
internal sealed class DialogueExecution
{
    internal readonly DialogueScript Script;
    internal readonly long Generation;
    internal readonly string Run,Request;
    internal readonly int Slot;
    private readonly HashSet<int> _committed=new();
    private readonly HashSet<int> _continued=new();
    private bool _active=true,_executing;
    private bool _left;
    internal bool Active=>_active;
    internal bool Executing=>_executing;
    internal int Confirmations=>_committed.Count;
    internal DialogueExecution(DialogueScript script,long generation,string run,int slot,string request)=>(Script,Generation,Run,Slot,Request)=(script,generation,run,slot,request);
    internal void Invalidate()=>_active=false;
    internal string Commit(int index,Func<bool> current,Action<string> error)
    {
        if(!_active||_executing||index<0||index>=Script.Definition.Choices.Count||_committed.Contains(index))
            return Script.Definition.FailureText;
        _executing=true;
        try {
            if(!current())return Script.Definition.FailureText;
            _committed.Add(index);
            var choice=Script.Definition.Choices[index];
            var context=new DialogueContext(Generation,Run,Slot,Script.OwnerId,Request,Script.Definition.Id,choice.Id)
                {LocaleCode=Script.LocaleCode??"en"};
            var result=choice.Confirm(context);
            if(!_active||!current())return Script.Definition.FailureText;
            if(!PresentationRules.Text(result,131072))throw new InvalidOperationException("EZAPI_DIALOGUE_REPLY: callback must return bounded non-null text without NUL.");
            return result;
        }catch(Exception e){try{error("EZAPI_DIALOGUE_CALLBACK: "+e);}catch{/* 日志提供者失败不能让原生回调越界抛出。 */}return Script.Definition.FailureText;}
        finally{_executing=false;}
    }

    internal void Continue(int index,Func<bool> current,Action<string> error)
    {
        if(!_active||_executing||index<0||index>=Script.Definition.Choices.Count||_continued.Contains(index))return;
        _executing=true;
        try{
            if(!current())return;
            _continued.Add(index);
            var choice=Script.Definition.Choices[index];
            Script.OnContinue?.Invoke(new(Generation,Run,Slot,Script.OwnerId,Request,Script.Definition.Id,choice.Id)
                {LocaleCode=Script.LocaleCode??"en"});
        }catch(Exception e){try{error("EZAPI_DIALOGUE_CONTINUE: "+e);}catch{/* Callback logging must not escape into native dialogue code. */}}
        finally{_active=false;_executing=false;}
    }

    internal void Leave(Func<bool> current,Action<string> error)
    {
        if(!_active||_executing||_left)return;
        _executing=true;
        try{
            if(!current())return;
            _left=true;
            Script.OnLeave?.Invoke(new(Generation,Run,Slot,Script.OwnerId,Request,Script.Definition.Id,string.Empty)
                {LocaleCode=Script.LocaleCode??"en"});
        }catch(Exception e){try{error("EZAPI_DIALOGUE_LEAVE: "+e);}catch{/* Callback logging must not escape into native dialogue code. */}}
        finally{_active=false;_executing=false;}
    }
}

internal enum DialogueAction { Navigate,Confirm,Continue,Leave }
internal sealed record DialogueEdge(string Label,int Target,DialogueAction Action=DialogueAction.Navigate,int Choice=-1);
internal sealed class DialoguePage
{
    internal string Body;
    internal readonly List<DialogueEdge> Edges=new();
    internal DialoguePage(string body)=>Body=body;
}
/// <summary>原生按钮容量内的有界对话图。预览/返回/分页不执行业务；确认后进入独立结果页。</summary>
internal sealed class DialoguePlan
{
    internal readonly List<DialoguePage> Pages=new();
    internal readonly Dictionary<int,int> Replies=new();
    internal readonly int Root;
    internal DialoguePlan(DialogueScript script,int capacity)
    {
        var definition=script.Definition;
        if(capacity<3||capacity>128)throw new ArgumentException("EZAPI_DIALOGUE_BUTTONS: expected 3..128 native choice buttons.");
        int Add(string body){Pages.Add(new(body));return Pages.Count-1;}
        Root=Add(definition.Body);
        var menu=new List<DialogueEdge>();
        for(var i=0;i<definition.Choices.Count;i++) {
            var option=definition.Choices[i];var reply=Add(definition.FailureText);Replies.Add(i,reply);
            Pages[reply].Edges.Add(new(definition.LeaveLabel,-1,DialogueAction.Leave));
            if(definition.RequireConfirmation) {
                var preview=Add(option.Preview);
                Pages[preview].Edges.Add(new(option.ConfirmLabel??definition.ConfirmLabel,reply,DialogueAction.Confirm,i));
                Pages[preview].Edges.Add(new(definition.BackLabel,Root));
                menu.Add(new(option.Label,preview));
            }else menu.Add(new(option.Label,reply,DialogueAction.Confirm,i));
        }
        if(definition.AllowLater)menu.Add(new(definition.LaterLabel,-1,DialogueAction.Leave));
        // 保留前/后页按钮空间，不克隆/扩大游戏共享按钮池。
        var size=menu.Count<=capacity?capacity:capacity-2;var count=(menu.Count+size-1)/size;
        var roots=new List<int>{Root};for(var i=1;i<count;i++)roots.Add(Add(definition.Body));
        for(var p=0;p<count;p++) {
            var edges=Pages[roots[p]].Edges;edges.AddRange(menu.Skip(p*size).Take(size));
            if(p>0)edges.Add(new(definition.PreviousPageLabel,roots[p-1]));
            if(p+1<count)edges.Add(new(definition.NextPageLabel,roots[p+1]));
            // 返回从本页选出的选项时回到本页，而不是强制回第一页。
            if(definition.RequireConfirmation)foreach(var edge in edges.Where(e=>e.Action==DialogueAction.Navigate&&e.Target>=0&&Pages[e.Target].Edges.Any(e=>e.Action==DialogueAction.Confirm)))
                Pages[edge.Target].Edges[1]=new(definition.BackLabel,roots[p]);
        }
        if(script.OnContinue is not null)
            for(var i=0;i<definition.Choices.Count;i++)
                Pages[Replies[i]].Edges.Insert(0,new(script.ContinueLabel!, -1, DialogueAction.Continue, i));
    }
}

using EZapi;
using System.Text.Json;
var cases=new List<object>();int failed=0;
void Check(bool ok,string name){cases.Add(new{name,passed=ok});if(!ok){failed++;Console.Error.WriteLine(name);}}
Fake Host()=>new();
PresentationResult Open(Fake h,string owner="author.reader",string id="author.reader.note",string run="run-a",int slot=7,string title="测试标题",string body="多行内容\n第二行",string sign="署名")=>h.Coordinator.Open(owner,id,run,slot,title,body,sign);
void Result(PresentationResult r,PresentationStatus expected,string name)=>Check(r.Status==expected,name+": "+r.Status);
foreach(var bad in new[]{"","a\n","A","a/../b","a..b",".a","a.",new string('a',161)}){
 var h=Host();Result(Open(h,bad,bad+".note"),PresentationStatus.Invalid,"invalid owner "+JsonSerializer.Serialize(bad));Check(h.Documents.Count==0,"invalid owner no creation "+JsonSerializer.Serialize(bad));
}
foreach(var bad in new[]{"","author.other.note","author.reader","author.reader.\n","author.reader/escape"}){
 var h=Host();Result(Open(h,id:bad),PresentationStatus.Invalid,"invalid owned ID "+JsonSerializer.Serialize(bad));Check(h.Documents.Count==0,"invalid owned ID no creation "+JsonSerializer.Serialize(bad));
}
{
 var h=Host();Result(Open(h,run:""),PresentationStatus.Invalid,"empty run");Result(Open(h,slot:-1),PresentationStatus.Invalid,"negative slot");Result(Open(h,title:new string('x',513)),PresentationStatus.Invalid,"title limit");Result(Open(h,body:new string('x',131073)),PresentationStatus.Invalid,"body limit");Result(Open(h,sign:new string('x',4097)),PresentationStatus.Invalid,"signature limit");
 foreach(var field in new[]{"title","body","signature"})Result(Open(h,title:field=="title"?"\0":"T",body:field=="body"?"\0":"B",sign:field=="signature"?"\0":"S"),PresentationStatus.Invalid,"NUL "+field);
 Check(h.Documents.Count==0,"invalid texts never allocate");Result(Open(h,title:new string('x',512),body:new string('x',131072),sign:new string('x',4096)),PresentationStatus.Opened,"text exact limits accepted");h.Coordinator.Reset();Check(!h.Coordinator.HasDocument&&h.Documents.All(d=>d.Releases==1),"limit fixture cleanup");
}
{
 var h=Host();h.Env=h.Env with{Ready=false};Result(Open(h),PresentationStatus.Unavailable,"hooks unavailable");h.Env=h.Env with{Ready=true,Scope=null};Result(Open(h),PresentationStatus.Unavailable,"run not observed");h.Env=new(true,new("run-a",7,1),0,false);Result(Open(h),PresentationStatus.Unavailable,"no native panel");Check(h.Documents.Count==0,"not ready no mutation");
}
{
 var h=Host();Result(Open(h,run:"other"),PresentationStatus.Conflict,"wrong run");Result(Open(h,slot:8),PresentationStatus.Conflict,"wrong slot");h.Env=h.Env with{Dragging=true};Result(Open(h),PresentationStatus.Conflict,"active drag");Check(h.Documents.Count==0,"identity/drag no mutation");
}
{
 var h=Host();h.Foreign(99);Result(Open(h),PresentationStatus.Conflict,"original/foreign visible priority");Result(h.Coordinator.Close("author.reader","author.reader.note"),PresentationStatus.Closed,"close missing own doc idempotent");Check(h.Env.Visible&&h.Display==99&&h.CloseCalls==0,"foreign panel not closed");
}
{
 var h=Host();Result(Open(h),PresentationStatus.Opened,"native open readback");var first=h.Documents.Single();Check(h.Coordinator.HasDocument&&h.Display==first.Token&&first.Releases==0,"retain visible document");
 Result(Open(h,"other.reader","other.reader.note"),PresentationStatus.Conflict,"other author cannot replace visible document");Result(Open(h,id:"author.reader.other"),PresentationStatus.Conflict,"other document cannot replace visible document");
 Result(h.Coordinator.Close("other.reader","other.reader.note"),PresentationStatus.Conflict,"other author cannot close");Check(h.Display==first.Token&&first.Releases==0,"rejected calls preserve visible item");
 Result(Open(h,body:"updated"),PresentationStatus.Opened,"same document refresh");Check(first.Releases==1&&h.Documents.Count==2&&h.Documents[1].Releases==0,"refresh releases old after switching display");
 Result(h.Coordinator.Close("author.reader","author.reader.note"),PresentationStatus.Closed,"close own native panel");Check(!h.Coordinator.HasDocument&&h.Documents.All(d=>d.Releases==1),"closed resources released once");Result(h.Coordinator.Close("author.reader","author.reader.note"),PresentationStatus.Closed,"repeat close idempotent");Check(h.CloseCalls==1,"idempotent close no native call");
}
{
 var h=Host();Open(h);h.UserClose();h.Coordinator.Maintain();Check(!h.Coordinator.HasDocument&&h.Documents[0].Releases==1,"user native close releases once");h.Coordinator.Maintain();Check(h.Documents[0].Releases==1,"idle maintenance no repeated destroy");
}
{
 var h=Host();Open(h);h.Foreign(99);Result(h.Coordinator.Close("author.reader","author.reader.note"),PresentationStatus.Conflict,"foreign replacement close conflicts");Check(h.Display==99&&h.CloseCalls==0&&h.Documents[0].Releases==1,"foreign replacement frees own item only");
}
foreach(var mode in new[]{"epoch","run","slot","missing","panel"}){
 var h=Host();Open(h);h.Env=mode switch{"epoch"=>h.Env with{Scope=new("run-a",7,2)},"run"=>h.Env with{Scope=new("run-b",7,2)},"slot"=>h.Env with{Scope=new("run-a",8,2)},"missing"=>h.Env with{Scope=null},_=>h.Env with{Panel=22,Visible=false}};
 h.Coordinator.Maintain();Check(!h.Coordinator.HasDocument&&h.Documents[0].Releases==1,mode+" invalidates lease");Check(h.CloseCalls==(mode=="panel"?0:1),mode+" closes only exact owned native panel");
}
{
 var h=Host();PresentationResult? nested=null;h.OnCreate=()=>nested=Open(h);Result(Open(h),PresentationStatus.Opened,"outer survives creation reentry");Check(nested?.Status==PresentationStatus.Conflict&&h.Documents.Count==1,"creation reentry rejected before mutation");h.Coordinator.Reset();
}
foreach(var when in new[]{"create","open","close"}){
 var h=Host();PresentationResult? nested=null;
 if(when=="create")h.OnCreate=()=>nested=h.Coordinator.Close("author.reader","author.reader.note");
 if(when=="open")h.OnOpen=()=>nested=Open(h);
 if(when=="close")h.OnClose=()=>nested=Open(h);
 Result(Open(h),PresentationStatus.Opened,when+" outer open");Result(h.Coordinator.Close("author.reader","author.reader.note"),PresentationStatus.Closed,when+" outer close");Check(nested?.Status==PresentationStatus.Conflict&&h.Documents.All(d=>d.Releases==1),when+" reentry rejected and cleanup");
}
foreach(var when in new[]{"create","open"}){
 var h=Host();if(when=="create")h.OnCreate=()=>h.Coordinator.Reset();else h.OnOpen=()=>h.Coordinator.Reset();Result(Open(h),PresentationStatus.Indeterminate,when+" reset during mutation");Check(!h.Coordinator.HasDocument&&!h.Env.Visible&&h.Documents.All(d=>d.Releases==1),when+" reset drains owned lease");
}
{
 var h=Host();h.OnCreate=()=>h.Foreign(99);Result(Open(h),PresentationStatus.Indeterminate,"foreign takeover during creation");Check(h.OpenCalls==0&&h.Display==99&&h.Documents[0].Releases==1,"takeover never overwritten");
}
{
 var h=Host();h.OnCreate=()=>h.Env=h.Env with{Scope=new("new-run",7,2)};Result(Open(h),PresentationStatus.Indeterminate,"identity changes during creation");Check(h.OpenCalls==0&&h.Documents[0].Releases==1,"changed scope does not display old content");
}
{
 var h=Host();h.FailCreate=true;Result(Open(h),PresentationStatus.Indeterminate,"factory exception reported");Check(!h.Coordinator.HasDocument,"factory failure leaves no lease");h.FailCreate=false;Result(Open(h),PresentationStatus.Opened,"factory failure can retry");h.Coordinator.Reset();
}
foreach(var afterDisplay in new[]{false,true}){
 var h=Host();h.FailOpen=afterDisplay?2:1;Result(Open(h),PresentationStatus.Indeterminate,"native open throws "+afterDisplay);Check(h.Coordinator.HasDocument==afterDisplay,"open throw retains only actually displayed document "+afterDisplay);h.FailOpen=0;h.Coordinator.Reset();Check(h.Documents[0].Releases==1&&!h.Env.Visible,"open throw cleanup "+afterDisplay);
}
{
 var h=Host();h.RefuseOpen=true;Result(Open(h),PresentationStatus.Indeterminate,"native rejects open");Check(!h.Coordinator.HasDocument&&h.Documents[0].Releases==1,"rejected open destroys detached temporary");
}
{
 var h=Host();Open(h);var first=h.Documents[0];h.RefuseOpen=true;Result(Open(h,body:"refresh"),PresentationStatus.Indeterminate,"native rejects refresh");Check(h.Coordinator.HasDocument&&h.Display==first.Token&&first.Releases==0&&h.Documents[1].Releases==1,"rejected refresh preserves original displayed item");h.RefuseOpen=false;h.Coordinator.Reset();
}
{
 var h=Host();Open(h);h.RefuseClose=true;Result(h.Coordinator.Close("author.reader","author.reader.note"),PresentationStatus.Indeterminate,"native refuses close");Check(h.Coordinator.HasDocument&&h.Documents[0].Releases==0,"never destroy visible note after failed close");h.Coordinator.Reset();Check(h.Coordinator.HasDocument&&h.Documents[0].Releases==0,"reset also preserves unclosed note");h.RefuseClose=false;h.Coordinator.Reset();Check(!h.Coordinator.HasDocument&&h.Documents[0].Releases==1,"explicit later reset cleans once");
}
{
 var h=Host();Open(h);h.Documents[0].FailRelease=true;h.UserClose();h.Coordinator.Maintain();Check(h.Coordinator.ReleaseFailures==1,"cleanup failure recorded");Result(Open(h),PresentationStatus.Unavailable,"cleanup fault blocks unbounded allocations");h.Coordinator.Reset();Check(h.Documents[0].ReleaseAttempts==1,"failed destroy never automatically retried");
}
foreach(var field in new[]{"title","body","signature"}){
 var h=Host();h.OnOpen=()=>h.Env=h.Env with{Title=field=="title"?"changed":"测试标题",Body=field=="body"?"changed":"多行内容\n第二行",Signature=field=="signature"?"changed":"署名"};
 Result(Open(h),PresentationStatus.Indeterminate,"visible native text mismatch "+field);
 Check(h.Coordinator.HasDocument&&h.Documents[0].Releases==0,"mismatched text still retains visible document "+field);
 h.Coordinator.Reset();Check(!h.Coordinator.HasDocument&&h.Documents[0].Releases==1,"mismatch cleanup "+field);
}
{
 var h=Host();h.OnOpen=()=>h.Env=h.Env with{Title="测试标题",Body="多行内容\n第二行",Signature="署名"};Result(Open(h),PresentationStatus.Opened,"all displayed text matches request");h.Coordinator.Reset();
}
foreach(var refuse in new[]{false,true}){
 var h=Host();Open(h);var first=h.Documents[0];h.RefuseOpen=refuse;h.OnOpen=()=>h.InspectFailures=2;
 Result(Open(h,body:"readback fails"),PresentationStatus.Indeterminate,"refresh inspect error "+refuse);
 Check(h.Coordinator.RetainedDocuments==2&&h.Documents.All(d=>d.ReleaseAttempts==0),"both handles retained during unreadable UI "+refuse);
 Result(Open(h),PresentationStatus.Unavailable,"pending native readback blocks allocation "+refuse);
 Check(h.Documents.Count==2,"no third document allocated "+refuse);
 h.Coordinator.Maintain();Check(h.Coordinator.RetainedDocuments==1,"later successful inspection reconciles handles "+refuse);
 Check(first.Releases==(refuse?0:1)&&h.Documents[1].Releases==(refuse?1:0),"only unshown document released "+refuse);
 h.OnOpen=null;h.Coordinator.Reset();Check(!h.Coordinator.HasDocument&&h.Documents.All(d=>d.Releases==1),"deferred inspection resources released once "+refuse);
}
{
 var h=Host();PresentationResult? nested=null;Open(h);h.OnRelease=()=>nested=Open(h);h.UserClose();h.Coordinator.Maintain();
 Check(nested?.Status==PresentationStatus.Conflict&&h.Documents.Count==1,"release callback cannot reenter allocation");
}
{
 var h=Host();Open(h);h.OnOpen=()=>h.InspectFailures=2;Open(h,body:"pending");h.Coordinator.Reset();
 Check(!h.Coordinator.HasDocument&&h.Documents.All(d=>d.Releases==1),"reset reconciles unreadable refresh and closes current document");
}
ReadingLocalizationChecks.Run(Check,(action,name)=>{try{action();Check(false,name);}catch(ArgumentException){Check(true,name);}});
var report=new{scope="real linked ReadingCoordinator and ReadingTextResolution; deterministic host interleavings; does not validate native panel or Harmony installation",framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,passed=cases.Count-failed,failed,cases};Console.WriteLine(JsonSerializer.Serialize(report));Environment.ExitCode=failed==0?0:1;

sealed class Doc:IReadingDocument
{
 public long Token{get;}public int Releases,ReleaseAttempts;public bool FailRelease;private readonly Fake _host;
 public Doc(Fake host,long token)=>(_host,Token)=(host,token);
 public void Release(){ReleaseAttempts++;if(_host.Env.Visible&&_host.Display==Token)throw new InvalidOperationException("use-after-free prevented by host");if(FailRelease)throw new InvalidOperationException("expected release failure");Releases++;if(Releases>1)throw new InvalidOperationException("double release");_host.OnRelease?.Invoke();}
}
sealed class Fake:IReadingHost
{
 public readonly ReadingCoordinator Coordinator;public ReadingEnvironment Env=new(true,new("run-a",7,1),11,false);public long Display;public int OpenCalls,CloseCalls,FailOpen,InspectFailures;public bool RefuseOpen,RefuseClose,FailCreate;public Action? OnCreate,OnOpen,OnClose,OnRelease;public readonly List<Doc> Documents=new();public readonly List<string> Errors=new();
 public Fake()=>Coordinator=new(this);
 public ReadingEnvironment Inspect(){if(InspectFailures>0){InspectFailures--;throw new InvalidOperationException("expected native inspect failure");}return Env;}
 public IReadingDocument Create(string a,string b,string c){if(FailCreate)throw new InvalidOperationException("expected create failure");var doc=new Doc(this,100+Documents.Count);Documents.Add(doc);OnCreate?.Invoke();return doc;}
 public void Open(long panel,IReadingDocument doc){OpenCalls++;if(FailOpen==1)throw new InvalidOperationException("expected pre-display error");if(!RefuseOpen){Env=Env with{Visible=true};Display=doc.Token;Coordinator.Opened(panel,doc.Token);}OnOpen?.Invoke();if(FailOpen==2)throw new InvalidOperationException("expected post-display error");}
 public void Close(long panel){CloseCalls++;if(!RefuseClose){Env=Env with{Visible=false};Display=0;Coordinator.Closed(panel);}OnClose?.Invoke();}
 public void Foreign(long token){Display=token;Env=Env with{Visible=true};Coordinator.Opened(Env.Panel,token);}
 public void UserClose(){Env=Env with{Visible=false};Display=0;Coordinator.Closed(Env.Panel);}
 public void Error(string message)=>Errors.Add(message);
}

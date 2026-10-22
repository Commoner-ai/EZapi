using EZapi;
using System.Text.Json;
var cases=new List<object>();int failed=0;
void Check(bool ok,string name){cases.Add(new{name,passed=ok});if(!ok){failed++;Console.Error.WriteLine(name);}}
void Reject(Action action,string name){try{action();Check(false,name);}catch(ArgumentException){Check(true,name);}}
DialogueDefinition Def(int count=1)=>new("author.dialogue","Speaker","Body",Enumerable.Range(0,count).Select(i=>new DialogueChoice("choice"+i,"Label "+i,"Preview "+i,c=>"Reply "+c.ChoiceId)).ToArray());
DialogueScript Freeze(DialogueDefinition? d=null)=>DialogueContract.Freeze("author",d??Def());
foreach(var owner in new[]{"","a\n","A","a..b","a/../b",new string('x',161)})Reject(()=>DialogueContract.Freeze(owner,Def()),"invalid owner "+JsonSerializer.Serialize(owner));
foreach(var id in new[]{"author","other.dialogue","author.x\n","author./x"})Reject(()=>Freeze(Def() with{Id=id}),"invalid owned id "+id);
Reject(()=>Freeze(Def(129)),"invalid count 129");
Reject(()=>Freeze(Def(0) with{AllowLater=false}),"empty menu without leave action rejected");
Reject(()=>Freeze(Def() with{Choices=null!}),"null choices");
Reject(()=>Freeze(Def() with{Choices=new DialogueChoice[]{null!}}),"null choice");
Reject(()=>Freeze(Def() with{Choices=new[]{Def().Choices[0],Def().Choices[0]}}),"duplicate choice IDs");
foreach(var field in new[]{"speaker","body","confirm","back","leave","later","next","previous","failure","label","preview","choiceId"})foreach(var kind in new[]{"null","NUL","oversize"}) {
 var limit=field is "body" or "failure" or "preview"?131072:field=="choiceId"?160:512;
 string bad=kind=="null"?null!:kind=="NUL"?"a\0":new string('x',limit+1);var d=Def();
 d=field switch{"speaker"=>d with{Speaker=bad},"body"=>d with{Body=bad},"confirm"=>d with{ConfirmLabel=bad},"back"=>d with{BackLabel=bad},"leave"=>d with{LeaveLabel=bad},"later"=>d with{LaterLabel=bad},"next"=>d with{NextPageLabel=bad},"previous"=>d with{PreviousPageLabel=bad},"failure"=>d with{FailureText=bad},_=>d with{Choices=new[]{field switch{"label"=>d.Choices[0] with{Label=bad},"preview"=>d.Choices[0] with{Preview=bad},_=>d.Choices[0] with{Id=bad}}}}};
 Reject(()=>Freeze(d),field+" rejects "+kind);
}
Reject(()=>Freeze(Def() with{Choices=new[]{Def().Choices[0] with{Confirm=null!}}}),"null callback");
{
 var choices=Def(2).Choices.ToArray();var script=Freeze(Def(2) with{Choices=choices});choices[0]=choices[0] with{Label="changed"};Check(script.Definition.Choices[0].Label=="Label 0","caller array mutation isolated");
 Check(script.Definition.Choices is not DialogueChoice[],"frozen list not exposed as mutable array");
}
foreach(var count in new[]{1,2,3,6,9,32,127,128})foreach(var cap in new[]{3,4,6,8,16,128})foreach(var confirm in new[]{false,true})foreach(var later in new[]{false,true}) {
 var script=Freeze(Def(count) with{RequireConfirmation=confirm,AllowLater=later});var d=script.Definition;var plan=new DialoguePlan(script,cap);var label=$"count={count},capacity={cap},confirm={confirm},later={later}";
 Check(plan.Pages.All(p=>p.Edges.Count<=cap),"all pages fit native buttons "+label);
 var seen=new HashSet<int>();var queue=new Queue<int>();queue.Enqueue(plan.Root);
 while(queue.Count>0){var n=queue.Dequeue();if(!seen.Add(n))continue;foreach(var e in plan.Pages[n].Edges)if(e.Target>=0)queue.Enqueue(e.Target);}
 Check(seen.Count==plan.Pages.Count,"every page reachable "+label);
 Check(plan.Replies.Count==count&&plan.Replies.All(kv=>seen.Contains(kv.Value)),"every reply reachable "+label);
 Check(plan.Pages.SelectMany(p=>p.Edges).Count(e=>e.Action==DialogueAction.Confirm)==count,"one confirm edge per choice "+label);
 Check(plan.Pages.SelectMany(p=>p.Edges).Where(e=>e.Action==DialogueAction.Confirm).All(e=>e.Target==plan.Replies[e.Choice]),"confirm enters matching reply "+label);
 if(confirm){bool valid=true;for(int n=0;n<plan.Pages.Count;n++)foreach(var e in plan.Pages[n].Edges)if(e.Target>=0&&plan.Pages[e.Target].Edges.Any(e=>e.Action==DialogueAction.Confirm))valid&=plan.Pages[e.Target].Edges[1].Target==n;Check(valid,"preview returns to exact source menu "+label);}
 Check(plan.Pages.Count<1024,"bounded native graph "+label);
 if(count+(later?1:0)<=cap)Check(plan.Pages[plan.Root].Edges.Count==count+(later?1:0),"single page uses full capacity "+label);
}
foreach(var cap in new[]{-1,0,1,2,129})Reject(()=>new DialoguePlan(Freeze(Def()),cap),"bad native capacity "+cap);
{
 int calls=0;DialogueContext? context=null;var script=Freeze(Def() with{Choices=new[]{new DialogueChoice("confirm","Label","Preview",c=>{calls++;context=c;return "Done";})}});var execution=new DialogueExecution(script,77,"run",5,"author.request");
 Check(execution.Commit(-1,()=>true,_=>{})==script.Definition.FailureText&&calls==0,"negative index no callback");Check(execution.Commit(1,()=>true,_=>{})==script.Definition.FailureText&&calls==0,"past-end index no callback");
 Check(execution.Commit(0,()=>false,_=>{})==script.Definition.FailureText&&calls==0,"invalid scope no callback");Check(execution.Commit(0,()=>true,_=>{})=="Done"&&calls==1,"valid callback once");
 Check(context==new DialogueContext(77,"run",5,"author","author.request","author.dialogue","confirm"),"all callback identity fields exact");
 execution.Commit(0,()=>true,_=>{});Check(calls==1&&execution.Confirmations==1,"duplicate callback denied");execution.Invalidate();execution.Commit(0,()=>true,_=>{});Check(calls==1&&!execution.Active,"invalidated execution cannot commit");
}
foreach(var mode in new[]{"throw","null","NUL","oversize","invalidate","reenter"}) {
 int calls=0,errors=0;DialogueExecution? execution=null;
 var script=Freeze(Def() with{Choices=new[]{new DialogueChoice("c","Label","Preview",c=>{calls++;if(mode=="throw")throw new Exception("expected");if(mode=="invalidate")execution!.Invalidate();if(mode=="reenter")execution!.Commit(0,()=>true,_=>{});return mode switch{"null"=>null!,"NUL"=>"\0","oversize"=>new string('x',131073),_=>"OK"};})}});
 execution=new(script,1,"run",1,"author.request");var result=execution.Commit(0,()=>true,_=>errors++);execution.Commit(0,()=>true,_=>errors++);
 Check(calls==1&&!execution.Executing&&execution.Confirmations==1,mode+" never repeats business");Check(result==(mode=="reenter"?"OK":script.Definition.FailureText),mode+" result safe");Check(errors==(mode is "throw" or "null" or "NUL" or "oversize"?1:0),mode+" diagnostic count");
}
{
 int calls=0;var execution=new DialogueExecution(Freeze(Def() with{Choices=new[]{new DialogueChoice("c","L","P",_=>{calls++;throw new Exception();})}}),1,"run",1,"author.r");
 Check(execution.Commit(0,()=>throw new Exception(),_=>throw new Exception())==execution.Script.Definition.FailureText&&calls==0&&!execution.Executing,"scope and diagnostic exceptions contained");
 execution.Commit(0,()=>true,_=>throw new Exception());Check(calls==1&&!execution.Executing,"throwing diagnostic cannot escape callback");
}
LocalizationContractChecks.Run(Check,Reject);
Console.WriteLine(JsonSerializer.Serialize(new{suite="dialogue",runtime=Environment.Version.ToString(),passed=cases.Count-failed,failed,cases},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;

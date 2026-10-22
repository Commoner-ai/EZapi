using EZapi;
using System.Text.Json;

var cases=new List<object>();var failed=0;var checks=0;
void Check(bool ok,string name){checks++;cases.Add(new{name,passed=ok});if(!ok){failed++;Console.Error.WriteLine("FAILED: "+name);}}
void Reject(Action action,string name){try{action();Check(false,name);}catch(ArgumentException){Check(true,name);}}
SupplierOffer Offer(string id="author.offer.one",string? confirm=null)=>new(id,"Trade","Preview",_=>"Sold",confirm);
SupplierPage Page(params SupplierOffer[] offers)=>new("Market",offers);
SupplierDefinition Definition(Func<DialogueContext,SupplierPage>? capture=null)=>new("author.supplier","Vendor",capture??(_=>Page(Offer())),_=>{});

foreach(var owner in new[]{"","A","bad/owner","author..x",new string('x',161)})
    Reject(()=>SupplierContract.Freeze(owner,Definition()),"invalid owner "+JsonSerializer.Serialize(owner));
foreach(var id in new[]{"author","other.supplier","author.bad\n"})
    Reject(()=>SupplierContract.Freeze("author",Definition() with{Id=id}),"invalid owned supplier id "+JsonSerializer.Serialize(id));
foreach(var field in new[]{"speaker","confirm","back","continue","leave","failure","next","previous"})
{
    var d=Definition();var bad=(string?)null;
    d=field switch{"speaker"=>d with{Speaker=bad!},"confirm"=>d with{ConfirmLabel=bad!},"back"=>d with{BackLabel=bad!},
        "continue"=>d with{ContinueLabel=bad!},"leave"=>d with{LeaveLabel=bad!},"failure"=>d with{FailureText=bad!},
        "next"=>d with{NextPageLabel=bad!},_=>d with{PreviousPageLabel=bad!}};
    Reject(()=>SupplierContract.Freeze("author",d),"required definition text "+field);
}

{
    var source=new[]{Offer(confirm:"立即确认")};var frozen=SupplierContract.FreezePage(Page(source) with{AutomaticOfferId="author.offer.one",AutomaticDelaySeconds=0.25});
    source[0]=Offer("author.changed");
    Check(frozen.Offers.Count==1&&frozen.Offers[0].Id=="author.offer.one","page copies the offer collection");
    Check(frozen.AutomaticOfferId=="author.offer.one"&&frozen.AutomaticDelaySeconds==0.25,"automatic offer snapshot retained");
    Check(frozen.Offers[0].ConfirmLabel=="立即确认","per-offer confirmation text retained");
    Check(SupplierContract.FreezePage(Page()).Offers.Count==0,"empty page is valid and can expose leave only");
}
Reject(()=>SupplierContract.FreezePage(new SupplierPage("x",new[]{Offer(),Offer()})),"duplicate offer IDs rejected");
Reject(()=>SupplierContract.FreezePage(Page(Offer("bad id"))),"invalid offer ID rejected");
Reject(()=>SupplierContract.FreezePage(Page(Offer()) with{AutomaticOfferId="author.missing"}),"automatic offer must exist on page");
Reject(()=>SupplierContract.FreezePage(Page(Offer()) with{AutomaticDelaySeconds=double.NaN}),"non-finite automatic delay rejected");
Reject(()=>SupplierContract.FreezePage(Page(Offer()) with{AutomaticDelaySeconds=-1}),"negative automatic delay rejected");
Reject(()=>SupplierContract.FreezePage(Page(Offer()) with{AutomaticDelaySeconds=1}),"delay without auto offer rejected");
Reject(()=>SupplierContract.FreezePage(Page(Offer()) with{Offers=Enumerable.Range(0,129).Select(i=>Offer("author.offer."+i)).ToArray()}),"offer count is bounded");
Reject(()=>SupplierContract.FreezePage(Page(Offer()) with{Offers=new[]{Offer() with{Purchase=null!}}}),"purchase callback is required");

{
    var definition=SupplierContract.Freeze("author",Definition() with{AllowMenuRefresh=true});
    var offers=new[]{Offer() with{ConfirmLabel="接受"}};
    var dialogue=new DialogueDefinition(definition.Id,definition.Speaker,"Market",
        offers.Select(x=>new DialogueChoice(x.Id,x.Label,x.Preview,x.Purchase,x.ConfirmLabel)).ToArray())
        {AllowLater=true,RequireConfirmation=definition.RequireConfirmation};
    var frozen=DialogueContract.Freeze("author",dialogue);
    var script=new DialogueScript("author",frozen.Definition,"继续交易",_=>{},_=>{},"author.offer.one");
    var plan=new DialoguePlan(script,4);
    var reply=plan.Pages[plan.Replies[0]];
    Check(reply.Edges.Any(x=>x.Action==DialogueAction.Continue&&x.Label=="继续交易"&&x.Choice==0),"reply page exposes deferred continue edge");
    Check(reply.Edges.Any(x=>x.Action==DialogueAction.Leave&&x.Label==definition.LeaveLabel),"reply page keeps leave action");
    Check(plan.Pages[plan.Root].Edges.Any(x=>x.Action==DialogueAction.Navigate&&x.Target>=0),"offer opens a confirmation preview");
}

{
    var continueCalls=0;DialogueContext? continueContext=null;var leaveCalls=0;DialogueContext? leaveContext=null;
    var script=new DialogueScript("author",DialogueContract.Freeze("author",new DialogueDefinition("author.supplier","Vendor","Menu",
        new[]{new DialogueChoice("author.offer.one","Offer","Preview",_=>"Bought")})).Definition,
        "Continue",context=>{continueCalls++;continueContext=context;},context=>{leaveCalls++;leaveContext=context;});
    var execution=new DialogueExecution(script,41,"run",3,"author.request");
    execution.Continue(0,()=>true,_=>{});execution.Continue(0,()=>true,_=>{});
    Check(continueCalls==1&&!execution.Active&&!execution.Executing,"continue callback is consumed once and ends the current graph");
    Check(continueContext==new DialogueContext(41,"run",3,"author","author.request","author.supplier","author.offer.one"),"continue callback receives scoped identity");

    var leaveExecution=new DialogueExecution(script,42,"run",4,"author.request");
    leaveExecution.Leave(()=>true,_=>{});leaveExecution.Leave(()=>true,_=>{});
    Check(leaveCalls==1&&!leaveExecution.Active&&!leaveExecution.Executing,"leave callback is consumed once and ends the graph");
    Check(leaveContext==new DialogueContext(42,"run",4,"author","author.request","author.supplier",string.Empty),"leave callback receives visit identity without a choice");
}

{
    var choices=new[]{new DialogueChoice("author.offer.one","Offer","Preview",_=>"Bought","Accept")};
    var frozen=DialogueContract.Freeze("author",new DialogueDefinition("author.supplier","Vendor","Menu",choices));
    Check(frozen.Definition.Choices[0].ConfirmLabel=="Accept","choice-specific confirm label survives freeze");
    Reject(()=>DialogueContract.Freeze("author",new DialogueDefinition("author.supplier","Vendor","Menu",
        new[]{choices[0] with{ConfirmLabel=" "}})),"blank choice-specific confirm label rejected");
}

Console.WriteLine(JsonSerializer.Serialize(new{suite="supplier-api",runtime=Environment.Version.ToString(),passed=checks-failed,failed,cases,
    nativeRuntimeVerified=false,scope="real SupplierContract, DialogueContract, DialoguePlan and DialogueExecution; no game process"},new JsonSerializerOptions{WriteIndented=true}));
return failed==0?0:1;

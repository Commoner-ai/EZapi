using EZapi;
using Il2Cpp;
using System.Text;
using System.Text.Json;
var cases=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
void Reject(Action run,string name){try{run();}catch(Exception e)when(e is ArgumentException or InvalidDataException or JsonException or NotSupportedException){cases.Add(name);return;}throw new Exception("Expected rejection: "+name);}
RequirementDefinition Leaf(string kind="upgrade-unlocked",string? target="upgrade",long? amount=null)=>new(){Kind=kind,TargetId=target,Amount=amount};
RequirementDefinition Group(string kind,params RequirementDefinition[] children)=>new(){Kind=kind,Children=children};
RequirementDefinition Parse(string json){using var s=new MemoryStream(Encoding.UTF8.GetBytes(json));return Requirements.Parse(s);}
var upgrade=new NetworkUpgrade();NetworkUpgrade.Items["upgrade"]=upgrade;var service=new StoreService();StoreService.Items["service"]=service;
var captured=ProgressionApi.ReadUpgrade("upgrade");Check(captured.Status==ProgressionReadStatus.Ready&&captured.Run==GameSession.Identity&&captured.Value!.Cost==20&&!captured.Value.Unlocked&&captured.Value.Ready,"read native upgrade predicates independently");
Check(captured.Value!.Prerequisites.SequenceEqual(new[]{"base"})&&captured.Value.CooldownRemaining==2&&captured.Value.Repeatable,"upgrade snapshot contains prerequisites and raw cooldown");upgrade.prerequisite!.Add("later");Check(captured.Value.Prerequisites.Count==1,"snapshot detaches prerequisite collection");Reject(()=>((IList<string>)captured.Value.Prerequisites).Add("bad"),"snapshot rejects prerequisite writes");
var srow=ProgressionApi.ReadService("service");Check(srow.Status==ProgressionReadStatus.Ready&&srow.Value!.Unlocked&&srow.Value.Introduced&&!srow.Value.Using&&!srow.Value.Active&&srow.Value.OnCooldown,"service distinguishes unlocked/introduced/using/active/cooldown");
Check(ProgressionApi.ReadUpgrade("missing").Status==ProgressionReadStatus.Missing&&ProgressionApi.ReadService("missing").Status==ProgressionReadStatus.Missing,"unknown IDs remain missing");
foreach(var id in new string?[]{null,"","a b","a\nb",new string('x',161)})Check(ProgressionApi.ReadUpgrade(id!).Status==ProgressionReadStatus.Invalid&&ProgressionApi.ReadService(id!).Status==ProgressionReadStatus.Invalid,"invalid ID rejected before native lookup "+id?.Length);
GameBuildFingerprint.IsKnown=false;int prior=NetworkUpgrade.Reads;Check(!ProgressionApi.IsAvailable&&ProgressionApi.ReadUpgrade("upgrade").Status==ProgressionReadStatus.Unavailable&&NetworkUpgrade.Reads==prior,"unsupported build cannot touch native upgrade");GameBuildFingerprint.IsKnown=true;
RunData.IsAvailable=false;Check(ProgressionApi.ReadService("service").Status==ProgressionReadStatus.Unavailable,"unloaded run cannot touch service");RunData.IsAvailable=true;
Check(Task.Run(()=>!ProgressionApi.IsAvailable&&ProgressionApi.ReadUpgrade("upgrade").Status==ProgressionReadStatus.Unavailable).GetAwaiter().GetResult(),"worker native query fails closed");
upgrade.id="wrong";Check(ProgressionApi.ReadUpgrade("upgrade").Status==ProgressionReadStatus.Unavailable,"wrong returned upgrade identity rejected");upgrade.id="upgrade";
upgrade.prerequisite=new(){"base","base"};Check(ProgressionApi.ReadUpgrade("upgrade").Status==ProgressionReadStatus.Unavailable,"ambiguous prerequisite list rejected");upgrade.prerequisite=new(){"base"};
upgrade.prerequisite=Enumerable.Range(0,129).Select(i=>"p"+i).ToList();Check(ProgressionApi.ReadUpgrade("upgrade").Status==ProgressionReadStatus.Unavailable,"bounded native prerequisite list");upgrade.prerequisite=new(){"base"};
upgrade.cost=-1;Check(ProgressionApi.ReadUpgrade("upgrade").Status==ProgressionReadStatus.Unavailable,"invalid native price rejected");upgrade.cost=20;
NetworkUpgrade.OnRead=()=>GameSession.Identity=GameSession.Identity with{Epoch=GameSession.Identity.Epoch+1};Check(ProgressionApi.ReadUpgrade("upgrade").Status==ProgressionReadStatus.Unavailable,"run switch during lookup rejected");NetworkUpgrade.OnRead=null;
StoreService.OnRead=()=>throw new InvalidOperationException("native failure");Check(ProgressionApi.ReadService("service").Status==ProgressionReadStatus.Unavailable,"native read exception does not expose partial snapshot");StoreService.OnRead=null;
var facts=new Facts();
foreach(var a in Enum.GetValues<RequirementStatus>())foreach(var b in Enum.GetValues<RequirementStatus>()){
 facts.Values["a"]=a;facts.Values["b"]=b;
 var all=Requirements.Evaluate(Group("all",Leaf(target:"a"),Leaf(target:"b")),facts);
 var any=Requirements.Evaluate(Group("any",Leaf(target:"a"),Leaf(target:"b")),facts);
 var expectedAll=a==RequirementStatus.Unsatisfied||b==RequirementStatus.Unsatisfied?RequirementStatus.Unsatisfied:a==RequirementStatus.Unavailable||b==RequirementStatus.Unavailable?RequirementStatus.Unavailable:RequirementStatus.Satisfied;
 var expectedAny=a==RequirementStatus.Satisfied||b==RequirementStatus.Satisfied?RequirementStatus.Satisfied:a==RequirementStatus.Unavailable||b==RequirementStatus.Unavailable?RequirementStatus.Unavailable:RequirementStatus.Unsatisfied;
 Check(all.Status==expectedAll,"three-valued all "+a+"/"+b);Check(any.Status==expectedAny,"three-valued any "+a+"/"+b);
}
foreach(var value in Enum.GetValues<RequirementStatus>()){
 facts.Values["a"]=value;var result=Requirements.Evaluate(Group("not",Leaf(target:"a")),facts);
 Check(result.Status==(value==RequirementStatus.Unavailable?value:value==RequirementStatus.Satisfied?RequirementStatus.Unsatisfied:RequirementStatus.Satisfied),"not preserves unknown "+value);
}
facts.Values["a"]=RequirementStatus.Satisfied;facts.Reads=0;Check(Requirements.Evaluate(Group("all",Leaf(target:"a"),Leaf(target:"a")),facts).Satisfied&&facts.Reads==1,"same fact resolved once per evaluation");
facts.Throw=true;Check(Requirements.Evaluate(Group("not",Leaf()),facts).Status==RequirementStatus.Unavailable,"fact exception never becomes true under not");facts.Throw=false;
Check(Requirements.Evaluate(Group("all"),facts).Satisfied,"empty all is explicit no restrictions");
var bilingual=new RequirementDefinition{Kind="upgrade-unlocked",TargetId="a",Message=new("需要升级","Upgrade required")};Check(Requirements.Evaluate(bilingual,facts,"en").Message=="Upgrade required"&&Requirements.Evaluate(bilingual,facts,"zh-CN").Message=="需要升级","bilingual explanatory text follows locale");
var children=new List<RequirementDefinition>{Leaf(target:"a")};var snapshot=Requirements.Copy(new(){Children=children});children.Clear();Check(snapshot.Children.Count==1,"tree snapshot independent of author list");
var loop=new List<RequirementDefinition>();var cyclic=new RequirementDefinition{Children=loop};loop.Add(cyclic);Reject(()=>Requirements.Copy(cyclic),"cyclic declaration rejected");
foreach(var def in new[]{new RequirementDefinition{Kind="bad"},Group("any"),Group("not"),Group("not",Leaf(),Leaf()),new RequirementDefinition{Kind="all",Amount=1},Leaf(target:null),Leaf(amount:1),new RequirementDefinition{Kind="day-at-least",Amount=-1},new RequirementDefinition{Kind="credits-at-least",Amount=(long)int.MaxValue+1},new RequirementDefinition{Kind="service-active",TargetId="service",Children=new[]{Leaf()}},new RequirementDefinition{Message=new(" ","x")}})Reject(()=>Requirements.Copy(def),"invalid tree "+def.Kind+"/"+cases.Count);
RequirementDefinition deep=Leaf();for(int i=0;i<8;i++)deep=Group("not",deep);Reject(()=>Requirements.Copy(deep),"depth bounded");Reject(()=>Requirements.Copy(Group("all",Enumerable.Range(0,33).Select(_=>Leaf()).ToArray())),"child fanout bounded");var many=Group("all",Enumerable.Range(0,5).Select(_=>Group("all",Enumerable.Range(0,26).Select(_=>Leaf()).ToArray())).ToArray());Reject(()=>Requirements.Copy(many),"total nodes bounded");
var json="{\"kind\":\"all\",\"children\":[{\"kind\":\"credits-at-least\",\"amount\":2,\"message\":{\"chinese\":\"钱\",\"english\":\"Credits\"}}]}";var parsed=Parse(json);Check(parsed.Children[0].Amount==2&&parsed.Children[0].Message!.English=="Credits","strict JSON bilingual tree roundtrip");
foreach(var invalid in new[]{"null","[]","{\"extra\":1}","{\"kind\":\"all\",\"kind\":\"all\"}","{\"children\":null}","{\"children\":[null]}","{\"message\":{\"english\":\"x\",\"extra\":\"y\"}}","{\"message\":{\"english\":null}}","{\"message\":{\"english\":\"x\",\"english\":\"y\"}}"})Reject(()=>Parse(invalid),"bad JSON "+invalid);
Reject(()=>Parse(new string(' ',65537)),"JSON byte size bounded");using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{}"))){Requirements.Parse(stream);Check(stream.CanRead,"parser retains caller stream");}
var nativeTree=Group("all",Leaf("day-at-least",null,1),Leaf("day-at-most",null,7),Leaf("credits-at-least",null,40),Leaf("service-unlocked","service"),Group("not",Leaf("upgrade-unlocked","upgrade")));
var native=RequirementApi.Evaluate(nativeTree);Check(native.Satisfied&&native.Run==GameSession.Identity,"native adapter composes current day/cash/service/upgrade");
Check(RequirementApi.Evaluate(Leaf("credits-at-least",null,41)).Status==RequirementStatus.Unsatisfied,"cash threshold uses actual amount without payment");Check(PlayerStore.instance.Cash==40&&upgrade.cost==20,"evaluations preserve native cash and definition fields");
Check(RequirementApi.Evaluate(Group("not",Leaf(target:"missing"))).Status==RequirementStatus.Unavailable,"missing native ID cannot unlock inverted condition");
RunData.IsAvailable=false;Check(RequirementApi.Evaluate(Group("all")).Status==RequirementStatus.Unavailable,"even empty current-run tree unavailable when unloaded");RunData.IsAvailable=true;
StoreStation.Reads=0;RequirementApi.Evaluate(Group("all",Leaf("day-at-least",null,1),Leaf("day-at-most",null,9)));Check(StoreStation.Reads==1,"day captured once across distinct numeric conditions");
var report=new{passed=cases.Count,failed=0,cases,nativeRuntimeVerified=false,scope="Real progression wrapper and strict declarative three-valued requirements; explicit native lookup, session, locale, cost and clock stubs. No native purchase, unlock or save behavior."};File.WriteAllText("work/ezapi-progression-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed,report.scope}));
sealed class Facts:IRequirementFacts {
 internal readonly Dictionary<string,RequirementStatus> Values=new();internal int Reads;internal bool Throw;
 public RequirementFact Read(string kind,string? id,long? amount){Reads++;if(Throw)throw new InvalidOperationException();return new(Values.GetValueOrDefault(id??"",RequirementStatus.Unavailable));}
}

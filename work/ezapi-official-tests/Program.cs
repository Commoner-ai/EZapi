using EZapi;
using Il2Cpp;
using System.Text;
using System.Text.Json;

var passed=new List<string>();
void Check(bool condition,string name) { if(!condition)throw new Exception(name);passed.Add(name); }
ItemDefinition Item(string id,IReadOnlyList<string>? types=null,IReadOnlyList<ItemTypeNameDefinition>? names=null)=>
    new(){Id=id,Name=id,Types=types??Array.Empty<string>(),TypeNames=names??Array.Empty<ItemTypeNameDefinition>()};
Check(!ItemRegistry.Register(Item("test.mod.before")).Accepted,"registration rejected before startup");
Check(ItemRegistry.BehaviorRegistrationError("test.mod.before")!.StartsWith("EZAPI_NOT_READY"),"activation registration blocked before startup");
ItemRegistry.Start(); ItemRegistry.Start();
var batch=ItemRegistry.RegisterBatch(new[]{Item("test.mod.one"),Item("test.mod.one")});
Check(batch.All(r=>!r.Accepted)&&ItemRegistry.GetStatus("test.mod.one")=="Unknown","duplicate batch leaves registry empty");
Check(ItemRegistry.Register(Item("test.mod.one")).Accepted,"register valid item");
Check(ItemRegistry.BehaviorRegistrationError("test.mod.one") is null,"queued item allows activation registration");
Check(ItemRegistry.BehaviorRegistrationError("test.mod.unknown")!.StartsWith("EZAPI_ACTIVATION_ITEM"),"unknown activation item rejected by real registry");
ItemRegistry.Register(new ItemDefinition{Id="test.mod.medical",Name="medical",Activation=new(){Kind="healWounds"}});
Check(ItemRegistry.BehaviorRegistrationError("test.mod.medical")!.StartsWith("EZAPI_ACTIVATION_CONFLICT"),"JSON behavior blocks C# callback registration");
batch=ItemRegistry.RegisterBatch(new[]{Item("test.mod.two"),Item("test.mod.one")});
Check(batch.All(r=>!r.Accepted)&&ItemRegistry.GetStatus("test.mod.two")=="Unknown","existing ID rejects whole batch");
try { ItemRegistry.RegisterBatch(new[]{Item("test.mod.two"),new ItemDefinition{Id="bad",Name="bad"}});throw new Exception("validation did not reject"); }
catch(ArgumentException) { Check(ItemRegistry.GetStatus("test.mod.two")=="Unknown","invalid field does not partially register"); }
var types=new List<string>{"TOOL"};
ItemRegistry.Register(Item("test.mod.snapshot",types)); types.Add("FOOD");
Check(ItemRegistry.AllDefinitions().Single(d=>d.Id=="test.mod.snapshot").Types.Count==1,"caller list mutation cannot alter declaration snapshot");
var typeName=new ItemTypeNameDefinition{Id="test.mod.kind",Name="kind"};
ItemRegistry.Register(Item("test.mod.kinditem",new[]{"test.mod.kind"},new[]{typeName}));
batch=ItemRegistry.RegisterBatch(new[]{Item("test.mod.two"),Item("test.mod.conflict",new[]{"test.mod.kind"},new[]{new ItemTypeNameDefinition{Id="test.mod.kind",Name="different"}})});
Check(batch.All(r=>!r.Accepted)&&ItemRegistry.GetStatus("test.mod.two")=="Unknown","conflicting custom type rejects whole batch");
Check(!ItemRegistry.TryTypeName("test.mod.kind",out _),"queued custom type is inert before directory apply");
var worker=Task.Run(()=>ItemRegistry.Register(Item("test.mod.worker"))).Result;
Check(!worker.Accepted&&ItemRegistry.GetStatus("test.mod.worker")=="Unknown","background registration rejected");
Check(Task.Run(()=>ItemRegistry.BehaviorRegistrationError("test.mod.one")).Result!.StartsWith("EZAPI_NOT_READY"),"activation registration enforces main thread");
using(var input=new MemoryStream(Encoding.UTF8.GetBytes("[{\"id\":\"test.mod.json\",\"name\":\"ok\"},{\"id\":\"test.mod.one\",\"name\":\"dup\"}]"))) {
    var result=JsonItems.Register(input);
    Check(result.All(r=>!r.Accepted)&&ItemRegistry.GetStatus("test.mod.json")=="Unknown","JsonItems.Register uses atomic submission");
}
ItemRegistry.Register(new ItemDefinition{Id="test.mod.food",Name="food",Food=new(){Calories=400}});
Check(ItemRegistry.BehaviorRegistrationError("test.mod.food")!.StartsWith("EZAPI_ACTIVATION_CONFLICT"),"native food rejects competing CSharp activation");
ItemRegistry.Register(new ItemDefinition{Id="test.mod.box",Name="box",Container=new(){Width=4,Height=3}});
Check(ItemRegistry.BehaviorRegistrationError("test.mod.box")!.StartsWith("EZAPI_ACTIVATION_CONFLICT"),"native container rejects competing CSharp activation");
var filters=new List<string>{"processed_cheese"};
ItemRegistry.Register(new ItemDefinition{Id="test.mod.filterbox",Name="filterbox",Container=new(){Width=2,Height=2,AllowedIds=filters}});
filters.Add("other");
Check(ItemRegistry.AllDefinitions().Single(d=>d.Id=="test.mod.filterbox").Container!.AllowedIds.Count==1,"caller mutation cannot change queued container filter");
ItemRegistry.Register(new ItemDefinition{Id="test.mod.liquid",Name="liquid",LiquidContainer=new(){Capacity=500}});
Check(ItemRegistry.BehaviorRegistrationError("test.mod.liquid")!.StartsWith("EZAPI_ACTIVATION_CONFLICT"),"native liquid rejects competing CSharp activation");
var scratch=Path.Combine(Path.GetTempPath(),"ezapi-official-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
File.WriteAllText(Path.Combine(scratch,"pack.json"),"{\"id\":\"pack.test\",\"name\":\"Pack\",\"ezapiVersion\":\"0.1\"}");
File.WriteAllText(Path.Combine(scratch,"items.json"),"[{\"id\":\"pack.test.first\",\"modId\":\"pack.test\",\"name\":\"ok\"},{\"id\":\"pack.test.png\",\"modId\":\"pack.test\",\"name\":\"bad\",\"sprite\":\"missing\"}]");
try { ItemRegistry.RegisterPack(scratch);throw new Exception("missing asset accepted"); }
catch(InvalidDataException) { Check(ItemRegistry.GetStatus("pack.test.first")=="Unknown","asset preparation failure leaves whole pack unregistered"); }
File.WriteAllText(Path.Combine(scratch,"items.json"),"[{\"id\":\"pack.test.first\",\"modId\":\"pack.test\",\"name\":\"ok\"}]");
Check(ItemRegistry.RegisterPack(scratch).All(r=>r.Accepted),"failed pack can be fixed and registered again");
Check(ItemRegistry.RegisterPack(scratch).All(r=>!r.Accepted),"duplicate pack rejected");
File.WriteAllText(Path.Combine(scratch,"pack.json"),"{\"id\":\"pack.bad\",\"id\":\"pack.test\",\"name\":\"Pack\",\"ezapiVersion\":\"0.1\"}");
try { ItemRegistry.RegisterPack(scratch);throw new Exception("duplicate manifest field accepted"); }
catch(InvalidDataException) { Check(true,"manifest duplicate field rejected"); }
var directory=new ModItemDirectory(); directory.Keys.Add("test.mod.snapshot");
ItemRegistry.Apply(directory);
Check(ItemRegistry.GetStatus("test.mod.one")=="Applied"&&ItemRegistry.GetStatus("test.mod.snapshot").StartsWith("Rejected"),"native ID collision does not overwrite directory");
Check(ItemRegistry.TryTypeName("test.mod.kind",out var name)&&name=="kind","custom name available after apply");
Check(ItemRegistry.SourceName(ItemRegistry.AllDefinitions().Single(d=>d.Id=="pack.test.first"))=="Pack","source name uses pack manifest name");
var adds=directory.Adds;ItemRegistry.Apply(directory);
Check(directory.Adds==adds&&ItemRegistry.GetStatus("test.mod.one")=="Applied","repeated same directory event is idempotent");
Check(!ItemRegistry.Register(Item("test.mod.late")).Accepted,"registration after directory event rejected");
Check(ItemRegistry.BehaviorRegistrationError("test.mod.one")!.StartsWith("EZAPI_TOO_LATE"),"activation registration blocked after directory event");
ItemRegistry.Apply(new ModItemDirectory{Pointer=new(2)});
Check(ItemRegistry.GetStatus("test.mod.snapshot")=="Applied","new directory rebuild reuses definitions");
ItemRegistry.Stop();ItemRegistry.Stop();ItemRegistry.Start();
Check(ItemRegistry.Register(Item("test.mod.one")).Accepted,"stop resets registration lifecycle");
ItemRegistry.Stop();

void Throws<T>(Action action,string name) where T:Exception {try{action();throw new Exception("no exception: "+name);}catch(T){Check(true,name);}}
Throws<InvalidOperationException>(()=>ContentApi.Subscribe("test.mod","before",_=>{}),"content blocked before start");
ContentApi.Start();ItemRegistry.Start();
var views=new List<NativeApplicationView>();
var watcher=ContentApi.Subscribe("test.mod","watch",v=>views.Add(v));
Throws<ArgumentException>(()=>ContentApi.Subscribe("test.mod","watch",_=>{}),"duplicate owner and callback ID rejected");
Throws<ArgumentException>(()=>ContentApi.Subscribe("test.mod","\n",_=>{}),"control callback ID rejected");
Throws<ArgumentNullException>(()=>ContentApi.Subscribe("test.mod","null",null!),"null callback rejected");
Throws<InvalidOperationException>(()=>Task.Run(()=>ContentApi.Subscribe("test.mod","worker",_=>{})).GetAwaiter().GetResult(),"content worker thread rejected");
bool atomic=true;var queueOrder=new List<string>();bool reentered=false;
using var nested=ContentApi.Subscribe("a.owner","nested",v=>{
 if(v.Status!=NativeApplicationStatus.Staged)return;
 atomic&=ItemRegistry.GetStatus("test.mod.a")=="Queued"&&ItemRegistry.GetStatus("test.mod.b")=="Queued";
 queueOrder.Add(v.ContentId);
 if(!reentered){reentered=true;ItemRegistry.Register(Item("test.mod.c"));}
});
var committed=ItemRegistry.RegisterBatch(new[]{Item("test.mod.a"),Item("test.mod.b")});
Check(committed.All(r=>r.Accepted)&&atomic,"content staged only after full batch commit");
Check(queueOrder.SequenceEqual(new[]{"test.mod.a","test.mod.b","test.mod.c"}),"reentrant registration queued after current batch");
nested.Dispose();
Check(views.Count==3&&views.All(v=>v.OwnerId=="ezapi.core"&&v.Status==NativeApplicationStatus.Staged),"global observers receive unowned declaration fallback");
var before=views.Count;ItemRegistry.Register(Item("test.mod.a"));
Check(views.Count==before,"rejected declaration does not emit staged");
ItemRegistry.Register(new ItemDefinition{Id="pack.owner.d",ModId="pack.owner",Name="owned"});
Check(views.Last().OwnerId=="pack.owner","content owner retained independently of subscription owner");
var brokenCalls=0;using var broken=ContentApi.Subscribe("a.owner","broken",_=>{brokenCalls++;throw new Exception("subscriber failure");});
var dir2=new ModItemDirectory{Pointer=new(30)};dir2.Keys.Add("test.mod.a");dir2.FailIds.Add("test.mod.b");ItemRegistry.Apply(dir2);
Check(views.Any(v=>v.ContentId=="test.mod.a"&&v.Status==NativeApplicationStatus.Conflict&&v.Reason is not null),"directory collision publishes conflict");
Check(views.Any(v=>v.ContentId=="test.mod.b"&&v.Status==NativeApplicationStatus.Failed&&v.Reason is not null),"directory false Add publishes failure");
Check(views.Any(v=>v.ContentId=="test.mod.c"&&v.Status==NativeApplicationStatus.Applied)&&ItemRegistry.GetStatus("test.mod.c")=="Applied","directory successful Add publishes applied");
Check(brokenCalls==1,"failing content callback removed without undoing registration");
before=views.Count;ItemRegistry.Apply(dir2);Check(before==views.Count,"same directory does not republish results");
ItemRegistry.Apply(new ModItemDirectory{Pointer=new(31)});Check(views.Count>before&&ItemRegistry.GetStatus("test.mod.b")=="Applied","directory rebuild republishes actual application");
ContentApi.Stop();ContentApi.Start();
var called=0;using var victim=ContentApi.Subscribe("b.owner","victim",_=>called++);
using var disposer=ContentApi.Subscribe("a.owner","dispose",_=>victim.Dispose());
ContentApi.PublishBatch(new[]{new NativeApplicationView("any.owner","content","item",NativeApplicationStatus.Applied,null)});
Check(called==0,"disposed content callback skipped in current snapshot");
disposer.Dispose();
using var old=ContentApi.Subscribe("test.mod","same",_=>called+=100);
ContentApi.Stop();ContentApi.Start();
using var fresh=ContentApi.Subscribe("test.mod","same",_=>called++);old.Dispose();
ContentApi.PublishBatch(new[]{new NativeApplicationView("any.owner","content","item",NativeApplicationStatus.Applied,null)});
Check(called==1,"stale lease cannot remove replacement after restart");
fresh.Dispose();
using var stopper=ContentApi.Subscribe("a.owner","stop",_=>ContentApi.Stop());
using var afterStop=ContentApi.Subscribe("b.owner","after",_=>called++);
ContentApi.PublishBatch(new[]{new NativeApplicationView("any.owner","1","item",NativeApplicationStatus.Applied,null),new NativeApplicationView("any.owner","2","item",NativeApplicationStatus.Applied,null)});
Check(called==1,"stop aborts remaining subscribers and queued notifications");
ContentApi.Start();var storm=0;
using var stormLease=ContentApi.Subscribe("test.mod","storm",v=>{storm++;ContentApi.PublishBatch(new[]{v});});
ContentApi.PublishBatch(new[]{new NativeApplicationView("test.mod","storm","item",NativeApplicationStatus.Staged,null)});
Check(storm==8192,"recursive notification storm bounded without stack recursion");stormLease.Dispose();
var leases=Enumerable.Range(0,256).Select(i=>ContentApi.Subscribe("test.mod","limit"+i,_=>{})).ToArray();
Throws<InvalidOperationException>(()=>ContentApi.Subscribe("test.mod","overflow",_=>{}),"content subscription count bounded");
foreach(var lease in leases)lease.Dispose();
ContentApi.Stop();ItemRegistry.Stop();

using(var stockJson=new MemoryStream(System.Text.Encoding.UTF8.GetBytes("[{\"id\":\"test.mod.stock\",\"name\":\"Stock\",\"nightShop\":\"unique\"}]")))
 Check(JsonItems.Parse(stockJson).Single().NightShop=="unique","JSON night shop policy parsed through shared strict validator");
Throws<ArgumentException>(()=>DefinitionRules.Validate(new(){Id="test.mod.badstock",Name="Stock",NightShop="unknown"}),"unknown JSON night shop policy rejected");
var report=new{passed=passed.Count,cases=passed,nativeRuntimeTest=false,scope="real managed registry/pack loader; native calls replaced with minimal stubs"};
Directory.CreateDirectory("work");
File.WriteAllText("work/ezapi-official-registration-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));

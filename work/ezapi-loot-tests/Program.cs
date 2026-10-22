using EZapi;
using Il2Cpp;
using System.Text;
using System.Text.Json;

var cases=new List<string>();
void Check(bool ok,string name) {if(!ok)throw new Exception(name);cases.Add(name);}
bool Throws(Action action,string fragment="") {
    try {action();return false;}catch(Exception e){return e.Message.Contains(fragment,StringComparison.Ordinal);}
}
LootDefinition Parse(string json) {using var stream=new MemoryStream(Encoding.UTF8.GetBytes(json));return JsonLoot.Parse(stream);}
void Reset() {
    LootTables.Stop();ItemRegistry.Stop();LootRegistry.Reset();MelonLoader.MelonLogger.Errors.Clear();
    ItemRegistry.Start();LootTables.Start();
    LootRegistry.tables["junkTable"]=new(){new("vanilla_a",4),new("vanilla_b",6)};
    LootRegistry.groups["vanillaGroup"]=new(){new("junkTable",1)};
}
void Items(string owner) {
    ItemRegistry.Register(new ItemDefinition{Id=owner+".one",Name="one"});
    ItemRegistry.Register(new ItemDefinition{Id=owner+".two",Name="two"});
}
LootDefinition Definition(string owner)=>new() {
    Tables=new[]{new LootListDefinition{Id=owner+".table",Entries=new[]{new LootWeightDefinition{Id=owner+".one",Weight=3},new LootWeightDefinition{Id=owner+".two",Weight=1}}}},
    Groups=new[]{new LootListDefinition{Id=owner+".group",Entries=new[]{new LootWeightDefinition{Id=owner+".table",Weight=1}}}}
};
string? State(string owner) {
    using var json=JsonDocument.Parse(JsonSerializer.Serialize(LootTables.Report()));
    return json.RootElement.GetProperty("batches").EnumerateArray().FirstOrDefault(b=>b.GetProperty("modId").GetString()==owner)
        .GetProperty("state").GetString();
}
void Apply() {ItemRegistry.Apply(new());ModHook.Fire();}

Check(Parse("{}").Tables.Count==0,"empty optional loot lists accepted");
Check(Parse("{\"tables\":[{\"id\":\"m.table\",\"entries\":[{\"id\":\"rusty_can\",\"weight\":0.5}]}]}").Tables[0].Entries[0].Weight==0.5f,"official format fractional weight preserved");
foreach(var (json,name) in new[]{
    ("[]","root array"),("{\"unknown\":[]}","unknown field"),("{\"tables\":[],\"tables\":[]}","duplicate field"),
    ("{\"tables\":null}","null list"),("{\"tables\":[null]}","null declaration"),
    ("{\"tables\":[{\"id\":\"m.t\",\"entries\":[{\"id\":\"a\",\"weight\":0}]}]}","zero register weight"),
    ("{\"patches\":[{\"table\":\"junkTable\",\"add\":[{\"id\":\"a\",\"weight\":-1}]}]}","negative add weight"),
    ("{\"patches\":[{\"table\":\"junkTable\",\"group\":\"vanillaGroup\",\"clear\":true}]}","ambiguous patch target"),
    ("{\"patches\":[{\"clear\":true}]}","missing patch target"),
    ("{\"patches\":[{\"table\":\"junkTable\"}]}","empty patch"),
    ("{\"tables\":[{\"id\":\"../escape\"}]}","path ID"),
    ("{\"tables\":[{\"id\":\"m.t\",\"entries\":[{\"id\":\"a\",\"weight\":1},{\"id\":\"a\",\"weight\":2}]}]}","duplicate entry"),
    ("{\"tables\":[{\"id\":\"m.t\"},{\"id\":\"m.t\"}]}","duplicate table"),
    ("{\"patches\":[{\"table\":\"junkTable\",\"remove\":[\"a\",\"a\"]}]}","duplicate remove"),
    ("{\"tables\":[{\"id\":\"m.t\",\"entries\":[{\"id\":\"a\",\"weight\":1e100}]}]}","nonfinite or overflow weight"),
    ("{\"patches\":[{\"table\":\"junkTable\",\"setWeight\":[{\"id\":\"a\"}]}]}","missing explicit setWeight value"),
    ("{/*comment*/\"tables\":[]}","comments"),("{\"tables\":[],}","trailing comma")
})Check(Throws(()=>Parse(json)),"parser rejects "+name);
Check(Parse("{\"patches\":[{\"table\":\"junkTable\",\"setWeight\":[{\"id\":\"a\",\"weight\":0}]}]}").Patches[0].SetWeight[0].Weight==0,"setWeight zero accepted");
Check(Throws(()=>Parse(new string(' ',1024*1024+1)),"EZAPI_LOOT_LIMIT"),"input byte limit enforced before parsing");
Check(Throws(()=>JsonLoot.Validate(new(){Tables=new[]{new LootListDefinition{Id="m.t",Entries=new[]{new LootWeightDefinition{Id="a",Weight=float.NaN}}}}}),"EZAPI_LOOT_WEIGHT"),"C# NaN weight rejected");

Reset();Items("test.mod");
var accepted=LootTables.Register("test.mod",Definition("test.mod"));
Check(accepted.Accepted&&accepted.OperationCount==2&&LootRegistry.Calls.Count==0,"register only queues managed declarations");
Check(Throws(()=>LootTables.Register("test.mod",Definition("test.mod")),"EZAPI_LOOT_DUPLICATE"),"duplicate owner rejected");
ModHook.Fire();Check(LootRegistry.Calls.Count==0,"native tables event waits for item directory");
ItemRegistry.Apply(new());
Check(State("test.mod")=="Submitted"&&LootRegistry.Calls.Count==2,"opposite event ordering submits once after both ready");
var snapshot=LootTables.GetSnapshot("test.mod.table");
Check(snapshot.Found&&snapshot.TotalWeight==4&&snapshot.Entries[0].Probability==0.75&&snapshot.Entries[1].Probability==0.25,"snapshot computes current relative probabilities");
Check(snapshot.Entries.All(e=>e.SourceModId=="test.mod"),"native source attribution preserved");
LootRegistry.tables["test.mod.table"][0].weight=100;
Check(snapshot.Entries[0].Weight==3,"snapshot detached from native mutable entries");
Check(LootTables.TryRoll("test.mod.table",false,out var id)&&id=="test.mod.one"&&LootRegistry.RollCount==1,"roll returns native ID without item factory call");
Check(LootTables.TryRoll("test.mod.group",true,out _),"group roll delegates through native group path");
Check(!LootTables.GetSnapshot("missing").Found&&!LootTables.TryRoll("missing",false,out _),"missing lists produce absent snapshot and no roll");
var operations=LootRegistry.Calls.Count;ModHook.Fire();ItemRegistry.Apply(new());
Check(LootRegistry.Calls.Count==operations,"repeated directory and table events do not duplicate journal operations");
LootTables.SceneChanged();Check(Throws(()=>LootTables.GetSnapshot("test.mod.table"),"EZAPI_LOOT_NOT_READY"),"scene change gates stale list reads");
ModHook.Fire();Check(LootRegistry.Calls.Count==operations&&LootTables.GetSnapshot("test.mod.table").Found,"new scene readiness does not resubmit persistent journal");
Check(Throws(()=>LootTables.Register("test.late",Definition("test.late")),"EZAPI_TOO_LATE"),"late declaration rejected");
Check(Task.Run(()=>Throws(()=>LootTables.GetSnapshot("junkTable"),"EZAPI_RUN_THREAD")).Result,"off-thread native snapshot rejected");

Reset();Items("test.patch");
LootTables.Register("test.patch",new(){Tables=Definition("test.patch").Tables,
    Patches=new[]{new LootPatchDefinition{Table="junkTable",Clear=true,
        Add=new[]{new LootWeightDefinition{Id="test.patch.one",Weight=8},new LootWeightDefinition{Id="vanilla_a",Weight=2}},
        Remove=new[]{"vanilla_a"},SetWeight=new[]{new LootWeightDefinition{Id="test.patch.one",Weight=0}}},
        new LootPatchDefinition{Group="vanillaGroup",Clear=true,Add=new[]{new LootWeightDefinition{Id="test.patch.table",Weight=2}},
            SetWeight=new[]{new LootWeightDefinition{Id="test.patch.table",Weight=0}},Remove=new[]{"junkTable"}}}});
Apply();
Check(LootRegistry.Calls.Take(6).SequenceEqual(new[]{"registerTable:test.patch.table","clear","add:test.patch.one","add:vanilla_a","remove:vanilla_a","set:test.patch.one"}),"official table patch order clear/add/remove/setWeight");
Check(LootTables.GetSnapshot("junkTable").Entries.Count==1&&LootTables.GetSnapshot("junkTable").TotalWeight==0&&!LootTables.TryRoll("junkTable",false,out _),"setWeight zero preserves entry but no positive roll");
Check(LootTables.GetSnapshot("vanillaGroup",true).Entries.Single().ModifiedByModId=="test.patch","group patch methods and modifier attribution");

Reset();Items("test.snapshot");var entries=new List<LootWeightDefinition>{new(){Id="test.snapshot.one",Weight=2}};
LootTables.Register("test.snapshot",new(){Tables=new[]{new LootListDefinition{Id="test.snapshot.table",Entries=entries}}});entries.Clear();
Apply();Check(LootTables.GetSnapshot("test.snapshot.table").Entries.Single().Weight==2,"caller declaration mutation cannot change queued entries");
Reset();
Check(Throws(()=>LootTables.Register("test.owner",new(){Tables=new[]{new LootListDefinition{Id="foreign.table"}}}),"EZAPI_LOOT_OWNER"),"new custom list requires owner namespace");
Check(Task.Run(()=>Throws(()=>LootTables.Register("test.worker",Definition("test.worker")),"EZAPI_NOT_READY")).Result,"background registration rejected");
LootTables.Register("test.missing",Definition("test.missing"));Apply();
Check(State("test.missing")=="Rejected"&&LootRegistry.Calls.Count==0,"missing owner item rejects before any native mutation");
Reset();Items("test.conflict");LootRegistry.tables["test.conflict.table"]=new();
LootTables.Register("test.conflict",Definition("test.conflict"));Apply();
Check(State("test.conflict")=="Rejected"&&LootRegistry.Calls.Count==0,"native ID collision never overwrites existing table");
Reset();LootTables.Register("test.target",new(){Patches=new[]{new LootPatchDefinition{Table="typo",Clear=true}}});Apply();
Check(State("test.target")=="Rejected"&&LootRegistry.Calls.Count==0,"unknown patch target rejected before mutation");
Reset();LootTables.Register("test.reference",new(){Groups=new[]{new LootListDefinition{Id="test.reference.group",Entries=new[]{new LootWeightDefinition{Id="typo",Weight=1}}}}});Apply();
Check(State("test.reference")=="Rejected"&&LootRegistry.Calls.Count==0,"missing group table reference rejected before mutation");
Reset();Items("test.partial");LootTables.Register("test.partial",Definition("test.partial"));LootRegistry.ThrowAfterCall=2;Apply();
Check(State("test.partial")=="FailedPartial"&&LootRegistry.Calls.Count==2,"partial native failure explicitly recorded");
ModHook.Fire();Check(LootRegistry.Calls.Count==2,"partially written native journal never auto-retried");

Reset();var scratch=Path.Combine(Path.GetTempPath(),"ezapi-loot-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(scratch,"LootTables"));
File.WriteAllText(Path.Combine(scratch,"pack.json"),"{\"id\":\"pack.loot\",\"name\":\"Loot Pack\",\"ezapiVersion\":\"0.1\"}");
File.WriteAllText(Path.Combine(scratch,"items.json"),"[{\"id\":\"pack.loot.one\",\"modId\":\"pack.loot\",\"name\":\"one\"}]");
File.WriteAllText(Path.Combine(scratch,"LootTables/first.json"),"{\"tables\":[{\"id\":\"pack.loot.table\",\"entries\":[{\"id\":\"pack.loot.one\",\"weight\":1}]}]}");
File.WriteAllText(Path.Combine(scratch,"LootTables/last.json"),"{\"unknown\":true}");
Check(Throws(()=>ItemRegistry.RegisterPack(scratch))&&ItemRegistry.GetStatus("pack.loot.one")=="Unknown"&&LootRegistry.Calls.Count==0,"bad later loot file leaves both items and native journal untouched");
File.WriteAllText(Path.Combine(scratch,"LootTables/last.json"),"{}");
File.WriteAllText(Path.Combine(scratch,"items.json"),"[{\"id\":\"pack.loot.one\",\"modId\":\"pack.loot\",\"name\":\"one\",\"sprite\":\"missing\"}]");
Check(Throws(()=>ItemRegistry.RegisterPack(scratch))&&ItemRegistry.GetStatus("pack.loot.one")=="Unknown","PNG failure after loot preparation does not commit items");
File.WriteAllText(Path.Combine(scratch,"items.json"),"[{\"id\":\"pack.loot.one\",\"modId\":\"pack.loot\",\"name\":\"one\"}]");
Check(ItemRegistry.RegisterPack(scratch).All(r=>r.Accepted),"failed pack can retry without reserved loot IDs");
Apply();Check(State("pack.loot")=="Submitted"&&LootTables.GetSnapshot("pack.loot.table").Entries.Count==1,"whole pack commits item and loot declarations together");
Reset();LootTables.Stop();
Check(ModHook.Handlers.Count==0,"same native loot callback removed on shutdown");
ModHook.ThrowAfterAdd=true;Check(Throws(()=>LootTables.Start())&&ModHook.Handlers.Count==0,"partial native event install rolls back same delegate");
ModHook.ThrowAfterAdd=false;LootTables.Start();ModHook.ThrowOnRemove=true;LootTables.Stop();
Check(ModHook.Handlers.Count==1,"failed native removal retains strong delegate");
ModHook.Fire();Check(LootRegistry.Calls.Count==0,"retained stopped callback is inert");
ModHook.ThrowOnRemove=false;LootTables.Stop();LootTables.Start();
Check(ModHook.Handlers.Count==1,"cleanup retry allows restart without duplicate native subscription");
LootTables.Stop();ItemRegistry.Stop();

var report=new{passed=cases.Count,cases,nativeRuntimeTest=false,
    scope="real JSON parser, item/pack registration, loot queue and native adapter sources; native LootRegistry/IL2CPP allocation/RNG are stubs; no native replay or acquisition proof"};
Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-loot-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));

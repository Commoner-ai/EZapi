using EZapi;
using Il2Cpp;
using System.Text.Json;
var cases=new List<string>();
void Check(bool b){if(!b)throw new Exception("assertion");}
void Reset(){DebugBatch.Clear();GameSession.Identity=new(1,"test",1);InventoryApi.IsItemDragActive=false;ItemRegistry.Count=10;EmporiumEntry.Instance=new();StoreUIManager.Instance=new();Calls.Order.Clear();Calls.ThrowAt=null;DebugLifecycle.Recording=false;EZapiBootstrap.Writes=0;}
JsonElement Report()=>JsonSerializer.SerializeToElement(DebugBatch.Report());
void Case(string n,Action a){Reset();a();cases.Add(n);}
void Start(){DebugBatch.Start(GameSession.Identity);Check(DebugBatch.Running);}
Case("requires current run",()=>{DebugBatch.Start(null);Check(!DebugBatch.Running&&Calls.Order.Count==0);});
Case("rejects stale epoch",()=>{DebugBatch.Start(new(1,"test",0));Check(!DebugBatch.Running);});
Case("requires store inventory",()=>{EmporiumEntry.Instance!.invElement=null;DebugBatch.Start(GameSession.Identity);Check(!DebugBatch.Running);});
Case("requires visible store canvas",()=>{StoreUIManager.Instance!.inventoryCanvas!.activeInHierarchy=false;DebugBatch.Start(GameSession.Identity);Check(!DebugBatch.Running);});
Case("caps factory definitions",()=>{ItemRegistry.Count=65;DebugBatch.Start(GameSession.Identity);Check(!DebugBatch.Running);});
Case("does not start while dragging",()=>{InventoryApi.IsItemDragActive=true;DebugBatch.Start(GameSession.Identity);Check(!DebugBatch.Running);});
Case("start queues without executing native groups",()=>{Start();Check(Calls.Order.Count==0&&EZapiBootstrap.Writes==1);});
Case("one group per update ordered across frames",()=>{Start();for(int i=0;i<5;i++){DebugBatch.Update();Check(Calls.Order.Count==i+1);}Check(Calls.Order.SequenceEqual(new[]{"lifecycle","items","inventory","resources","production"})&&!DebugBatch.Running);});
Case("completed reports collected never automatic pass",()=>{Start();for(int i=0;i<5;i++)DebugBatch.Update();var r=Report();Check(r.GetProperty("state").GetString()=="collected"&&r.GetProperty("entries").EnumerateArray().All(x=>x.GetProperty("Status").GetString()=="collected"));});
Case("duplicate start does not reset progress",()=>{Start();DebugBatch.Update();DebugBatch.Start(GameSession.Identity);DebugBatch.Update();Check(Calls.Order.SequenceEqual(new[]{"lifecycle","items"}));});
Case("scene change cancels pending groups",()=>{Start();DebugBatch.Update();DebugBatch.SceneChanged();DebugBatch.Update();Check(!DebugBatch.Running&&Calls.Order.Count==1&&Report().GetProperty("state").GetString()=="cancelled");});
Case("epoch change cancels before next group",()=>{Start();GameSession.Identity=new(1,"test",2);DebugBatch.Update();Check(!DebugBatch.Running&&Calls.Order.Count==0);});
Case("new drag cancels pending batch",()=>{Start();InventoryApi.IsItemDragActive=true;DebugBatch.Update();Check(!DebugBatch.Running&&Calls.Order.Count==0);});
Case("group exception recorded and later groups still execute",()=>{Calls.ThrowAt="items";Start();for(int i=0;i<5;i++)DebugBatch.Update();Check(Calls.Order.Last()=="production"&&Report().GetProperty("entries")[1].GetProperty("Status").GetString()=="error");});
Case("existing lifecycle recorder remains enabled",()=>{DebugLifecycle.Recording=true;Start();DebugBatch.Update();Check(DebugLifecycle.Recording&&Calls.Order.Count==0);});
Case("completed update cannot repeat factories",()=>{Start();for(int i=0;i<20;i++)DebugBatch.Update();Check(Calls.Order.Count==5);});
Case("explicit rerun replaces old batch reports",()=>{Start();DebugBatch.SceneChanged();Start();Check(Report().GetProperty("entries").GetArrayLength()==0);});
Case("clear releases run identity and state",()=>{Start();DebugBatch.Clear();Check(!DebugBatch.Running&&Report().GetProperty("run").ValueKind==JsonValueKind.Null);});
Case("gameplay and persistence remain pending",()=>{Check(Report().GetProperty("pendingGameplay").GetArrayLength()==5);});
var report=new{passed=cases.Count,cases,nativeRuntimeTest=false,scope="real DebugBatch scheduler with instrumented group/native substitutes; no game launch/UI/IL2CPP proof"};
File.WriteAllText("work/ezapi-batch-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.passed}));

using EZapi;
using Il2Cpp;
using System.Text.Json;
var cases=new List<string>();int failed=0;
void Check(bool ok,string name){cases.Add(name);if(!ok){failed++;Console.Error.WriteLine("FAIL "+name);}}
void Reject(Action act,string name){bool threw=false;try{act();}catch(ArgumentException){threw=true;}catch(InvalidOperationException){threw=true;}Check(threw,name);}
void Reset(){MachineAutomationApi.Stop();MachineRecipeApi.Stop();MachineTransactionApi.Stop();MachineAutomationApi.Start();MachineRecipeApi.Start();MachineTransactionApi.Start();}
(GameItem Machine,MachineInventory Slots,MachineBatchProcessor Processor) Fixture(bool eligible=true,bool fail=false,bool failRestore=false){
 var input=new GameItem{identifier="test.material"};var inv=new GameGridInventory();inv.Add(input);var output=new GameGridInventory();
 var slots=new MachineInventory(null,null,inv,output,new Dictionary<string,GameSlotInventory>(),null);
 var machine=new GameItem{identifier="test.machine",Layout=slots,NativeCost=0};
 var r=new MachineRecipe("test.mod.first",new[]{new MachineIngredient(eligible?"test.material":"test.absent")},new("test.product")){Transaction=p=>new Tx(p,fail,failRestore)};
 return(machine,slots,MachineRecipeApi.CreateBatchProcessor("test.mod","test.machine",new[]{r,r with {RecipeId="test.mod.second",Output=new("test.product",2)}},new(0)));
}
Reset();MachineAutomationApi.Stop();Reject(()=>MachineAutomationApi.Subscribe("test.mod","test.machine",MachineAutomationPhase.BeforeBatch,_=>{}),"stopped subscribe rejected");MachineAutomationApi.Start();
foreach(var owner in new[]{"A","bad\n","","../x"})Reject(()=>MachineAutomationApi.Subscribe(owner,"test.machine",MachineAutomationPhase.BeforeBatch,_=>{}),"bad owner "+owner.Trim());
Reject(()=>MachineAutomationApi.Subscribe("test.mod","",MachineAutomationPhase.BeforeBatch,_=>{}),"empty machine rejected");Reject(()=>MachineAutomationApi.Subscribe("test.mod","test.machine",(MachineAutomationPhase)99,_=>{}),"invalid phase rejected");Reject(()=>MachineAutomationApi.Subscribe("test.mod","test.machine",MachineAutomationPhase.BeforeBatch,null!),"null callback rejected");
using(var one=MachineAutomationApi.Subscribe("test.mod","test.machine",MachineAutomationPhase.BeforeBatch,_=>{}))Reject(()=>MachineAutomationApi.Subscribe("test.mod","test.machine",MachineAutomationPhase.BeforeBatch,_=>{}),"duplicate owner machine phase rejected");
Check(MachineAutomationApi.Subscribe("test.mod","test.machine",MachineAutomationPhase.BeforeBatch,_=>{}) is not null,"disposed key can resubscribe");Reset();
foreach(var mode in new[]{"normal","select","same-select","conflict","block","missing","nomatch","dispose","subscribe","nested","before-error","after-error","mutation","tag-mutation","machine-tag-mutation","stale-scope","full-output","partial","fault"}) {
 Reset();var f=Fixture(mode!="nomatch",mode is "partial" or "fault",mode=="fault");var leases=new List<IDisposable>();var order=new List<string>();MachineAutomationContext? captured=null;int before=0,after=0,late=0;bool afterControl=false,afterRead=false;MachineBatchResult? nested=null;IDisposable? other=null;
 void Sub(string owner,MachineAutomationPhase phase,Action<MachineAutomationContext> cb,int priority=0)=>leases.Add(MachineAutomationApi.Subscribe(owner,"test.machine",phase,cb,priority));
 Sub("test.first",MachineAutomationPhase.BeforeBatch,c=>{
  before++;captured=c;order.Add("first");Check(c.Machine==f.Machine&&c.Inventory.ItemInput.Pointer==f.Slots.ItemInput.Pointer&&c.Inventory.Output.Pointer==f.Slots.Output.Pointer&&c.Inventory.Battery?.Pointer==f.Slots.Battery?.Pointer&&c.IsCurrent&&c.Result is null&&c.BatchResult is null,mode+": live before fields");
  if(mode is "select" or "same-select")c.SelectRecipe("test.mod.second");if(mode=="conflict")c.SelectRecipe("test.mod.first");if(mode=="block")c.Block();if(mode=="missing")c.SelectRecipe("test.mod.missing");
  if(mode=="dispose")other!.Dispose();if(mode=="subscribe")Sub("test.late",MachineAutomationPhase.BeforeBatch,_=>late++);
  if(mode=="nested")nested=f.Processor.Process(f.Machine);if(mode=="before-error")throw new Exception("expected before");if(mode=="mutation")f.Slots.ItemInput.childItems[0].unitValue++;if(mode=="tag-mutation")f.Slots.ItemInput.childItems[0].state.GetTag("test.illegal").SetString("changed");if(mode=="machine-tag-mutation")f.Machine.state.GetTag("test.illegal").SetString("changed");
  if(mode=="stale-scope")GameSession.Identity=new(8,"changed",5);
 },-10);
 Sub("test.second",MachineAutomationPhase.BeforeBatch,c=>{order.Add("second");if(mode is "same-select" or "conflict")c.SelectRecipe("test.mod.second");});
 if(mode=="dispose"){other=MachineAutomationApi.Subscribe("test.disposed","test.machine",MachineAutomationPhase.BeforeBatch,_=>late++);leases.Add(other);}
 Sub("test.after",MachineAutomationPhase.AfterBatch,c=>{
  after++;afterRead=c.BatchResult is not null&&c.Result is not null&&c.RecipeId is not null;
  try{c.Block();}catch(InvalidOperationException){afterControl=true;}
  Reject(()=>c.SelectRecipe("test.mod.first"),mode+": after selection rejected");
  if(mode=="after-error")throw new Exception("expected after");
 });
 if(mode=="full-output")f.Slots.Output.InsertLocked=true;
 var result=f.Processor.Process(f.Machine);var success=mode is "normal" or "select" or "same-select" or "dispose" or "subscribe" or "nested" or "after-error";
 var executed=success||mode is "full-output" or "partial" or "fault";
 Check(result.Committed==success,mode+": commit status");Check(before==1,mode+": one Before");Check(after==(executed?1:0),mode+": After only executed transaction");Check(captured?.IsCurrent==false,mode+": context expired");
 Reject(()=>captured!.Block(),mode+": stale block rejected");Reject(()=>{var _=captured!.Machine;},mode+": stale native reference rejected");
 Check(f.Slots.Output.childItems.Count==(success?(mode is "select" or "same-select"?2:1):mode=="fault"?0:0),mode+": output readback");
 if(executed)Check(afterRead&&afterControl,mode+": after result and read-only controls");
 if(mode is "select" or "same-select")Check(result.RecipeId=="test.mod.second",mode+": forced second recipe");
 if(mode is "block" or "conflict")Check(result.Error=="EZAPI_MACHINE_AUTOMATION_BLOCKED",mode+": monotone conflict/block");
 if(mode=="nested")Check(nested?.Error=="EZAPI_RECIPE_REENTRANT_OR_STALE","nested processor blocked");
 if(mode is "dispose" or "subscribe")Check(late==0,mode+": snapshot/disposal respected");
 if(mode=="before-error") {var next=f.Processor.Process(f.Machine);Check(next.Committed&&before==1,"failing before disabled and explicit retry succeeds");}
 if(mode=="after-error")Check(result.Committed&&result.Transaction?.CompletionError is null,"after failure preserves exact committed receipt");
 if(mode is "mutation" or "tag-mutation" or "machine-tag-mutation")Check(result.Error!.StartsWith("EZAPI_MACHINE_AUTOMATION_STALE"),"resource mutation blocks before write");
 if(mode=="fault") {var next=f.Processor.Process(f.Machine);Check(next.Error!.StartsWith("EZAPI_MACHINE_QUARANTINED"),"RecoveryFailed prevents next batch");Check(!string.IsNullOrEmpty(f.Machine.GetTagReadonly("EZAPI_MACHINE_FAULT_V1")?.valueString),"RecoveryFailed saves quarantine marker");}
 foreach(var lease in leases)lease.Dispose();GameSession.Identity=new(1,"test",1);
}
Reset();var tie=Fixture();var seen=new List<string>();using(var b=MachineAutomationApi.Subscribe("z.mod","test.machine",MachineAutomationPhase.BeforeBatch,_=>seen.Add("z")))using(var a=MachineAutomationApi.Subscribe("a.mod","test.machine",MachineAutomationPhase.BeforeBatch,_=>seen.Add("a")))tie.Processor.Process(tie.Machine);Check(seen.SequenceEqual(new[]{"a","z"}),"same priority deterministic owner ordering");
Reset();for(var i=0;i<256;i++)MachineAutomationApi.Subscribe("test.o"+i,"test.machine",MachineAutomationPhase.BeforeBatch,_=>{});Reject(()=>MachineAutomationApi.Subscribe("test.excess","test.machine",MachineAutomationPhase.BeforeBatch,_=>{}),"max256 subscriptions enforced");
Reset();MachineAutomationContext? old=null;var stale=Fixture();using(var lease=MachineAutomationApi.Subscribe("test.mod","test.machine",MachineAutomationPhase.BeforeBatch,c=>{old=c;MachineAutomationApi.Stop();MachineAutomationApi.Start();})) {Check(!stale.Processor.Process(stale.Machine).Committed,"restart during callback blocks old context");Check(old?.IsCurrent==false,"generation invalidates old context");}
Check(Task.Run(()=>{try{MachineAutomationApi.Subscribe("test.bg","test.machine",MachineAutomationPhase.BeforeBatch,_=>{});return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"wrong thread blocked");
// 真实夜间循环纯逻辑；此处不冒充官方EndNight或存档证据。
MachineBatchResult Success()=>new("test.recipe",null,new(MachineTransactionStatus.Committed,Array.Empty<MachineProductReceipt>(),null,Array.Empty<string>()));
foreach(var n in new[]{-1,0,65,int.MaxValue}) {int calls=0;var r=MachineNightCycle.Run(()=>n,()=>{calls++;return Success();},()=>true);Check(r.Committed==0&&calls==0&&r.Status.StartsWith("nightly-count-invalid"),"invalid nightly quota "+n);}
foreach(var n in new[]{1,2,64}) {int calls=0,resolves=0;var r=MachineNightCycle.Run(()=>{resolves++;return n;},()=>{calls++;return Success();},()=>true);Check(r.Committed==n&&calls==n&&resolves==n,"nightly quota boundary "+n);}
{int calls=0,resolves=0;var r=MachineNightCycle.Run(()=>++resolves==1?3:1,()=>{calls++;return Success();},()=>true);Check(r.Committed==1&&calls==1,"dynamic quota shrink stops");}
{int calls=0,resolves=0;var r=MachineNightCycle.Run(()=>++resolves==1?2:64,()=>{calls++;return Success();},()=>true);Check(r.Committed==2&&calls==2,"dynamic increase cannot exceed initial quota");}
{int calls=0;var r=MachineNightCycle.Run(()=>4,()=>++calls==1?Success():new(null,"no-material",null),()=>true);Check(r.Committed==1&&calls==2&&r.Status=="no-material","first failed batch stops nightly loop");}
{int calls=0;var r=MachineNightCycle.Run(()=>4,()=>{calls++;return Success();},()=>calls<1);Check(r.Committed==1&&calls==1&&r.Status=="scope-stale","scope invalidation stops between batches");}
{int resolves=0;var r=MachineNightCycle.Run(()=>{resolves++;return 1;},Success,()=>false);Check(r.Committed==0&&resolves==0,"stale scope cannot invoke resolver");}
// 真实HandleNight入口：场景与目录仅为替身，验证算法不等于原生保存证据。
void NightReset(){Reset();MachineRuntimeApi.Stop();MachineRegistrationApi.Definitions.Clear();MachineRuntimeApi.Start();GameSession.Identity=new(1,"test",1);LifecycleApi.Ready=true;InventoryApi.IsItemDragActive=false;}
(GameItem Machine,LifecycleContext Context,MachineInventory Slots) NightFixture(bool enabled=true,Func<GameItem,int>? resolver=null,int inputs=4) {
 var f=Fixture();for(var i=1;i<inputs;i++)f.Slots.ItemInput.Add(new GameItem{identifier="test.material"});new GameGridInventory().Add(f.Machine);
 var recipe=new MachineRecipe("test.mod.nightly",new[]{new MachineIngredient("test.material")},new("test.product")){Transaction=p=>new Tx(p,false,false)};
 MachineRecipeApi.RegisterRecipes("test.mod","test.machine",new[]{recipe},new(0));
 MachineRegistrationApi.Definitions["test.machine"]=new("test.machine",new(),new[]{recipe}){EnableNightly=enabled,ResolveNightlyBatchCount=resolver};
 return(f.Machine,new(){Items=new[]{f.Machine},Store=new(){LiveItems=new[]{f.Machine}}},f.Slots);
}
NightReset();var nf=NightFixture();MachineRuntimeApi.HandleNight(nf.Context);
Check(nf.Slots.ItemInput.childItems.Count==3&&nf.Slots.Output.childItems.Count==1,"real scheduler defaults one batch");
Check(nf.Machine.GetTagReadonly("EZAPI_MACHINE_NIGHT_ATTEMPT_V1")?.valueString=="1/test/1","scheduler persists attempt token");
MachineRuntimeApi.HandleNight(nf.Context);Check(nf.Slots.Output.childItems.Count==1,"same night repeat has no extra product");
MachineRuntimeApi.Stop();MachineRuntimeApi.Start();GameSession.Identity=new(1,"test",2);MachineRuntimeApi.HandleNight(nf.Context);
Check(nf.Slots.Output.childItems.Count==1,"fresh epoch and scheduler respect saved marker");
nf.Context.Run=nf.Context.Run with{Day=2};MachineRuntimeApi.HandleNight(nf.Context);Check(nf.Slots.Output.childItems.Count==2,"next night may process again");
foreach(var mode in new[]{"disabled","invalid","unowned","detached","stacked","dragging","incomplete","stale","wrong-run","runtime-unready","quarantined"}) {
 NightReset();var f=NightFixture(mode!="disabled",mode=="invalid"?_=>0:null);
 if(mode=="unowned")f.Machine.Owned=false;if(mode=="detached")f.Machine.parentInventory!.Expel(f.Machine);if(mode=="stacked")f.Machine.unitCount=2;
 if(mode=="dragging")InventoryApi.IsItemDragActive=true;if(mode=="incomplete")f.Context.ItemsComplete=false;if(mode=="stale")f.Context.IsCurrent=false;
 if(mode=="wrong-run")f.Context.Run=f.Context.Run with{RunId="other"};if(mode=="runtime-unready")LifecycleApi.Ready=false;
 if(mode=="quarantined")f.Machine.state.GetTag("EZAPI_MACHINE_FAULT_V1").SetString("quarantine");
 MachineRuntimeApi.HandleNight(f.Context);Check(f.Slots.Output.childItems.Count==0&&f.Slots.ItemInput.childItems.Count==4,"scheduler blocks "+mode);
}
NightReset();int res=0;var dynamicNight=NightFixture(resolver:_=>++res==1?3:1);MachineRuntimeApi.HandleNight(dynamicNight.Context);
Check(dynamicNight.Slots.Output.childItems.Count==1&&res==2,"scheduler rechecks dynamic shrinking quota");
NightReset();var growingNight=NightFixture(resolver:_=>2);MachineRuntimeApi.HandleNight(growingNight.Context);Check(growingNight.Slots.Output.childItems.Count==2,"scheduler processes requested two batches");
NightReset();var throwing=NightFixture(resolver:_=>throw new Exception("expected quota failure"));MachineRuntimeApi.HandleNight(throwing.Context);MachineRuntimeApi.HandleNight(throwing.Context);
Check(throwing.Slots.Output.childItems.Count==0&&throwing.Machine.GetTagReadonly("EZAPI_MACHINE_NIGHT_ATTEMPT_V1") is not null,"resolver exception retains night attempt and does not produce");
NightReset();var changing=NightFixture(resolver:m=>{m.Layout!.ItemInput.childItems[0].unitValue++;return 1;});MachineRuntimeApi.HandleNight(changing.Context);
Check(changing.Slots.Output.childItems.Count==0,"night resolver resource mutation detected before processing");
// 链接真实MachineMaterialGraph和HandleNight；游戏对象/标签/图遍历仍为显式离线替身。
foreach(var mode in new[]{"machine-tag","input-tag","input-state","nested-tag","nested-value","nested-shape","nested-parent","nested-window","nested-topology","nested-lock","machine-shape","machine-window","same-shape-reparent","binding-replaced"}) {
 NightReset();GameItem? nested=null;GameInventory? inner=null;int calls=0;
 var f=NightFixture(resolver:m=>{
  calls++;var input=m.Layout!.ItemInput.childItems[0];
  switch(mode){
   case "machine-tag":m.state.GetTag("test.callback").SetString("changed");break;
   case "input-tag":input.state.GetTag("test.callback").SetString("changed");break;
   case "input-state":input.state=new();break;
   case "nested-tag":nested!.state.GetTag("test.callback").SetString("changed");break;
   case "nested-value":nested!.unitValue++;break;
   case "nested-shape":nested!.Shape="changed";break;
   case "nested-parent":nested!.parentInventory=new GameGridInventory();break;
   case "nested-window":nested!.contentWindow=new();break;
   case "nested-topology":inner!.children.Add(new GraphNodeStorage());break;
   case "nested-lock":inner!.RemoveLocked=true;break;
   case "machine-shape":m.Shape="changed";break;
   case "machine-window":m.contentWindow=new();break;
   case "same-shape-reparent":new GameGridInventory().Add(input);break;
   case "binding-replaced":m.Layout=m.Layout with{Output=new GameGridInventory()};break;
  }return 1;
 });
 if(mode.StartsWith("nested-")){inner=new GameInventory(f.Slots.ItemInput.childItems[0]);nested=new GameItem{identifier="test.nested"};inner.Add(nested);}
 var warnings=MelonLoader.MelonLogger.Warnings.Count;
 MachineRuntimeApi.HandleNight(f.Context);
 Check(f.Slots.Output.childItems.Count==0&&f.Slots.ItemInput.childItems.Count==(mode=="same-shape-reparent"?3:4),mode+": resolver mutation blocks all production");
 Check(MelonLoader.MelonLogger.Warnings.Skip(warnings).Any(x=>x.Contains("nightly count resolver changed resources")),mode+": explicit read-only callback diagnostic");
 MachineRuntimeApi.HandleNight(f.Context);MachineRuntimeApi.Stop();MachineRuntimeApi.Start();MachineRuntimeApi.HandleNight(f.Context);
 Check(calls==1&&f.Slots.Output.childItems.Count==0,mode+": persistent attempt prevents replay after scheduler restart");
}
NightReset();var nestedRead=NightFixture(resolver:m=>m.Layout!.ItemInput.childItems.Count>0?1:0);
var readInner=new GameInventory(nestedRead.Slots.ItemInput.childItems[0]);readInner.Add(new GameItem{identifier="test.nested"});
MachineRuntimeApi.HandleNight(nestedRead.Context);
Check(nestedRead.Slots.Output.childItems.Count==1,"read-only quota over nested graph remains supported with custom transaction");
NightReset();LifecycleContext? nestedContext=null;int count=0;var reentry=NightFixture(resolver:_=>{count++;MachineRuntimeApi.HandleNight(nestedContext!);return 1;});nestedContext=reentry.Context;MachineRuntimeApi.HandleNight(reentry.Context);
Check(count==1&&reentry.Slots.Output.childItems.Count==1,"scheduler reentry cannot double produce");
// 共享回调快照可以已经包含被前一订阅者销毁的包装对象；只允许读取新库存扫描。
NightReset();var fresh=NightFixture();fresh.Context.Items=new GameItem[]{null!};var captures=InventoryApi.Captures;
MachineRuntimeApi.HandleNight(fresh.Context);
Check(fresh.Slots.Output.childItems.Count==1&&InventoryApi.Captures==captures+2,"scheduler uses current inventory graph without dereferencing retired context entries");
fresh.Context.Run=fresh.Context.Run with{Day=2};fresh.Context.Store.LiveItems=Array.Empty<GameItem>();
MachineRuntimeApi.HandleNight(fresh.Context);
Check(fresh.Slots.Output.childItems.Count==1,"removed machine in cached context cannot process on next night");
// 第一台的回调移除第二台时，调度不得再使用调度开始时取得的原生引用。
NightReset();LifecycleContext? removingContext=null;GameItem? removedTarget=null;
var removing=NightFixture(resolver:_=>{removingContext!.Store.LiveItems=new[]{removingContext.Store.LiveItems[0]};removedTarget!.Destroy();return 1;});
var removingDefinition=MachineRegistrationApi.Definitions["test.machine"];
var removed=NightFixture();MachineRegistrationApi.Definitions["test.machine"]=removingDefinition;removingContext=removing.Context;removedTarget=removed.Machine;
removing.Context.Store.LiveItems=new[]{removing.Machine,removed.Machine};
MachineRuntimeApi.HandleNight(removing.Context);
Check(removing.Slots.Output.childItems.Count==1&&removed.Slots.Output.childItems.Count==0&&removed.Machine.GetTagReadonly("EZAPI_MACHINE_NIGHT_ATTEMPT_V1") is null,"machine consumed by previous callback is resolved as missing before any native dereference");
MachineRuntimeApi.Stop();InventoryApi.IsItemDragActive=false;
var report=new{passed=cases.Count-failed,failed,cases,nativeRuntimeVerified=false,scope="real recipe processor, automation, quarantine, nightly loop and real scheduler; explicit native/session stubs and custom transaction fixture, native lifecycle/session/directory stubs; no actual EndNight or save proof"};
File.WriteAllText("work/ezapi-machine-automation-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.passed,failed}));if(failed>0)Environment.Exit(1);
sealed class Tx:IMachineBatchTransaction {
 readonly MachineRecipePlan plan;readonly bool fail,failRestore;IReadOnlyList<GameItem> products=Array.Empty<GameItem>();GameItem? material;
 internal Tx(MachineRecipePlan p,bool f,bool fr)=>(plan,fail,failRestore)=(p,f,fr);
 public bool Validate()=>true;
 public void Apply(IReadOnlyList<GameItem> p){products=p;material=plan.Context.Items[0].Item;material.parentInventory!.Expel(material);if(fail)throw new Exception("expected partial");foreach(var item in p)plan.Inventory.Output.Add(item);}
 public bool Verify()=>true;
 public bool Restore(){if(failRestore)return false;foreach(var p in products)p.parentInventory?.Expel(p);if(material is not null&&material.parentInventory is null)plan.Inventory.ItemInput.Add(material);return true;}
 public void Fault(string reason){}
}

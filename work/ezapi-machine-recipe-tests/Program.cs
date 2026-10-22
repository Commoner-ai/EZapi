using EZapi;
using Il2Cpp;
using System.Text.Json;
var cases=new List<object>();var failed=0;
void Check(bool ok,string message="assertion"){if(!ok)throw new Exception(message);}
void Case(string name,Action test){try{Reset();test();cases.Add(new{name,passed=true});}catch(Exception e){failed++;cases.Add(new{name,passed=false,error=e.ToString()});}}
void Reject(Action test){try{test();}catch(ArgumentException){return;}throw new Exception("expected argument rejection");}
void Reset(){GameSession.RequireThread();GameSession.Identity=new(1,"run",1);InventoryApi.IsItemDragActive=false;MachineTransactionApi.Stop();MachineTransactionApi.Start();MachineRecipeApi.Stop();MachineRecipeApi.Start();Items.Made.Clear();}
World World()=>new();
MachineRecipe Recipe(Func<MachineRecipePlan,IMachineBatchTransaction>? factory=null)=>new("test.author.recipe",new[]{new MachineIngredient("input")},new("output")){LiquidInputs=new[]{new MachineLiquidIngredient("water",1)},Transaction=factory??(p=>new Tx(p))};
MachineBatchProcessor Processor(MachineRecipe r,MachinePowerRule? power=null)=>MachineRecipeApi.CreateBatchProcessor("test.author","machine",new[]{r},power);

// 小规模独立穷举模型：逐需求枚举每个库存可分配数量，与最大流/整堆搜索采用不同算法。
bool Oracle(int[] stock,int[] demand,bool[] whole,bool[,] eligible){
 var remaining=(int[])stock.Clone();
 bool Row(int r){if(r==demand.Length)return true;
  bool Item(int i,int need){if(i==stock.Length)return need==0&&Row(r+1);
   var cap=eligible[r,i]?Math.Min(need,remaining[i]):0;
   for(var take=0;take<=cap;take++) {
    if(whole[r]&&take>0&&(take!=stock[i]||remaining[i]!=stock[i]))continue;
    remaining[i]-=take;var okay=Item(i+1,need-take);remaining[i]+=take;if(okay)return true;
   }return false;
  }return Item(0,demand[r]);
 }return Row(0);
}
for(var seed=0;seed<350;seed++) {
 var capture=seed;
 Case("material oracle seed "+seed,()=>{
  var rng=new Random(capture);var stock=Enumerable.Range(0,rng.Next(1,6)).Select(_=>rng.Next(1,4)).ToArray();var demand=Enumerable.Range(0,rng.Next(1,4)).Select(_=>rng.Next(1,6)).ToArray();var whole=demand.Select(_=>rng.Next(2)==0).ToArray();var eligible=new bool[demand.Length,stock.Length];
  for(var r=0;r<demand.Length;r++)for(var i=0;i<stock.Length;i++)eligible[r,i]=rng.Next(3)!=0;
  var result=MachineIngredientMatching.Allocate(stock,demand,whole,eligible);Check((result is not null)==Oracle(stock,demand,whole,eligible),"feasibility differs from exhaustive model");
  if(result is not null)Check(result.Sum()==demand.Sum()&&result.Select((n,i)=>n>=0&&n<=stock[i]).All(b=>b),"conservation");
 });
}
Case("broad tag cannot steal strict item's only stock",()=>{var a=MachineIngredientMatching.Allocate(new[]{1,1},new[]{1,1},new[]{false,false},new[,]{{true,true},{true,false}});Check(a is not null&&a.SequenceEqual(new[]{1,1}));});
Case("whole stacks exact quantity, no overconsume",()=>Check(MachineIngredientMatching.Allocate(new[]{3},new[]{2},new[]{true},new[,]{{true}}) is null));
Case("whole stack search reassigns for partial constraints",()=>Check(MachineIngredientMatching.Allocate(new[]{2,2},new[]{2,2},new[]{true,false},new[,]{{true,true},{true,false}}) is not null));
Case("material limit rejects",()=>Reject(()=>MachineIngredientMatching.Allocate(new int[65],Array.Empty<int>(),Array.Empty<bool>(),new bool[0,65])));
Case("adversarial whole-stack search fails within shared budget",()=>{var matrix=new bool[2,64];for(var i=0;i<64;i++)matrix[0,i]=true;matrix[1,0]=true;try{MachineIngredientMatching.Allocate(Enumerable.Repeat(1,64).ToArray(),new[]{32,2},new[]{true,false},matrix);}catch(InvalidOperationException e){Check(e.Message.Contains("EZAPI_RECIPE_BUDGET"));return;}throw new Exception("expected bounded stop without partial allocation");});
Case("no ingredients returns zero take",()=>Check(MachineIngredientMatching.Allocate(new[]{1},Array.Empty<int>(),Array.Empty<bool>(),new bool[0,1])!.Single()==0));
foreach(var bad in new[]{"test.author\n","Test.author","test..author",".test",""," test"})Case("owner rejects "+JsonSerializer.Serialize(bad),()=>Reject(()=>MachineRecipeRules.Copy(bad,new[]{Recipe()})));
Case("omitted transaction selects native default strategy",()=>Check(MachineRecipeRules.Copy("test.author",new[]{Recipe() with {Transaction=null}}).Single().Transaction is null));
Case("material contents policy defaults to reject",()=>Check(MachineRecipeRules.Copy("test.author",new[]{Recipe()}).Single().MaterialContents==MachineMaterialContentsPolicy.Reject));
Case("explicit whole material graph policy preserved",()=>Check(MachineRecipeRules.Copy("test.author",new[]{Recipe() with {MaterialContents=MachineMaterialContentsPolicy.ConsumeGraph}}).Single().MaterialContents==MachineMaterialContentsPolicy.ConsumeGraph));
Case("unknown material contents policy rejected",()=>Reject(()=>MachineRecipeRules.Copy("test.author",new[]{Recipe() with {MaterialContents=(MachineMaterialContentsPolicy)7}})));
Case("requires owned recipe namespace",()=>Reject(()=>MachineRecipeRules.Copy("test.author",new[]{Recipe() with {RecipeId="other.recipe"}})));
Case("duplicate recipe rejected atomically",()=>Reject(()=>MachineRecipeRules.Copy("test.author",new[]{Recipe(),Recipe()})));
Case("ID and tag missing rejected",()=>Reject(()=>MachineRecipeRules.Copy("test.author",new[]{Recipe() with {ItemInputs=new[]{new MachineIngredient(null)}}})));
Case("null output rejected",()=>Reject(()=>MachineRecipeRules.Copy("test.author",new[]{Recipe() with {Output=null!}})));
Case("zero quantity rejected",()=>Reject(()=>MachineRecipeRules.Copy("test.author",new[]{Recipe() with {ItemInputs=new[]{new MachineIngredient("input",0)}}})));
Case("oversized liquid rejected",()=>Reject(()=>MachineRecipeRules.Copy("test.author",new[]{Recipe() with {LiquidInputs=new[]{new MachineLiquidIngredient("water",2147484)}}})));
Case("output cap64 enforced",()=>Reject(()=>MachineRecipeRules.Copy("test.author",new[]{Recipe() with {Output=new("out",65)}})));
Case("negative fee rejected",()=>Reject(()=>Processor(Recipe(),new(-1))));
Case("registration copied collections",()=>{var recipes=new[]{Recipe()};Check(MachineRecipeApi.RegisterRecipes("test.author","machine",recipes).Accepted);recipes[0]=recipes[0] with {RecipeId="test.author.changed"};Check(MachineRecipeApi.Snapshot().Recipes.Single().RecipeId=="test.author.recipe");});
Case("invalid replacement preserves revision and prior recipes",()=>{Check(MachineRecipeApi.RegisterRecipes("test.author","machine",new[]{Recipe()}).Accepted);var snap=MachineRecipeApi.Snapshot();Check(!MachineRecipeApi.RegisterRecipes("test.author","machine",new[]{Recipe(),Recipe()}).Accepted);Check(MachineRecipeApi.Snapshot().Revision==snap.Revision&&MachineRecipeApi.Snapshot().Recipes.Count==1);});
Case("different owner cannot replace another machine catalog",()=>{MachineRecipeApi.RegisterRecipes("test.author","machine",new[]{Recipe()});Check(!MachineRecipeApi.RegisterRecipes("other","machine",Array.Empty<MachineRecipe>()).Accepted);});
Case("empty registration releases owner catalog",()=>{MachineRecipeApi.RegisterRecipes("test.author","machine",new[]{Recipe()});Check(MachineRecipeApi.RegisterRecipes("test.author","machine",Array.Empty<MachineRecipe>()).Accepted&&MachineRecipeApi.Snapshot().Recipes.Count==0);});
Case("registry dispatches actual transaction",()=>{var w=World();MachineRecipeApi.RegisterRecipes("test.author","machine",new[]{Recipe()});Check(MachineRecipeApi.ProcessBatch(w.Machine).Committed&&w.Input.parentInventory is null);});
Case("registry missing gives no match without products",()=>{var w=World();Check(MachineRecipeApi.ProcessBatch(w.Machine).Transaction is null&&Items.Made.Count==0);});
Case("commits real selected resources and placements",()=>{var w=World();var result=Processor(Recipe()).Process(w.Machine);Check(result.Committed&&w.Battery.Energy==93&&w.Water.Water.TotalParts==499000&&w.Slots.Output.childItems.Count==1);});
Case("base values are total selected units without second multiply",()=>{var w=World();w.Input.unitCount=3;w.Input.unitValue=13;MachineRecipePlan? plan=null;Processor(Recipe(p=>{plan=p;return new Tx(p);}) with {ItemInputs=new[]{new MachineIngredient("input",2)}}).Process(w.Machine);Check(plan!.Context.Items.Single().Value==26);});
Case("tag and ID intersection enforced",()=>{var w=World();var result=Processor(Recipe() with {ItemInputs=new[]{new MachineIngredient("input"){ItemTag="metal"}}}).Process(w.Machine);Check(result.Transaction is null&&Items.Made.Count==0);});
Case("missing materials never invokes transaction",()=>{var w=World();var called=0;var r=Recipe(p=>{called++;return new Tx(p);}) with {ItemInputs=new[]{new MachineIngredient("wrong")}};Check(!Processor(r).Process(w.Machine).Committed&&called==0&&Items.Made.Count==0);});
Case("condition false never generates",()=>{var w=World();Check(Processor(Recipe() with {MachineCondition=_=>false}).Process(w.Machine).Transaction is null&&Items.Made.Count==0);});
Case("repeated liquid slot cannot spend600ml from500ml",()=>{var w=World();var r=Recipe() with {LiquidInputs=new[]{new MachineLiquidIngredient("water",300),new MachineLiquidIngredient("water",300)}};Check(Processor(r).Process(w.Machine).Transaction is null&&Items.Made.Count==0);});
Case("dynamic quantity and fee resolved against final count",()=>{var w=World();var r=Recipe() with {Output=new("output",ResolveCount:_=>2)};Check(Processor(r,new(7,PerOutput:true)).Process(w.Machine).Committed&&w.Battery.Energy==86&&w.Slots.Output.childItems.Count==2);});
Case("dynamic item resolves final quantity",()=>{var w=World();var r=Recipe() with {Output=new("output",2){ResolveItemId=c=>c.OutputCount==2?"chosen":"wrong"}};Check(Processor(r).Process(w.Machine).Committed&&w.Slots.Output.childItems.All(i=>i.identifier=="chosen"));});
Case("insufficient power is read-only",()=>{var w=World();w.Battery.Energy=0;Check(Processor(Recipe()).Process(w.Machine).Transaction is null&&Items.Made.Count==0&&w.Input.parentInventory is not null);});
Case("zero fee requires no battery",()=>{var w=World();w.Slots.Battery!.Expel(w.Battery);Check(Processor(Recipe(),new(0)).Process(w.Machine).Committed);});
Case("required battery rejects empty slot even at zero cost before author callback",()=>{var w=World();w.Machine.Layout=w.Slots with {RequiresBattery=true};w.Slots.Battery!.Expel(w.Battery);var calls=0;var r=Recipe() with {MachineCondition=_=>{calls++;return true;}};var result=Processor(r,new(0)).Process(w.Machine);Check(result.Error!.StartsWith("EZAPI_RECIPE_BATTERY_REQUIRED")&&result.Transaction is null&&calls==0&&Items.Made.Count==0&&w.Input.parentInventory==w.Slots.ItemInput);});
Case("required battery accepts an empty but native-recognized source at zero cost",()=>{var w=World();w.Machine.Layout=w.Slots with {RequiresBattery=true};w.Battery.Energy=0;Check(Processor(Recipe(),new(0)).Process(w.Machine).Committed&&w.Battery.Energy==0);});
Case("required battery rejects frozen source without creating output",()=>{var w=World();w.Machine.Layout=w.Slots with {RequiresBattery=true};w.Battery.Frozen=true;Check(Processor(Recipe(),new(0)).Process(w.Machine).Error!.StartsWith("EZAPI_RECIPE_BATTERY_REQUIRED")&&Items.Made.Count==0);});
Case("required battery rejects unowned source",()=>{var w=World();w.Machine.Layout=w.Slots with {RequiresBattery=true};w.Battery.Owned=false;Check(Processor(Recipe(),new(0)).Process(w.Machine).Error!.StartsWith("EZAPI_RECIPE_BATTERY_REQUIRED")&&Items.Made.Count==0);});
Case("required battery availability is rechecked after PrepareItem",()=>{var w=World();w.Machine.Layout=w.Slots with {RequiresBattery=true};var r=Recipe() with {Output=new("out",PrepareItem:(_,_)=>w.Battery.Frozen=true)};var result=Processor(r,new(0)).Process(w.Machine);Check(!result.Committed&&result.Transaction?.Status==MachineTransactionStatus.Rejected&&w.Input.parentInventory==w.Slots.ItemInput&&w.Slots.Output.childItems.Count==0);});
Case("negative dynamic quantity rejected before creation",()=>{var w=World();Check(Processor(Recipe() with {Output=new("out",ResolveCount:_=>-1)}).Process(w.Machine).Error!.Contains("CALLBACK")&&Items.Made.Count==0);});
Case("callback throwing aborts selection without fallback",()=>{var w=World();var r=Recipe() with {MachineCondition=_=>throw new Exception("condition")};Check(Processor(r).Process(w.Machine).Error!.Contains("condition")&&Items.Made.Count==0);});
Case("nested processing forbidden during factory",()=>{var w=World();MachineBatchProcessor? proc=null;MachineBatchResult? nested=null;proc=Processor(Recipe(p=>{nested=proc!.Process(w.Machine);return new Tx(p);}));Check(proc.Process(w.Machine).Committed&&nested!.Error=="EZAPI_RECIPE_REENTRANT_OR_STALE");});
Case("registry mutation rejected while processing",()=>{var w=World();ContentSubmissionResult? update=null;var r=Recipe(p=>{update=MachineRecipeApi.RegisterRecipes("test.author","machine",Array.Empty<MachineRecipe>());return new Tx(p);});Check(Processor(r).Process(w.Machine).Committed&&update?.Accepted==false);});
Case("stop restart does not revive processor",()=>{var w=World();var p=Processor(Recipe());MachineRecipeApi.Stop();MachineRecipeApi.Start();Check(p.Process(w.Machine).Error=="EZAPI_RECIPE_REENTRANT_OR_STALE"&&Items.Made.Count==0);});
Case("full output planned before resource debit",()=>{var w=World();for(var i=0;i<4;i++)w.Slots.Output.Add(new());Tx? tx=null;var result=Processor(Recipe(p=>tx=new(p))).Process(w.Machine);Check(result.Transaction?.Status==MachineTransactionStatus.Rejected&&tx!.Applies==0&&w.Battery.Energy==100&&Items.Made.All(i=>i.Destroyed));});
Case("partial resource failure compensated",()=>{var w=World();Tx? tx=null;var result=Processor(Recipe(p=>tx=new(p){FailApply=true})).Process(w.Machine);Check(result.Transaction?.Status==MachineTransactionStatus.Restored&&tx!.Restores==1&&w.Battery.Energy==100&&w.Input.parentInventory==w.Slots.ItemInput&&w.Water.Water.TotalParts==500000);});
Case("wrong energy delta triggers restore despite inner Verify",()=>{var w=World();var result=Processor(Recipe(p=>new Tx(p){WrongEnergy=true})).Process(w.Machine);Check(result.Transaction?.Status==MachineTransactionStatus.Restored&&w.Battery.Energy==100);});
Case("wrong destination cannot count as delivered",()=>{var w=World();var result=Processor(Recipe(p=>new Tx(p){WrongTarget=true})).Process(w.Machine);Check(result.Transaction?.Status==MachineTransactionStatus.Restored&&Items.Made.All(i=>i.Destroyed));});
Case("frozen machine prevents Apply",()=>{var w=World();w.Machine.Frozen=true;Check(Processor(Recipe()).Process(w.Machine).Transaction?.Status==MachineTransactionStatus.Rejected&&Items.Made.Count==0);});
Case("native removal veto respected",()=>{var w=World();w.Input.May=false;Check(Processor(Recipe()).Process(w.Machine).Transaction?.Status==MachineTransactionStatus.Rejected&&Items.Made.Count==0);});
Case("configuration callback cannot silently change energy",()=>{var w=World();var r=Recipe() with {Output=new("out",PrepareItem:(_,_)=>w.Battery.Energy--)};Check(Processor(r).Process(w.Machine).Transaction?.Status==MachineTransactionStatus.Rejected&&w.Input.parentInventory is not null);});
Case("transaction factory cannot silently change units",()=>{var w=World();Check(Processor(Recipe(p=>{w.Input.unitCount++;return new Tx(p);})).Process(w.Machine).Transaction?.Status==MachineTransactionStatus.Rejected);});
Case("resolved contents must physically exist",()=>{var w=World();var r=Recipe() with {Output=new("out"){Contents=new[]{new MachineLiquidAmount("water",1)}}};Check(Processor(r).Process(w.Machine).Transaction?.Status==MachineTransactionStatus.Restored);});
Case("definition deep copies nested content and ingredients",()=>{var input=new[]{new MachineIngredient("input")};var liquids=new[]{new MachineLiquidAmount("water",1)};var r=Recipe() with {ItemInputs=input,Output=new("out"){Contents=liquids}};var copy=MachineRecipeRules.Copy("test.author",new[]{r})[0];input[0]=new("other");liquids[0]=new("other",4);Check(copy.ItemInputs[0].ItemId=="input"&&copy.Output.Contents[0].LiquidId=="water");});
Case("success notification exactly once and failure never notifies",()=>{var w=World();var notes=0;Check(Processor(Recipe() with {AfterCommitted=(_,_)=>notes++}).Process(w.Machine).Committed&&notes==1);var second=World();Processor(Recipe(p=>new Tx(p){FailApply=true}) with {AfterCommitted=(_,_)=>notes++}).Process(second.Machine);Check(notes==1);});
Case("notification exception keeps committed result",()=>{var w=World();var result=Processor(Recipe() with {AfterCommitted=(_,_)=>throw new Exception("notify")}).Process(w.Machine);Check(result.Committed&&result.Transaction!.CompletionError!.Contains("notify")&&!result.Transaction.MayRetry&&w.Battery.Energy==93);});
Case("false Restore success cannot hide resource loss",()=>{var w=World();var result=Processor(Recipe(p=>new Tx(p){FailApply=true,LieRestore=true})).Process(w.Machine);Check(result.Transaction?.Status==MachineTransactionStatus.RecoveryFailed&&!result.Transaction.MayRetry);});
Case("input admission current recipes and auxiliary, removal, dispose",()=>{var w=World();MachineRecipeApi.RegisterRecipes("test.author","machine",new[]{Recipe() with {AuxiliaryItemIds=new[]{"catalyst"}}});using(var lease=MachineRecipeApi.InstallInputAdmission("test.author","machine")){Check(InventoryAdmissionApi.Predicate!(w.Input,new()));Check(InventoryAdmissionApi.Predicate!(new(){identifier="catalyst"},new()));Check(!InventoryAdmissionApi.Predicate!(new(){identifier="unrelated"},new()));MachineRecipeApi.RegisterRecipes("test.author","machine",new[]{Recipe() with {ItemInputs=new[]{new MachineIngredient("changed")}}});Check(!InventoryAdmissionApi.Predicate!(w.Input,new()));MachineRecipeApi.RegisterRecipes("test.author","machine",Array.Empty<MachineRecipe>());Check(InventoryAdmissionApi.Predicate!(w.Input,new()));}Check(InventoryAdmissionApi.Predicate is null);});
// 容器分支验证真实选择/审计及自定义事务协议；标准原生写入由游戏夹具验证。
Case("container declarations preserve subtype and snapshot contents",()=>{
 var amounts=new[]{new MachineLiquidAmount("water",1)};
 var c=MachineRecipeRules.Copy("test.author",new[]{Recipe() with {Output=new MachineContainerOutput(amounts)}}).Single();
 amounts[0]=new("water",9);Check(c.Output is MachineContainerOutput&&c.Output.Contents[0].Millilitres==1);
});
Case("container rejects inherited item output fields",()=>Reject(()=>MachineRecipeRules.Copy("test.author",new[]{Recipe() with {Output=new MachineContainerOutput(Array.Empty<MachineLiquidAmount>()){Count=2}}})));
foreach(var mode in new[]{"success","full","missing","false-condition","changed-resolver","null-resolver","empty","unknown-id","false-verify","lying-restore"}) {
 var captured=mode;Case("container selector "+mode,()=>{
  var w=World();var slot=new GameSlotInventory();w.Machine.Layout=w.Slots with {Output=slot};
  var target=new GameItem{identifier="vessel",Water=new(2000,new[]{new MachineLiquidPart("water",captured=="full"?1500:500)})};
  if(captured!="missing")slot.Add(target);var before=target.Water;var source=w.Water.Water;var original=w.Input.parentInventory;
  var calls=0;var notes=0;
  var output=new MachineContainerOutput(new[]{new MachineLiquidAmount(captured=="unknown-id"?"other":"water",captured=="empty"?0:1)}) {
   Condition=_=>captured!="false-condition",
   ResolveContents=captured=="null-resolver"?_=>null!:captured=="changed-resolver"?_=>{target.Water=new(2000,new[]{new MachineLiquidPart("water",600)});return new[]{new MachineLiquidPart("water",1000)};}:null
  };
  var recipe=Recipe(p=>{calls++;return new ContainerTx(p,captured=="false-verify"||captured=="lying-restore",captured=="lying-restore");}) with {
   Output=output,AfterCommitted=(_,products)=>{notes++;Check(products.Count==0);}
  };
  var result=Processor(recipe).Process(w.Machine);
  Check(Items.Made.Count==0,"container output must never create new item products");
  if(captured=="success")Check(result.Committed&&target.Water.TotalParts==1500&&w.Water.Water.TotalParts==source.TotalParts-1000&&notes==1);
  else if(captured=="false-verify")Check(result.Transaction?.Status==MachineTransactionStatus.Restored&&target.Water==before&&w.Water.Water==source&&w.Input.parentInventory==original&&notes==0);
  else if(captured=="lying-restore")Check(result.Transaction?.Status==MachineTransactionStatus.RecoveryFailed&&notes==0);
  else Check(!result.Committed&&result.Transaction is null&&calls==0&&notes==0);
 });
}
foreach(var mode in new[]{"output-denied","input-denied","output-changed","input-changed"}) {
 var captured=mode;Case("template container condition recheck "+mode,()=>{
  var w=World();var output=new GameSlotInventory();w.Machine.Layout=w.Slots with {Output=output};
  var target=new GameItem{identifier="target",Water=new(2000,new[]{new MachineLiquidPart("water",0)})};output.Add(target);
  int outputCalls=0,inputCalls=0,transactions=0;
  MachineLayoutApi.Admission=(i,inv)=>{
   if(inv==output){outputCalls++;return captured!="output-denied"&&(captured!="output-changed"||outputCalls==1);}
   inputCalls++;return captured!="input-denied"&&(captured!="input-changed"||inputCalls==1);
  };
  try {
   var recipe=Recipe(p=>{transactions++;return new ContainerTx(p,false,false);}) with {Output=new MachineContainerOutput(new[]{new MachineLiquidAmount("water",1)})};
   var result=Processor(recipe).Process(w.Machine);Check(!result.Committed&&Items.Made.Count==0&&w.Input.parentInventory==w.Slots.ItemInput&&w.Battery.Energy==100&&target.Water.TotalParts==0);
   Check(transactions==(captured.EndsWith("denied")?0:1),"early mismatch prevents transaction; later mismatch rejected by eligibility");
  }finally{MachineLayoutApi.Admission=null;}
 });
}

// 公共显式inventory契约：既支持自定义窗口，也拒绝借错对象及回调中换窗。
MachineLayoutDefinition Template(bool required=false)=>new(){ItemInput=new(4,4),Output=new(){Size=new(4,4)},Battery=new(new(2,2),required),LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Size=new(2,2)}}};
MachineBatchProcessor Bound(MachineRecipe recipe,MachineLayoutDefinition? template=null)=>MachineRecipeApi.CreateBoundProcessor("test.author",new("machine",template??Template(),new[]{recipe}){Power=new(0)});
Case("explicit canonical inventory commits",()=>{var w=World();Check(Processor(Recipe()).Process(w.Machine,w.Slots).Committed);});
Case("explicit custom window works without EZapi marker",()=>{var w=World();w.Machine.Layout=null;Check(Processor(Recipe()).Process(w.Machine,w.Slots).Committed&&w.Slots.Output.childItems.Count==1);});
Case("implicit custom window requires explicit inventory",()=>{var w=World();w.Machine.Layout=null;Check(Processor(Recipe()).Process(w.Machine).Error!.Contains("EZAPI_RECIPE_INVENTORY")&&Items.Made.Count==0);});
Case("explicit null rejects before processing",()=>{var w=World();try{Processor(Recipe()).Process(w.Machine,null!);}catch(ArgumentNullException){Check(Items.Made.Count==0);return;}throw new Exception("null accepted");});
foreach(var mode in new[]{"foreign","duplicate","missing-parent","missing-window","zero-pointer","missing-input","missing-liquids","bad-liquid-key","omit-battery","swap-roles","downgrade-required"}){
 var selectedMode=mode;Case("borrowed inventory rejects "+mode,()=>{
  var w=World();var view=w.Slots;var calls=0;
  if(selectedMode=="foreign"){var other=World();view=other.Slots;}
  if(selectedMode=="duplicate")view=view with{Output=view.ItemInput};
  if(selectedMode=="missing-parent")view.ItemInput.ParentItem=null;
  if(selectedMode=="missing-window")w.Machine.contentWindow=null;
  if(selectedMode=="zero-pointer")view.Output.Pointer=IntPtr.Zero;
  if(selectedMode=="missing-input")view=view with{ItemInput=null!};
  if(selectedMode=="missing-liquids")view=view with{LiquidInputs=null!};
  if(selectedMode=="bad-liquid-key")view=view with{LiquidInputs=new Dictionary<string,GameSlotInventory>{{"bad.key",view.LiquidInputs["water"]}}};
  if(selectedMode=="omit-battery")view=view with{Battery=null};
  if(selectedMode=="swap-roles")view=view with{ItemInput=view.Output,Output=view.ItemInput};
  if(selectedMode=="downgrade-required")w.Machine.Layout=view with{RequiresBattery=true};
  var result=Processor(Recipe() with{MachineCondition=_=>{calls++;return true;}}).Process(w.Machine,view);
  Check(!result.Committed&&result.Transaction is null&&calls==0&&Items.Made.Count==0&&w.Input.parentInventory==w.Slots.ItemInput);
 });
}
Case("bound template accepts matching custom window",()=>{var w=World();w.Machine.Layout=null;Check(Bound(Recipe()).Process(w.Machine,w.Slots).Committed);});
Case("bound template rejects wrong dimensions before callbacks",()=>{var w=World();var calls=0;var t=new MachineLayoutDefinition{ItemInput=new(3,4),Battery=new(new(2,2),false),LiquidInputs=Template().LiquidInputs};var result=Bound(Recipe() with{MachineCondition=_=>{calls++;return true;}},t).Process(w.Machine,w.Slots);Check(result.Transaction is null&&calls==0&&Items.Made.Count==0);});
Case("bound template rejects incompatible liquid IDs on creation",()=>Reject(()=>Bound(Recipe(),new(){Battery=new(new(2,2),false)})));
Case("bound template rejects wrong output kind on creation",()=>Reject(()=>Bound(Recipe() with{Output=new MachineContainerOutput(new[]{new MachineLiquidAmount("water",1)})})));
Case("bound template snapshots caller arrays",()=>{var w=World();var list=new[]{new MachineContainerSlot{SlotId="water",Size=new(2,2)}};var proc=Bound(Recipe(),new(){Battery=new(new(2,2),false),LiquidInputs=list});list[0]=new(){SlotId="other",Size=new(1,1)};Check(proc.Process(w.Machine).Committed);});
Case("bound required source cannot be downgraded by borrowed view",()=>{var w=World();w.Machine.Layout=null;w.Slots.Battery!.Expel(w.Battery);var result=Bound(Recipe(),Template(true)).Process(w.Machine,w.Slots);Check(result.Error!.Contains("EZAPI_RECIPE_BATTERY_REQUIRED")&&Items.Made.Count==0);});
Case("bound template enforces liquid whitelist without registration",()=>{var w=World();w.Machine.Layout=null;var t=new MachineLayoutDefinition{Battery=new(new(2,2),false),LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",AllowedIds=new[]{"different"}}}};Check(!Bound(Recipe(),t).Process(w.Machine,w.Slots).Committed&&Items.Made.Count==0);});
Case("bound template condition rechecked after prepare",()=>{var w=World();var accept=true;w.Machine.Layout=null;var t=new MachineLayoutDefinition{Battery=new(new(2,2),false),LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Condition=_=>accept}}};var recipe=Recipe() with{Output=new("output"){PrepareItem=(_,_)=>accept=false}};var result=Bound(recipe,t).Process(w.Machine,w.Slots);Check(!result.Committed&&w.Input.parentInventory==w.Slots.ItemInput&&w.Water.Water.TotalParts==500000&&w.Battery.Energy==100);});
foreach(var mode in new[]{"reparent","replace-window","resize-template","replace-canonical","install-canonical"}){
 var selectedMode=mode;Case("callback invalidates borrowed graph "+mode,()=>{
  var w=World();w.Machine.Layout=null;var calls=0;
  var recipe=Recipe(p=>{calls++;return new Tx(p);}) with{MachineCondition=_=>{
   if(selectedMode=="reparent")w.Slots.ItemInput.ParentItem=new GameItem();
   if(selectedMode=="replace-window")w.Machine.contentWindow=new();
   if(selectedMode=="resize-template")((GameGridInventory)w.Slots.ItemInput).inventoryShape.globalWidth=3;
   if(selectedMode is "replace-canonical" or "install-canonical")w.Machine.Layout=w.Slots;
   return true;
  }};
  var result=Bound(recipe).Process(w.Machine,w.Slots);Check(!result.Committed&&Items.Made.Count==0&&calls==0&&w.Input.parentInventory==w.Slots.ItemInput);
 });
}
Case("caller mapping mutation cannot swap selected liquid slot",()=>{var w=World();w.Machine.Layout=null;var map=new Dictionary<string,GameSlotInventory>(w.Slots.LiquidInputs);var supplied=w.Slots with{LiquidInputs=map};var recipe=Recipe() with{MachineCondition=_=>{map["water"]=new();return true;}};Check(Processor(recipe).Process(w.Machine,supplied).Committed&&w.Water.Water.TotalParts==499000);});

// 真实processor/graph源码：只读回调覆盖所有解析入口，标签写入不因数量不变而放行。
foreach(var mode in new[]{"machine-condition","false-condition","item-condition","false-item-condition","liquid-condition","false-liquid-condition","liquid-amount","output-count","output-id","power-cost","liquid-output-amount","liquid-output-value","liquid-output-quality","transaction-factory","transaction-validate","prepare-input-tag","prepare-machine-tag","prepare-battery-tag","prepare-water-tag","prepare-output-tag","prepare-nested-tag"}) {
 var captured=mode;Case("read-only state guard "+mode,()=>{
  var w=World();var callbacks=0;var factory=0;Tx? tx=null;GameItem? descendant=null;
  void Mutate(GameItem? target=null){callbacks++;(target??w.Input).state.GetTag("test.illegal").SetString("changed");}
  var recipe=Recipe(p=>{factory++;tx=new Tx(p);return tx;});var power=new MachinePowerRule(0);
  switch(captured){
   case "machine-condition":case "false-condition":recipe=recipe with{MachineCondition=_=>{Mutate(w.Machine);return captured!="false-condition";}};break;
   case "item-condition":case "false-item-condition":recipe=recipe with{ItemInputs=new[]{new MachineIngredient("input",Condition:_=>{Mutate();return captured!="false-item-condition";})}};break;
   case "liquid-condition":case "false-liquid-condition":recipe=recipe with{LiquidInputs=new[]{new MachineLiquidIngredient("water",1,Condition:_=>{Mutate(w.Water);return captured!="false-liquid-condition";})}};break;
   case "liquid-amount":recipe=recipe with{LiquidInputs=new[]{new MachineLiquidIngredient("water",1,ResolveMillilitres:_=>{Mutate();return 1;})}};break;
   case "output-count":recipe=recipe with{Output=recipe.Output with{ResolveCount=_=>{Mutate();return 1;}}};break;
   case "output-id":recipe=recipe with{Output=recipe.Output with{ResolveItemId=_=>{Mutate();return "output";}}};break;
   case "power-cost":power=new(0,ResolveCost:_=>{Mutate(w.Battery);return 0;});break;
   case "liquid-output-amount":recipe=recipe with{Output=recipe.Output with{Contents=new[]{new MachineLiquidAmount("water",1,ResolveMillilitres:_=>{Mutate();return 1;})}}};break;
   case "liquid-output-value":recipe=recipe with{Output=recipe.Output with{Contents=new[]{new MachineLiquidAmount("water",1,ResolveValue:_=>{Mutate();return 1;})}}};break;
   case "liquid-output-quality":recipe=recipe with{Output=recipe.Output with{Contents=new[]{new MachineLiquidAmount("water",1){ResolveQualityBasis=_=>{Mutate();return 1;}}}}};break;
   case "transaction-factory":recipe=recipe with{Transaction=p=>{Mutate();factory++;return tx=new Tx(p);}};break;
   case "transaction-validate":recipe=recipe with{Transaction=p=>{factory++;return new InvalidValidator(new Tx(p),()=>Mutate());}};break;
   default:
    if(captured=="prepare-nested-tag"){descendant=new GameItem{identifier="nested"};new GameInventory(w.Input).Add(descendant);}
    var outputItem=new GameItem{identifier="existing"};w.Slots.Output.Add(outputItem);
    recipe=recipe with{Output=recipe.Output with{PrepareItem=(_,_)=>Mutate(captured switch{"prepare-machine-tag"=>w.Machine,"prepare-battery-tag"=>w.Battery,"prepare-water-tag"=>w.Water,"prepare-output-tag"=>outputItem,"prepare-nested-tag"=>descendant,_=>w.Input})}};break;
  }
  var before=w.Slots.Output.childItems.Count;var result=Processor(recipe,power).Process(w.Machine);
  Check(!result.Committed&&callbacks>0&&(tx?.Applies??0)==0&&w.Input.parentInventory==w.Slots.ItemInput&&w.Slots.Output.childItems.Count==before,"illegal callback cannot Apply or consume");
  Check(w.Battery.Energy==100&&w.Water.Water.TotalParts==500000,"no energy/liquid debit");
  Check(result.Error?.StartsWith("EZAPI_RECIPE_STALE")==true||result.Transaction?.Status==MachineTransactionStatus.Rejected,"explicit stale/precheck rejection");
  Check(!string.IsNullOrEmpty((captured is "machine-condition" or "false-condition" or "prepare-machine-tag"?w.Machine:captured is "liquid-condition" or "false-liquid-condition" or "prepare-water-tag"?w.Water:captured is "power-cost" or "prepare-battery-tag"?w.Battery:captured=="prepare-output-tag"?w.Slots.Output.childItems[0]:captured=="prepare-nested-tag"?descendant!:w.Input).GetTagReadonly("test.illegal")?.valueString),"detection does not claim rollback of author mutation");
 });
}
var report=new{passed=cases.Count-failed,failed,cases,scope="real recipe definitions, allocator, processor, transaction and liquid math; native game/stock consumption substituted"};
File.WriteAllText("work/ezapi-machine-recipe-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{report.passed} passed, {failed} failed");if(failed>0){foreach(var c in cases.Where(c=>c.ToString()!.Contains("passed = False")))Console.WriteLine(c);Environment.Exit(1);}

sealed class World {
 internal readonly GameItem Machine=new(){identifier="machine"},Input=new(){identifier="input"},Water=new(){identifier="container"},Battery=new(){identifier="battery"};
 internal readonly MachineInventory Slots;
 internal World(){var b=new GameSlotInventory();b.Add(Battery);var liquid=new GameSlotInventory();liquid.Add(Water);var input=new GameGridInventory();input.Add(Input);Slots=new(b,null,input,new GameGridInventory(),new Dictionary<string,GameSlotInventory>{{"water",liquid}},null);Machine.Layout=Slots;}
}
sealed class Tx:IMachineBatchTransaction {
 readonly MachineRecipePlan p;readonly Dictionary<GameItem,(GameInventory? Parent,int Units)> original;readonly GameItem? battery;readonly int energy;readonly Dictionary<GameItem,MachineLiquidSnapshot> water;
 IReadOnlyList<GameItem> products=Array.Empty<GameItem>();internal bool FailApply,WrongEnergy,WrongTarget,LieRestore;internal int Applies,Restores;
 internal Tx(MachineRecipePlan plan){p=plan;original=plan.Context.Items.ToDictionary(t=>t.Item,t=>(t.Item.parentInventory,t.Item.unitCount));battery=plan.Inventory.Battery?.childItems.FirstOrDefault();energy=battery?.Energy??0;water=plan.Context.LiquidContainers.Values.ToDictionary(i=>i,i=>i.Water);}
 public bool Validate()=>true;
 public void Apply(IReadOnlyList<GameItem> fresh){Applies++;products=fresh;foreach(var t in p.Context.Items)t.Item.parentInventory!.Expel(t.Item);foreach(var t in p.Context.Liquids)t.Container.Water=t.After;if(battery is not null)battery.Energy-=p.PowerCost+(WrongEnergy?1:0);if(FailApply)throw new Exception("partial");foreach(var product in fresh)(WrongTarget?new GameGridInventory():p.Inventory.Output).Add(product);}
 public bool Verify()=>true;
 public bool Restore(){Restores++;if(LieRestore)return true;foreach(var item in products)item.parentInventory?.Expel(item);foreach(var pair in original){pair.Key.unitCount=pair.Value.Units;if(pair.Key.parentInventory is null)pair.Value.Parent!.Add(pair.Key);}if(battery is not null)battery.Energy=energy;foreach(var pair in water)pair.Key.Water=pair.Value;return true;}
 public void Fault(string reason){}
}
sealed class ContainerTx:IMachineBatchTransaction {
 readonly MachineRecipePlan plan;readonly Tx input;readonly bool refuse,lie;
 internal ContainerTx(MachineRecipePlan p,bool refuse,bool lie){plan=p;input=new(p);this.refuse=refuse;this.lie=lie;}
 public bool Validate()=>input.Validate();
 public void Apply(IReadOnlyList<GameItem> products){input.Apply(products);plan.OutputContainer!.Water=plan.OutputContainerAfter!;}
 public bool Verify()=>!refuse;
 public bool Restore(){if(lie)return true;var restored=input.Restore();plan.OutputContainer!.Water=plan.OutputContainerBefore!;return restored;}
 public void Fault(string reason){}
}

sealed class InvalidValidator:IMachineBatchTransaction {readonly Tx inner;readonly Action mutate;internal InvalidValidator(Tx t,Action m)=>(inner,mutate)=(t,m);public bool Validate(){mutate();return true;}public void Apply(IReadOnlyList<GameItem> p)=>inner.Apply(p);public bool Verify()=>inner.Verify();public bool Restore()=>inner.Restore();public void Fault(string reason)=>inner.Fault(reason);}

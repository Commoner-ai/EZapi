using EZapi;
using Il2Cpp;
var checks=new List<string>();int failed=0;
void Check(bool ok,string name){checks.Add(name);if(!ok){failed++;Console.Error.WriteLine("FAIL "+name);}}
MachineRecipe Recipe(string owner="test.mod",string suffix="recipe")=>new(owner+"."+suffix,new[]{new MachineIngredient("NATIVE_A")},new("NATIVE_B"));
MachineDefinition Def(string suffix="machine")=>new("test.mod."+suffix,new(){ItemInput=new(3,3)},new[]{Recipe()}){Power=new(0),ItemOptions=new("测试机器")};
void Reset(){MachineRegistrationApi.Stop();RecipeGuideApi.Stop();NativeFactoryRegistry.Items.Clear();MachineRecipeApi.Catalog.Clear();NativeFactoryRegistry.Reject=false;InventoryAdmissionApi.Reject=false;MachineContainerConditionApi.Reject=false;MachineRecipeApi.Reject=null;MachineRecipeApi.CanEdit=true;ItemRegistry.Error=null;ContentApi.Observer=null;RecipeGuideApi.Start();MachineRegistrationApi.Start();}
bool Register(MachineDefinition d)=>MachineRegistrationApi.RegisterMachine("test.mod",d).Accepted;
void Rejected(MachineDefinition d,string label){Reset();Check(!Register(d),label);Check(NativeFactoryRegistry.Items.Count==0&&MachineRecipeApi.Catalog.Count==0&&RecipeGuideApi.Snapshot().Recipes.Count==0&&InventoryAdmissionApi.Active==0,label+" leaves no partial state");}
Reset();MachineRegistrationApi.Stop();Check(!Register(Def()),"stopped registration rejected");MachineRegistrationApi.Start();
foreach(var id in new[]{"other.mod.machine","test.mod.","test.mod.Bad","test.mod.x\n","test.mod."+new string('a',140)})Rejected(Def() with {MachineId=id},"invalid machine ID "+id.Trim());
Rejected(Def() with {Template=null!},"null layout");
Rejected(Def() with {Template=new(){ItemInput=new(0,1)}},"invalid layout dimensions");
Rejected(Def() with {Recipes=null!},"null recipes");
Rejected(Def() with {Recipes=new[]{Recipe() with {RecipeId="other.recipe"}}},"wrong recipe owner");
Rejected(Def() with {ProductionMarkupPercent=-1},"negative markup");
Rejected(Def() with {ProductionMarkupPercent=1000001},"excess markup");
Rejected(Def() with {Power=new(-1)},"negative power");
Rejected(Def() with {Power=new(1)},"positive power without battery");
Rejected(Def() with {Power=new(0,_=>1)},"dynamic power without battery");
Rejected(Def() with {Template=new(){Output=new(){Kind="container"}}},"item recipe cannot use container output template");
Reset();Check(Register(Def() with {Template=new(){Output=new(){Kind="container"}},Recipes=new[]{Recipe() with {Output=new MachineContainerOutput(new[]{new MachineLiquidAmount("NATIVE_WATER",1)})}}}),"container recipe and automatic guide register with matching template");
Check(MachineRegistrationApi.GuideSnapshot().Recipes.Single().Output.Quantity=="液体（加入已有容器）","container automatic guide describes existing vessel instead of new item");
Rejected(Def() with {Recipes=new[]{Recipe() with {Output=new MachineContainerOutput(new[]{new MachineLiquidAmount("NATIVE_WATER",1)})}}},"container recipe cannot use item grid template");
Rejected(Def() with {Recipes=new[]{Recipe() with {LiquidInputs=new[]{new MachineLiquidIngredient("water",1)}}}},"missing liquid slot");
Rejected(Def() with {Recipes=new[]{Recipe() with {ItemInputs=new[]{new MachineIngredient(null){ItemTag="tag"}}}}},"tag-only auto guide needs illustration");
Rejected(Def() with {Guide=new("")},"invalid guide category");
Rejected(Def() with {Guide=new(Note:new string('x',4001))},"overlong automatic guide");
Reset();ItemRegistry.Error="directory already applied";Check(!Register(Def())&&InventoryAdmissionApi.Active==0,"late declaration rejected before admission");
Reset();NativeFactoryRegistry.Reject=true;Check(!Register(Def()),"factory staging rejection propagated");Check(InventoryAdmissionApi.Active==0&&MachineRecipeApi.Catalog.Count==0&&RecipeGuideApi.Snapshot().Recipes.Count==0,"factory rejection disposes reserved admission");
Reset();InventoryAdmissionApi.Reject=true;Check(!Register(Def())&&NativeFactoryRegistry.Items.Count==0,"admission quota failure before factory staging");
Reset();MachineRecipeApi.Reject="catalog quota";Check(!Register(Def())&&InventoryAdmissionApi.Active==0,"recipe quota rejected before admission");
Reset();MachineRecipeApi.CanEdit=false;Check(!Register(Def()),"registration during batch rejected");
Reset();bool observed=false,coherent=false,appended=false;
ContentApi.Observer=v=>{
 observed=true;coherent=MachineRegistrationApi.Snapshot().Count==1&&MachineRecipeApi.Catalog[v.ContentId].Count==1&&MachineRegistrationApi.GuideSnapshot().Recipes.Count==1;
 appended=MachineRegistrationApi.RegisterAdditionalRecipes("addon.mod",v.ContentId,new[]{Recipe("addon.mod")}).Accepted;
};
Check(Register(Def()),"unified declaration accepted");Check(observed&&coherent,"publication observes coherent three catalogs");Check(appended,"publication can append reentrantly");
Check(InventoryAdmissionApi.Active==1,"one automatic admission lease per machine");
var original=MachineRegistrationApi.RegisteredRecipes("test.mod.machine");
Check(original.Select(r=>r.OwnerId).SequenceEqual(new[]{"test.mod","addon.mod"}),"merged recipes retain owner and append order");
Check(!Register(Def()),"duplicate machine rejected");
Check(!MachineRegistrationApi.RegisterAdditionalRecipes("addon.mod","missing",new[]{Recipe("addon.mod")}).Accepted,"missing target rejected");
Check(!MachineRegistrationApi.RegisterAdditionalRecipes("addon.mod","test.mod.machine",Array.Empty<MachineRecipe>()).Accepted,"empty append rejected");
Check(!MachineRegistrationApi.RegisterAdditionalRecipes("addon.mod","test.mod.machine",new[]{Recipe("addon.mod")}).Accepted,"duplicate addon rejected");
Check(!MachineRegistrationApi.RegisterAdditionalRecipes("addon.mod","test.mod.machine",new[]{Recipe("addon.mod","valid"),Recipe("addon.mod","bad") with {LiquidInputs=new[]{new MachineLiquidIngredient("absent",1)}}}).Accepted,"whole addon batch rejected if last recipe invalid");
Check(MachineRegistrationApi.RegisteredRecipes("test.mod.machine").Count==2,"bad addon keeps earlier catalog unchanged");
MachineRecipeApi.CanEdit=false;Check(!MachineRegistrationApi.RegisterAdditionalRecipes("third.mod","test.mod.machine",new[]{Recipe("third.mod")}).Accepted,"append during processing rejected");MachineRecipeApi.CanEdit=true;
Check(RecipeGuideApi.RegisterRecipes("test.mod",new[]{new RecipeGuideEntry("test.mod","test.mod.hand","NATIVE_MACHINE","手写","synthesis",Array.Empty<RecipeGuideFigure>(),new("NATIVE_B",1))}).Accepted,"manual guide coexists");
Check(MachineRegistrationApi.GuideSnapshot().Recipes.Count==2&&RecipeGuideApi.Snapshot().Recipes.Count==3,"automatic snapshot excludes manual partition");
Check(!RecipeGuideApi.RegisterRecipes("test.mod",new[]{new RecipeGuideEntry("test.mod","test.mod.recipe","NATIVE_MACHINE","冲突","synthesis",Array.Empty<RecipeGuideFigure>(),new("NATIVE_B",1))}).Accepted,"manual cannot overwrite automatic recipe ID");
Check(RecipeGuideApi.Snapshot().Recipes.Count==3,"failed manual replacement retains previous manual entry");
Check(RecipeGuideApi.RegisterRecipes("test.mod",Array.Empty<RecipeGuideEntry>()).Accepted&&MachineRegistrationApi.GuideSnapshot().Recipes.Count==2,"empty manual replacement preserves automatic entries");
var oldFactory=NativeFactoryRegistry.Items["test.mod.machine"].Factory;var oldAdmission=InventoryAdmissionApi.Predicates.Last();
var snapshot=MachineRegistrationApi.Snapshot();Check(snapshot[0].Template!=MachineRegistrationApi.Snapshot()[0].Template,"machine template snapshot copied");
var instance=oldFactory();Check(instance.contentWindow is not null&&instance.identifier=="test.mod.machine","factory invokes layout builder boundary");
Check(!oldAdmission(new(){identifier="WRONG"},new()),"automatic admission rejects unrelated item");Check(oldAdmission(new(){identifier="NATIVE_A"},new()),"automatic admission matches merged inputs");
MachineRegistrationApi.Stop();Check(InventoryAdmissionApi.Active==0&&MachineRecipeApi.Catalog.Count==0&&RecipeGuideApi.Snapshot().Recipes.Count==0,"stop releases admission and both managed catalogs");MachineRegistrationApi.Start();
bool stale=false;try{oldFactory();}catch(InvalidOperationException){stale=true;}Check(stale,"old factory cannot create after restart");Check(oldAdmission(new(),new()),"old admission inert after restart");
Reset();var dynamicCalls=0;
var dynamicRecipe=Recipe() with {
 ItemInputs=new[]{new MachineIngredient(null){ItemTag="FOOD",Guide=new("ICON_A",Requirement:"食品",Quantity:"整堆")}},
 LiquidInputs=new[]{new MachineLiquidIngredient("water",0,ResolveMillilitres:_=>{dynamicCalls++;return 1;}){Guide=new("BOTTLE",Name:"原料水")}},
 Output=new("NATIVE_B",ResolveCount:_=>{dynamicCalls++;return 2;}){ResolveItemId=_=>{dynamicCalls++;return "OTHER";},Guide=new(Name:"动态产物")},
 Guide=new("recycling","配方覆盖")
};
Check(Register(Def() with {Template=new(){LiquidInputs=new[]{new MachineContainerSlot{SlotId="water"}}},Recipes=new[]{dynamicRecipe}}),"dynamic descriptions and explicit illustrations accepted");
var guide=MachineRegistrationApi.GuideSnapshot().Recipes.Single();Check(dynamicCalls==0,"automatic guide never invokes dynamic resolver");
Check(guide.CategoryId=="recycling"&&guide.Note=="配方覆盖"&&guide.MachineName=="测试机器","recipe guide options override machine defaults");
Check(guide.Inputs[0].ItemId=="ICON_A"&&guide.Inputs[0].Quantity=="整堆"&&guide.Inputs[0].Requirement=="食品","tag illustration and display overrides retained");
Check(guide.Inputs[1].Amount==1&&guide.Inputs[1].Quantity!.Contains("动态"),"zero dynamic liquid fallback is descriptive");
Check(guide.Output.Name=="动态产物"&&guide.Output.Requirement!.Contains("动态")&&guide.Output.Quantity!.Contains("动态"),"dynamic output describes quantity and identity");
Reset();Check(Register(Def() with {Guide=null,Recipes=new[]{Recipe() with {ItemInputs=new[]{new MachineIngredient(null){ItemTag="FOOD"}}}}}),"disable auto guide permits nonvisual tag recipe");Check(MachineRegistrationApi.GuideSnapshot().Recipes.Count==0,"disabled guide adds no entries");
Reset();Check(Register(Def()),"fresh machine for quotas");var batch=Enumerable.Range(1,63).Select(i=>Recipe("addon.mod","r"+i)).ToArray();Check(MachineRegistrationApi.RegisterAdditionalRecipes("addon.mod","test.mod.machine",batch).Accepted,"64 recipes aggregate boundary accepted");Check(!MachineRegistrationApi.RegisterAdditionalRecipes("third.mod","test.mod.machine",new[]{Recipe("third.mod")}).Accepted,"65 aggregate recipes rejected");
var wrongThread=Task.Run(()=>{try{MachineRegistrationApi.Snapshot();return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();Check(wrongThread,"wrong thread blocked");
Reset();Func<GameItem,bool> outputCondition=_=>true;Func<GameItem,bool> inputCondition=_=>false;
var conditional=Def() with {Template=new(){Output=new(){Kind="container",ContainerCondition=outputCondition},LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Condition=inputCondition}}},Recipes=new[]{Recipe() with {Output=new MachineContainerOutput(new[]{new MachineLiquidAmount("NATIVE_WATER",1)})}}};
Check(Register(conditional),"unified machine registers input and output conditions");
Check(MachineContainerConditionApi.Active==1&&MachineContainerConditionApi.Last!.Output.ContainerCondition==outputCondition&&MachineContainerConditionApi.Last.LiquidInputs[0].Condition==inputCondition,"condition installer receives copied real delegates");
Check(MachineRegistrationApi.Snapshot().Single().Template.Output.ContainerCondition==outputCondition,"definition snapshot preserves C# callback contract");
Reset();Check(MachineContainerConditionApi.Active==0,"stopping unified registration releases its condition handles");
MachineContainerConditionApi.Reject=true;Check(!Register(Def())&&InventoryAdmissionApi.Active==0&&NativeFactoryRegistry.Items.Count==0,"condition installation failure releases admission before factory staging");
Reset();NativeFactoryRegistry.Reject=true;Check(!Register(Def())&&MachineContainerConditionApi.Active==0,"factory staging failure releases condition handle");
foreach(var mode in new[]{"id","stack","window","attached"}){
 Reset();GameItem? configured=null;
 var definition=Def() with {ConfigureItem=item=>{configured=item;if(mode=="id")item.identifier="foreign.item";if(mode=="stack")item.unitCount=2;if(mode=="window")item.contentWindow=new();if(mode=="attached")item.parentInventory=new();}};
 Check(Register(definition),"configure mutation is not executed during declaration: "+mode);
 var rejected=false;try{NativeFactoryRegistry.Items[definition.MachineId].Factory();}catch(InvalidOperationException e){rejected=e.Message.StartsWith("EZAPI_MACHINE_CONFIGURE");}
 Check(rejected&&configured is not null&&configured.Destroyed==(mode!="attached"),"configure mutation rejected before layout; attached object preserved: "+mode);
}

Reset();
var typedRecipe=Recipe() with{Guide=new("synthesis"){LocalizedNote=new("配方说明","Recipe notes")},ItemInputs=new[]{new MachineIngredient("input"){WholeStack=true,ItemTag="METAL",Guide=new(){LocalizedName=new("材料","Material")}}},Output=new("output",ResolveCount:_=>2){ResolveItemId=_=>"output"}};
var typedDefinition=Def() with{Recipes=new[]{typedRecipe},ItemOptions=new("原名",LocalizedName:new("机器","Machine"))};
var typedRows=MachineAutomaticGuide.Build(typedDefinition,new[]{new RegisteredMachineRecipe("test.mod",typedRecipe)});
var typedEn=RecipeGuideRules.ResolveLocale(typedRows[0],"en");var typedZh=RecipeGuideRules.ResolveLocale(typedRows[0],"zh");
Check(typedEn.MachineName=="Machine"&&typedZh.MachineName=="机器","automatic guide preserves machine localized name");
Check(typedEn.Note=="Recipe notes"&&typedZh.Note=="配方说明","recipe-specific bilingual note reaches automatic guide");
Check(typedEn.Inputs[0].Name=="Material"&&typedZh.Inputs[0].Name=="材料","automatic figure bilingual name preserved");
Check(typedEn.Inputs[0].Quantity!.StartsWith("Whole stacks")&&typedEn.Inputs[0].Requirement=="Tag: METAL","framework-generated whole stack/tag explanation bilingual");
Check(typedEn.Output.Quantity!.StartsWith("Dynamic quantity")&&typedEn.Output.Requirement!.StartsWith("Dynamic product"),"dynamic output explanation translated without running resolver");

// 全字段显示覆盖必须只影响图鉴；不可运行真实配方的条件/动态用量/动态产出委托。
var guideCallbacks=0;
var fullDisplay=new RecipeGuideDisplay("test.mod.picture","固定名","固定条件","固定用量"){
 LocalizedName=new("展示名","Display name"),LocalizedRequirement=new("展示条件","Display requirement"),LocalizedQuantity=new("展示用量","Display quantity")};
var fullRecipe=Recipe() with {ItemInputs=new[]{new MachineIngredient("NATIVE_A",7,_=>{guideCallbacks++;return true;}){Guide=fullDisplay}},
 Output=new("NATIVE_B",3,_=>{guideCallbacks++;return 4;}){Guide=fullDisplay,ResolveItemId=_=>{guideCallbacks++;return "NATIVE_B";}},Guide=null};
var fullDefinition=Def() with {Guide=new("recycling","固定说明"){LocalizedNote=new("机器说明","Machine notes")},Recipes=new[]{fullRecipe},ItemOptions=new("原机名",LocalizedName:new("机器名","Machine name"))};
var fullRows=MachineAutomaticGuide.Build(fullDefinition,new[]{new RegisteredMachineRecipe("test.mod",fullRecipe)});
foreach(var locale in new[]{"zh","en"}){
 var row=RecipeGuideRules.ResolveLocale(fullRows.Single(),locale);var zh=locale=="zh";
 Check(row.MachineName==(zh?"机器名":"Machine name")&&row.Note==(zh?"机器说明":"Machine notes")&&row.CategoryId=="recycling","automatic guide inherits machine options and typed text: "+locale);
 Check(row.Inputs[0].ItemId=="test.mod.picture"&&row.Output.ItemId=="test.mod.picture"&&row.Inputs[0].Amount==7&&row.Output.Amount==3,"display illustration overrides both images while retaining declared amounts: "+locale);
 Check(row.Inputs[0].Name==(zh?"展示名":"Display name")&&row.Output.Name==row.Inputs[0].Name,"display name overrides input and output: "+locale);
 Check(row.Inputs[0].Requirement==(zh?"展示条件":"Display requirement")&&row.Output.Requirement==row.Inputs[0].Requirement,"display requirement overrides inferred explanation: "+locale);
 Check(row.Inputs[0].Quantity==(zh?"展示用量":"Display quantity")&&row.Output.Quantity==row.Inputs[0].Quantity,"display quantity overrides inferred explanation: "+locale);
}
Check(guideCallbacks==0&&fullRecipe.Output.ItemId=="NATIVE_B"&&fullRecipe.ItemInputs[0].ItemId=="NATIVE_A","guide generation never executes callbacks or mutates actual item IDs");
var recipeOverride=fullRecipe with {Guide=new("custom","配方说明")};
var overrideRow=MachineAutomaticGuide.Build(fullDefinition,new[]{new RegisteredMachineRecipe("test.mod",recipeOverride)}).Single();
Check(overrideRow.CategoryId=="custom"&&RecipeGuideRules.ResolveLocale(overrideRow,"en").Note=="配方说明","recipe guide replaces machine options atomically; fixed text stays literal");
Check(MachineAutomaticGuide.Build(fullDefinition with{Guide=null},new[]{new RegisteredMachineRecipe("test.mod",fullRecipe)}).Count==0,"omitted guide options suppress automatic rows");

// 机器视图是调用时的状态快照；Applied、同步加工门控、全局运行时和单机夜间配置分别检查。
foreach(var status in Enum.GetValues<NativeApplicationStatus>())
foreach(var runtime in new[]{false,true})
foreach(var nightly in new[]{false,true}){
 Reset();ItemApi.Status=status;MachineRuntimeApi.RuntimeInstalled=runtime;InventoryFreezeApi.IsAvailable=true;InventoryAdmissionApi.IsAvailable=true;
 Check(Register(Def() with{EnableNightly=nightly}),"view matrix registration "+status+"/"+runtime+"/"+nightly);
 var row=MachineRegistrationApi.Snapshot().Single();
 Check(row.Status==status&&row.ProcessingAvailable==(status==NativeApplicationStatus.Applied)&&row.RuntimeInstalled==runtime&&row.NightlyInstalled==(runtime&&nightly),"independent view states "+status+"/"+runtime+"/"+nightly);
 MachineRuntimeApi.RuntimeInstalled=!runtime;
 Check(row.RuntimeInstalled==runtime&&MachineRegistrationApi.Snapshot().Single().RuntimeInstalled==!runtime,"captured runtime state changes only on a new snapshot");
}
foreach(var mode in new[]{"admission","freeze","processing"}){
 Reset();ItemApi.Status=NativeApplicationStatus.Applied;InventoryFreezeApi.IsAvailable=true;InventoryAdmissionApi.IsAvailable=true;MachineRuntimeApi.RuntimeInstalled=true;
 Check(Register(Def()),"readiness fixture registered: "+mode);
 if(mode=="admission")InventoryAdmissionApi.IsAvailable=false;
 if(mode=="freeze")InventoryFreezeApi.IsAvailable=false;
 if(mode=="processing")MachineRecipeApi.CanEdit=false;
 Check(!MachineRegistrationApi.Snapshot().Single().ProcessingAvailable,"manual processing closes when required gate unavailable: "+mode);
}
Reset();ItemApi.Status=NativeApplicationStatus.Applied;InventoryFreezeApi.IsAvailable=true;InventoryAdmissionApi.IsAvailable=true;
ItemApi.Created=9;ItemApi.Failures=2;ItemApi.LastError="fixture factory fault";
Check(Register(Def()),"factory counters fixture registered");
var counters=MachineRegistrationApi.Snapshot().Single();
Check(counters.Created==9&&counters.Failures==2&&counters.LastError=="fixture factory fault","view preserves actual factory success/failure diagnostics");
ItemApi.Created=10;ItemApi.LastError=null;
Check(counters.Created==9&&counters.LastError=="fixture factory fault"&&MachineRegistrationApi.Snapshot().Single().Created==10,"factory diagnostic view is immutable snapshot");
var legacy=new MachineView(counters.OwnerId,counters.MachineId,counters.Template,counters.RecipeCount,counters.Status,counters.ProcessingAvailable,counters.NightlyInstalled,counters.Created,counters.Failures,counters.LastError);
var (legacyOwner,legacyId,legacyTemplate,legacyCount,legacyStatus,legacyProcessing,legacyNightly,legacyCreated,legacyFailures,legacyError)=legacy;
Check(!legacy.RuntimeInstalled&&legacyOwner==counters.OwnerId&&legacyId==counters.MachineId&&legacyCount==counters.RecipeCount,"legacy constructor and ten-field deconstruction preserved");
Check((legacy with{RuntimeInstalled=true}).RuntimeInstalled&&!legacy.RuntimeInstalled,"new property supports independent record copy");
Check(System.Text.Json.JsonSerializer.Serialize(legacy with{RuntimeInstalled=true}).Contains("\"RuntimeInstalled\":true"),"output JSON exposes captured RuntimeInstalled flag");
foreach(var operation in new Action[]{()=>MachineRegistrationApi.Snapshot(),()=>MachineRegistrationApi.Recipes("test.mod.machine"),()=>MachineRegistrationApi.RegisteredRecipes("test.mod.machine"),()=>MachineRegistrationApi.GuideSnapshot()}){
 bool rejected=false;try{Task.Run(operation).GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
 Check(rejected,"public registry snapshots reject worker thread");
}
ItemApi.Status=NativeApplicationStatus.Staged;ItemApi.Created=ItemApi.Failures=0;ItemApi.LastError=null;InventoryAdmissionApi.IsAvailable=InventoryFreezeApi.IsAvailable=true;MachineRuntimeApi.RuntimeInstalled=false;
Reset();Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=checks.Count-failed,failed,cases=checks,nativeRuntimeVerified=false,scope="real unified registration/automatic guide/partition contracts; native factory, admission and processor are explicit offline stubs"}));if(failed>0)Environment.Exit(1);

using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>一次验证统一声明/多作者追加/图鉴隔离/真实加工。只有托管报告保留；所有原生实例临时创建后销毁。</summary>
internal static class DebugMachineRegistration
{
    private const string Owner="ezapi.debug",Id=Owner+".registered_machine";
    private static readonly List<(bool Passed,string Name)> Initialization=new();
    private static bool _reentrantBlocked;
    internal static object? LastReport {get;private set;}
    internal static void Clear(){LastReport=null;Initialization.Clear();}
    private static MachineRecipe Base()=>new(Owner+".registered_base",new[]{new MachineIngredient(Owner+".factory_item")},new(Owner+".factory_item")) {
        MachineCondition=_=>{
            var result=MachineRegistrationApi.RegisterAdditionalRecipes("ezapi.blocked",Id,new[]{new MachineRecipe("ezapi.blocked.never",Array.Empty<MachineIngredient>(),new(Owner+".factory_item"))});
            _reentrantBlocked=!result.Accepted&&result.Reason?.Contains("EZAPI_MACHINE_NOT_READY",StringComparison.Ordinal)==true;return true;
        }
    };
    private static MachineRecipe Added()=>new("ezapi.addon.registered_added",new[]{new MachineIngredient(Owner+".factory_amenity")},new(Owner+".factory_item")) {
        Guide=new("recycling","追加作者的说明"),
    };
    private static MachineRecipe Runtime()=>new("ezapi.runtime.registered_runtime",new[]{new MachineIngredient(Owner+".factory_node"){Guide=new(Name:"原生节点材料")}},new(Owner+".factory_item")) {
        Guide=new("recycling","游戏载入后追加，准入与图鉴立即更新。")
    };
    private static MachineDefinition Definition(string id)=>new(id,new(){ItemInput=new(3,3),Output=new(){Size=new(3,3)}},new[]{Base()}) {
        Power=new(0),ItemOptions=new("EZapi统一注册检查机","临时机器；不保存、不自动夜间加工。"),
        ConfigureItem=item=>item.SetValue(123),Guide=new("synthesis","基础机器说明")
    };
    internal static void Register()
    {
        void Check(bool ok,string name){Initialization.Add((ok,name));if(!ok)throw new InvalidOperationException("registration initialization: "+name);}
        var before=ItemApi.FactorySnapshot().Count;
        var bad=Definition(Owner+".rejected_machine") with {Recipes=new[]{Base() with {LiquidInputs=new[]{new MachineLiquidIngredient("absent",1)}}}};
        Check(!MachineRegistrationApi.RegisterMachine(Owner,bad).Accepted,"invalid slot rejects entire machine before factory staging");
        Check(ItemApi.FactorySnapshot().Count==before&&!MachineRegistrationApi.Contains(bad.MachineId),"rejected declaration leaves no factory/catalog");
        bool observed=false,coherent=false,appended=false;
        using(var sub=ContentApi.Subscribe(Owner,"registered-machine-observer",view=>{
            if(view.ContentId!=Id||view.Status!=NativeApplicationStatus.Staged)return;
            observed=true;coherent=MachineRegistrationApi.Snapshot().Single(v=>v.MachineId==Id).RecipeCount==1&&
                MachineRecipeApi.Snapshot().Recipes.Any(r=>r.MachineId==Id)&&MachineRegistrationApi.GuideSnapshot().Recipes.Any(r=>r.MachineId==Id);
            appended=MachineRegistrationApi.RegisterAdditionalRecipes("ezapi.addon",Id,new[]{Added()}).Accepted;
        }))Check(MachineRegistrationApi.RegisterMachine(Owner,Definition(Id)).Accepted,"unified registration accepted");
        Check(observed&&coherent,"content callback observes factory, recipe and guide already committed");
        Check(appended&&MachineRegistrationApi.RegisteredRecipes(Id).Count==2,"reentrant content callback can append after coherent commit");
        Check(!MachineRegistrationApi.RegisterMachine(Owner,Definition(Id)).Accepted,"duplicate machine ID rejected");
        Check(!MachineRecipeApi.RegisterRecipes(Owner,Id,new[]{Base()}).Accepted,"low-level replacement cannot erase managed/addon catalog");
        var expected=MachineRegistrationApi.RegisteredRecipes(Id).Count;
        Check(!MachineRegistrationApi.RegisterAdditionalRecipes("ezapi.addon",Id,new[]{Added()}).Accepted,"duplicate addon batch rejected");
        Check(!MachineRegistrationApi.RegisterAdditionalRecipes("ezapi.addon",Id,new[]{Added() with {RecipeId="ezapi.addon.invalid",LiquidInputs=new[]{new MachineLiquidIngredient("missing",1)}}}).Accepted,"addon missing liquid slot rejected");
        Check(MachineRegistrationApi.RegisteredRecipes(Id).Count==expected,"rejected addon leaves original merged recipes intact");
        var guideBefore=MachineRegistrationApi.GuideSnapshot().Recipes.Count;
        Check(!RecipeGuideApi.RegisterRecipes(Owner,new[]{new RecipeGuideEntry(Owner,Base().RecipeId,Id,"冲突","synthesis",Array.Empty<RecipeGuideFigure>(),new(Owner+".factory_item",1))}).Accepted,"manual guide cannot collide with automatic guide");
        Check(MachineRegistrationApi.GuideSnapshot().Recipes.Count==guideBefore,"guide collision leaves automatic partition intact");
    }
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.IsReady)return "请进入测试档店铺并放下拖拽物品。";
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        foreach(var c in Initialization)Check(c.Passed,c.Name);
        var made=new List<GameItem>();var destroyed=new HashSet<IntPtr>();
        try {
            var view=MachineRegistrationApi.Snapshot().Single(v=>v.MachineId==Id);
            Check(view.Status==NativeApplicationStatus.Applied&&view.ProcessingAvailable&&view.NightlyInstalled,"native directory applied; sync processing available; nightly installed");
            Check(view.RuntimeInstalled&&view.RuntimeInstalled==MachineRuntimeApi.RuntimeInstalled,"machine view captures actual runtime readiness");
            var disabled=MachineRegistrationApi.Snapshot().Single(v=>v.MachineId==Owner+".night_disabled");
            Check(disabled.RuntimeInstalled&&disabled.ProcessingAvailable&&!disabled.NightlyInstalled,"disabled nightly machine retains runtime and manual processing readiness");
            Check(MachineRegistrationApi.Snapshot().All(v=>v.RuntimeInstalled==view.RuntimeInstalled),"all definitions capture same global runtime readiness independently of per-machine night flag");
            var legacy=new MachineView(view.OwnerId,view.MachineId,view.Template,view.RecipeCount,view.Status,view.ProcessingAvailable,view.NightlyInstalled,view.Created,view.Failures,view.LastError);
            Check(!legacy.RuntimeInstalled&&(legacy with{RuntimeInstalled=true}).RuntimeInstalled,"old positional view constructor remains valid with explicit additive runtime state");
            var m=ItemApi.Create(Id,true);made.Add(m);var other=ItemApi.Create(Id,false);made.Add(other);
            Check(m.Pointer!=other.Pointer&&m.contentWindow.Pointer!=other.contentWindow.Pointer,"registered factory creates independent instances/windows");
            Check(m.name=="EZapi统一注册检查机"&&m.unitValue==123,"configured value and presentation applied");
            Check(MachineLayoutApi.TryGetInventory(m,out var slots)&&MachineLayoutApi.TryGetInventory(other,out var second)&&slots.ItemInput.Pointer!=second.ItemInput.Pointer,"registered layouts retain independent inventories");
            var grid=slots.ItemInput.TryCast<GameGridInventory>()!;
            var unrelated=ItemApi.Create("ezapi.sample.water_container",true);made.Add(unrelated);
            Check(!grid.MayHaveValidInventorySlot(unrelated),"automatic merged admission refuses unrelated item");
            var runtimeInput=ItemApi.Create(Owner+".factory_node",true);made.Add(runtimeInput);
            var hadRuntime=MachineRegistrationApi.Recipes(Id).Any(r=>r.RecipeId==Runtime().RecipeId);
            Check(hadRuntime||!grid.MayHaveValidInventorySlot(runtimeInput),"runtime ingredient refused before addon registration");
            Check(hadRuntime||MachineRegistrationApi.RegisterAdditionalRecipes("ezapi.runtime",Id,new[]{Runtime()}).Accepted,"runtime addon batch accepted on idle main thread");
            Check(grid.MayHaveValidInventorySlot(runtimeInput),"same native input grid immediately accepts new addon ingredient");
            foreach(var mode in new[]{"base","addon","runtime"}) {
                var item=mode=="runtime"?runtimeInput:ItemApi.Create(Owner+(mode=="addon"?".factory_amenity":".factory_item"),true);if(mode!="runtime")made.Add(item);var ptr=item.Pointer;
                var marker=grid.TryFindOneValidInventorySlot(item);
                Check(marker?.IsValid()==true&&marker.TryAcceptOnce(1)==1,mode+": native admission accepts ingredient");
                var result=MachineRecipeApi.ProcessBatch(m);
                if(result.Committed)destroyed.Add(ptr);
                details.Add(new{mode,result,outputs=slots.Output.childItems.Count});
                Check(result.Committed&&result.RecipeId==(mode=="addon"?Added().RecipeId:mode=="runtime"?Runtime().RecipeId:Base().RecipeId),mode+": actual registered recipe selected and committed");
                Check(slots.ItemInput.childItems.Count==0,mode+": real material consumed");
                Check(slots.Output.childItems.Count==(mode=="base"?1:mode=="addon"?2:3),mode+": real product delivered");
            }
            Check(_reentrantBlocked,"real processing callback cannot append into active recipe catalog");
            var recipes=MachineRecipeApi.Snapshot().Recipes.Where(r=>r.MachineId==Id).ToArray();
            Check(recipes.Length==3&&recipes[0].OwnerId==Owner&&recipes[1].OwnerId=="ezapi.addon"&&recipes[2].OwnerId=="ezapi.runtime","executable catalog preserves author and append priority");
            var guides=MachineRegistrationApi.GuideSnapshot().Recipes.Where(r=>r.MachineId==Id).ToArray();
            Check(guides.Length==3&&guides.Any(r=>r.OwnerId=="ezapi.addon"&&r.CategoryId=="recycling"&&r.Note=="追加作者的说明"),"automatic guide preserves addon owner/category/note");
            var hand=new RecipeGuideEntry(Owner,Owner+".manual_registration_probe",Id,"手写图鉴","synthesis",Array.Empty<RecipeGuideFigure>(),new(Owner+".factory_item",1));
            var automatic=MachineRegistrationApi.GuideSnapshot().Recipes;
            var manualBefore=RecipeGuideApi.Snapshot().Recipes.Where(r=>r.OwnerId==Owner&&!automatic.Any(g=>g.OwnerId==r.OwnerId&&g.RecipeId==r.RecipeId)).ToArray();
            try {
                Check(RecipeGuideApi.RegisterRecipes(Owner,new[]{hand}).Accepted,"manual owner partition can be replaced independently");
                Check(MachineRegistrationApi.GuideSnapshot().Recipes.Count(r=>r.MachineId==Id)==3,"manual replacement preserves automatic machine guides");
                Check(RecipeGuideApi.Snapshot().Recipes.Any(r=>r.RecipeId==hand.RecipeId),"manual and automatic entries coexist");
            }finally{var restored=RecipeGuideApi.RegisterRecipes(Owner,manualBefore);if(!restored.Accepted)throw new InvalidOperationException(restored.Reason);}
            var before=MachineRegistrationApi.RegisteredRecipes(Id).Count;
            Check(!MachineRegistrationApi.RegisterMachine(Owner,Definition(Owner+".late_machine")).Accepted,"late machine item registration rejected after native directory");
            Check(MachineRegistrationApi.RegisteredRecipes(Id).Count==before,"late rejection leaves catalog unchanged");
            var copied=MachineRegistrationApi.Snapshot().Single(v=>v.MachineId==Id).Template;
            Check(copied!=view.Template&&copied.ItemInput==view.Template.ItemInput,"machine snapshots copy template object and retain dimensions");
            Check(!InventoryFreezeApi.IsFrozen(m),"registered processing releases all freezes");
            DebugMachineBatteryRequirements.Run(Check,details);
            DebugMachineInventoryBinding.Run(Check,details);
            DebugMachineNightResources.Run(Check,details);
        }catch(Exception e){Check(false,"execution: "+e);MelonLogger.Error("[EZapi] Registration probe: "+e);}
        finally {
            foreach(var item in made.AsEnumerable().Reverse()) {
                if(destroyed.Contains(item.Pointer))continue;
                if(item.parentInventory is {} parent&&!parent.Expel(item))throw new InvalidOperationException("registration fixture expel");item.Destroy();
            }
        }
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details,scope="temporary native registration, merged author recipes, admission and automatic guide; no player delivery/save/nightly"};
        EZapiBootstrap.WriteDiagnostics();return $"机器统一注册检查：{passed}通过，{failed}失败，已导出。";
    }
}

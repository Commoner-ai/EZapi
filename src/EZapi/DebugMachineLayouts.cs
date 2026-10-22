using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>一次按钮检查多个临时工厂/槽位/图鉴契约；不交付玩家、不保存、不伪称生产成功。</summary>
internal static class DebugMachineLayouts
{
    private const string Owner="ezapi.debug";
    internal static object? LastReport{get;private set;}
    internal static void Clear()=>LastReport=null;
    private static ItemDefinition Definition(bool liquid)=>new(){Id=Owner+(liquid?".machine_liquid_probe":".machine_layout_probe"),Name=liquid?"EZapi液体槽检查机":"EZapi槽位检查机",Width=2,Height=2};
    private static MachineLayoutDefinition Layout(bool liquid)=>new(){Title="EZapi临时机器检查",LocalizedTitle=new("EZapi临时机器检查","EZapi temporary machine check"),ItemInput=new(3,3),
        Output=new(){Kind=liquid?"container":"items",Size=new(3,3),LocalizedLabel=new("产物容器","Output container")},
        Battery=new(new(2,2),Required:false),Modules=new(){Size=new(4,4),AllowedTypes=new[]{"ezapi.debug.probe_type"}},ManualSlot=true,
        LocalizedBatteryLabel=new("电池槽","Battery slot"),LocalizedManualLabel=new("说明书槽","Instructions slot"),
        LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Label="原料水容器",LocalizedLabel=new("原料水容器","Water input"),Size=new(3,3),AllowedIds=new[]{"ezapi.sample.water_container"}}}};
    internal static void Register()
    {
        foreach(var isLiquid in new[]{false,true}) {
            var d=Definition(isLiquid);LegacySpriteAdapter.Commit(d,LegacySpriteAdapter.Prepare(d,null));
            var captured=isLiquid;
            var result=ItemApi.RegisterItem(Owner,d.Id,()=>{
                var item=NativeItemFactory.Create(Definition(captured));
                try{MachineLayoutApi.Create(item,Layout(captured));MachineryHelper.InitMachinery(item,0,false);return item;}
                catch{item.Destroy();throw;}
            },new NativeItemOptions(d.Name,SourceName:"EZapi开发检查"));
            if(!result.Accepted)throw new InvalidOperationException(result.Reason);
        }
        var guide=RecipeGuideApi.RegisterRecipes(Owner,new[]{
            new RecipeGuideEntry(Owner,Owner+".000_image_probe",Definition(false).Id,"EZapi图片检查机","images",
                new[]{new RecipeGuideFigure("ezapi.sample.water_container",1,"原料水容器"){LocalizedName=new("原料水容器","Water container")},new RecipeGuideFigure("ezapi.sample.field_kit",2,"PNG工具包"){LocalizedName=new("PNG工具包","PNG field kit")}},
                new("ezapi.sample.field_kit",1,"PNG示意产物"){LocalizedName=new("PNG示意产物","Field kit product"),LocalizedRequirement=new("示意，无实际加工","Display only; no production"),LocalizedQuantity=new("1份","1 unit")},"图片按ItemId解析；同ID在多个卡片共用缓存。该检查配方只有说明，不执行生产。"){
                    LocalizedMachineName=new("EZapi图片检查机","EZapi image probe"),LocalizedNote=new("图片和文字都随游戏语言显示；该条目只作展示。","Images and text follow the game language. This entry is display only.")},
            new RecipeGuideEntry(Owner,Owner+".layout_example",Definition(false).Id,"EZapi槽位检查机","synthesis",
                new[]{new RecipeGuideFigure("ezapi.sample.water_container",1,"原料水容器","必须是液体容器，并符合输入槽过滤条件。","1 个容器")},
                new(Owner+".factory_item",1,"示意产物"),"这是图鉴展示示例。当前阶段仅验证机器布局与图鉴，不执行这条加工配方。"),
            new RecipeGuideEntry(Owner,Owner+".paging_example",Definition(true).Id,"EZapi液体槽检查机","liquid",
                Enumerable.Range(1,6).Select(i=>new RecipeGuideFigure(Owner+".material_"+i,i,"示意材料 "+i)).ToArray(),
                new("ezapi.sample.water_container",1,"产物容器"),"用于检查材料分页、长文字换行、配方切换和窗口关闭。\n图鉴登记不会让物品获得生产能力。")});
        if(!guide.Accepted)throw new InvalidOperationException(guide.Reason);
    }
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive)return "请先放下拖拽物品。";
        if(!VisibleStoreInventory.IsReady)return "请进入测试档店铺。";
        var checks=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        var locale=NativeItemLocalization.CurrentLocaleCode;var english=!locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
        var created=new List<GameItem>();
        try {
            NativeAccess.RequireRun();
            foreach(var liquid in new[]{false,true}) {
                var id=Definition(liquid).Id;var machine=ItemApi.Create(id,false);created.Add(machine);
                var second=ItemApi.Create(id,false);created.Add(second);
                Check(MachineLayoutApi.TryGetInventory(machine,out var slots),id+": complete native layout found");
                Check(MachineLayoutApi.TryGetInventory(second,out var other),id+": second native layout found");
                Check(machine.contentWindow.titleString==(english?"EZapi temporary machine check":"EZapi临时机器检查"),id+": current-locale native window title");
                Check(slots.LiquidInputs["water"].tooltipName==(english?"Water input":"原料水容器"),id+": current-locale liquid slot tooltip");
                Check(slots.Battery!.tooltipName==(english?"Battery slot":"电池槽")&&slots.Manual!.tooltipName==(english?"Instructions slot":"说明书槽"),id+": current-locale fixed slot tooltips");
                if(liquid)Check(slots.Output.Cast<GameSlotInventory>().tooltipName==(english?"Output container":"产物容器"),id+": current-locale output tooltip");
                Check(machine.contentWindow.Pointer!=second.contentWindow.Pointer&&slots.ItemInput.Pointer!=other.ItemInput.Pointer&&slots.Output.Pointer!=other.Output.Pointer,id+": independent windows and inventories");
                Check(MachineHelper.GetBatterySlot(machine)?.Pointer==slots.Battery?.Pointer,id+": official battery route");
                Check(MachineHelper.GetModuleInv(machine)?.Pointer==slots.Modules?.Pointer,id+": official module route");
                Check(slots.LiquidInputs.Count==1&&slots.Manual is not null,id+": liquid and manual slots present");
                GeneralHelper.SetItemOwned(machine,true);
                var ordinary=ItemApi.Create(Owner+".factory_item",true);created.Add(ordinary);
                var water=ItemApi.Create("ezapi.sample.water_container",true);created.Add(water);
                var module=ItemApi.Create(Owner+".json_module",true);created.Add(module);
                var waterSlot=slots.LiquidInputs["water"];
                Check(waterSlot.TryFindOneValidInventorySlot(ordinary)?.IsValid()!=true,id+": ordinary item refused by liquid slot");
                var wrongWater=ItemDirectory.CreateEmptyItem(Owner+".wrong_water_probe");created.Add(wrongWater);
                wrongWater.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());WaterHelper.InitLiquidContainerItem(wrongWater,100,0,false,false,false,false,3);GeneralHelper.SetItemOwned(wrongWater,true);
                Check(waterSlot.TryFindOneValidInventorySlot(wrongWater)?.IsValid()!=true,id+": liquid container with wrong ID rejected by filter");
                Check(slots.Battery!.TryFindOneValidInventorySlot(ordinary)?.IsValid()!=true,id+": ordinary item refused by official battery slot");
                Check(slots.Manual!.TryFindOneValidInventorySlot(ordinary)?.IsValid()!=true,id+": ordinary item refused by official manual slot");
                var input=slots.ItemInput.TryFindOneValidInventorySlot(ordinary);Check(input?.IsValid()==true&&input.TryAcceptOnce(1)==1,id+": input receives native item");
                var liquidMarker=waterSlot.TryFindOneValidInventorySlot(water);Check(liquidMarker?.IsValid()==true&&liquidMarker.TryAcceptOnce(1)==1,id+": filtered liquid input receives matching container");
                var moduleMarker=slots.Modules!.TryFindOneValidInventorySlot(module);Check(moduleMarker?.IsValid()==true&&moduleMarker.TryAcceptOnce(1)==1,id+": official module bay accepts configured type");
                Check(SaveGraphApi.Items(machine).Count==4,id+": complete storage graph contains all three inserted items");
                var json=SaveGraphApi.Encode(machine);Check(json.Contains(id,StringComparison.Ordinal)&&json.Contains("ezapi.sample.water_container",StringComparison.Ordinal),id+": native encoder includes machine and child container");
                Check(SaveGraphApi.Items(second).Count==1,id+": second machine remains empty");
                if(liquid)Check(slots.Output.TryFindOneValidInventorySlot(ordinary)?.IsValid()!=true,id+": container output rejects ordinary item");
                if(liquid) {
                    Check(waterSlot.Expel(water),id+": native liquid source expel");
                    var outputMarker=slots.Output.TryFindOneValidInventorySlot(water);
                    Check(outputMarker?.IsValid()==true&&outputMarker.TryAcceptOnce(1)==1,id+": container output receives actual liquid container");
                    Check(slots.Output.Expel(water),id+": native liquid output expel");
                }
                // 销毁前先逐件原生Expel；不依赖Destroy是否级联，也不留下游离测试物品。
                Check(slots.ItemInput.Expel(ordinary)&&(liquid||waterSlot.Expel(water))&&slots.Modules.Expel(module),id+": native expel clears all inserted temporary items");
                Check(SaveGraphApi.Items(machine).Count==1,id+": temporary machine graph cleared");
                Check(!MachineLayoutApi.TryGetInventory(ordinary,out _),id+": ordinary item not claimed as machine layout");
            }
            var minimal=ItemDirectory.CreateEmptyItem(Owner+".minimal_layout_probe");created.Add(minimal);
            minimal.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());MachineLayoutApi.Create(minimal,new());
            Check(MachineLayoutApi.TryGetInventory(minimal,out var sparse)&&sparse.Battery is null&&sparse.Modules is null&&sparse.Manual is null&&sparse.LiquidInputs.Count==0,"minimal layout marker survives strict JSON roundtrip with omitted optional fields");
            var before=RecipeGuideApi.Snapshot();
            var invalid=RecipeGuideApi.RegisterRecipes(Owner,new[]{new RecipeGuideEntry(Owner,"another.mod.recipe","x","无效配方","synthesis",Array.Empty<RecipeGuideFigure>(),new("x",1))});
            Check(!invalid.Accepted&&RecipeGuideApi.Snapshot().Revision==before.Revision&&RecipeGuideApi.Snapshot().Recipes.Count==before.Recipes.Count,"guide rejected batch leaves prior catalog and revision intact");
            Check(new[]{Owner+".layout_example",Owner+".paging_example"}.All(id=>before.Recipes.Any(r=>r.OwnerId==Owner&&r.RecipeId==id)),"guide contains both registered examples alongside other content");
            Check(!RecipeGuideApi.IsVisible,"layout check does not open guide UI");
        } catch(Exception e){failed++;checks.Add(new{name="execution",passed=false,error=e.ToString()});MelonLogger.Error("[EZapi] Machine layout probe: "+e);}
        finally {
            // 子件先销毁，机器后销毁；只清理本次明确创建的实例。
            foreach(var item in created.AsEnumerable().Reverse())try{if(item.Pointer!=IntPtr.Zero)item.Destroy();}catch(Exception e){failed++;checks.Add(new{name="cleanup",passed=false,error=e.Message});}
        }
        LastReport=new{at=DateTimeOffset.Now,locale,passed,failed,checks,scope="two custom directory factories, independent native layouts, bilingual native title/tooltips, official battery/module/manual admission, liquid filtering and native graph encoding; no player inventory delivery, production, save or decode"};
        EZapiBootstrap.WriteDiagnostics();return $"机器槽位检查：{passed}通过，{failed}失败，已导出。";
    }
}

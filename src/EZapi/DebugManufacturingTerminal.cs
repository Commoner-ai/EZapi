using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>显式按钮的临时原生回归和独立新档夹具。正常运行没有内置配方、发放或保存请求。</summary>
internal static class DebugManufacturingTerminal
{
    private const string Owner="ezapi.debug",Material=Owner+".factory_item",Addon="ezapi.addon";
    private static bool _reentrantBlocked;
    internal static object? LastReport{get;private set;}
    internal static void Clear()=>LastReport=null;
    private static void EnsureRecipes()
    {
        foreach(var owner in new[]{Owner,Addon}) {
            var id=owner+".terminal_value";
            if(MachineRegistrationApi.Recipes(ManufacturingTerminal.ItemId).Any(r=>r.RecipeId==id))continue;
            var result=MachineRegistrationApi.RegisterAdditionalRecipes(owner,ManufacturingTerminal.ItemId,new[]{
                new MachineRecipe(id,new[]{new MachineIngredient(owner==Owner?Material:Owner+".factory_amenity")},new(Material){
                    PrepareItem=(plan,item)=>ProductionValueApi.SetBasis(item,ProductionValueApi.Calculate(plan.Context,plan.Context.ProductionMarkupPercent))
                }) {MachineCondition=_=>{
                    _reentrantBlocked=!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup(Owner,99).Accepted;return true;
                }}
            });
            if(!result.Accepted)throw new InvalidOperationException(result.Reason);
        }
    }
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.IsReady)return "请进入测试档店铺并放下拖拽物品。";
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        var roots=new List<GameItem>();var loose=new List<GameItem>();
        try {
            var initial=ManufacturingTerminal.Snapshot();
            Check(initial.Registered&&initial.ProductionMarkupPercent==15,"shared terminal registered with default15 percent");
            var factory=MachineRegistrationApi.Snapshot().Single(v=>v.MachineId==ManufacturingTerminal.ItemId);
            Check(factory.Status==NativeApplicationStatus.Applied&&factory.ProcessingAvailable&&factory.NightlyInstalled,"terminal native factory, processing and nightly available");
            foreach(var owner in new[]{"",Owner+"\n","foreign.owner "})Check(!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup(owner,15).Accepted,"invalid owner rejected: "+owner.Trim());
            foreach(var percent in new[]{-1,1001,int.MaxValue})Check(!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup(Owner,percent).Accepted,"invalid percent rejected: "+percent);
            Check(ManufacturingTerminal.Snapshot()==initial,"invalid requests leave owner and configuration unchanged");
            EnsureRecipes();var recipes=MachineRegistrationApi.RegisteredRecipes(ManufacturingTerminal.ItemId);
            Check(recipes.Count==2&&recipes[0].OwnerId==Owner&&recipes[1].OwnerId==Addon,"two content authors append to one shared terminal");
            Check(!MachineRegistrationApi.RegisterAdditionalRecipes(Owner,ManufacturingTerminal.ItemId,new[]{recipes[0].Recipe}).Accepted,"duplicate contribution rejected atomically");
            Check(MachineRegistrationApi.GuideSnapshot().Recipes.Count(r=>r.MachineId==ManufacturingTerminal.ItemId)==2,"shared terminal automatically lists both authors in guide");
            Check(MachineRegistrationApi.ConfigureManufacturingTerminalMarkup(Owner,15).Accepted,"first valid default-value configuration accepted");
            Check(ManufacturingTerminal.Snapshot().MarkupOwner==Owner,"default-value submission still claims configuration ownership");
            Check(!MachineRegistrationApi.ConfigureManufacturingTerminalMarkup(Addon,15).Accepted,"second author cannot override even identical markup");
            var m=ItemApi.Create(ManufacturingTerminal.ItemId,true);roots.Add(m);
            var second=ItemApi.Create(ManufacturingTerminal.ItemId,true);roots.Add(second);
            MachineLayoutApi.TryGetInventory(m,out var firstSlots);MachineLayoutApi.TryGetInventory(second,out var secondSlots);
            Check(m.Pointer!=second.Pointer&&m.contentWindow.Pointer!=second.contentWindow.Pointer&&firstSlots.ItemInput.Pointer!=secondSlots.ItemInput.Pointer&&firstSlots.Output.Pointer!=secondSlots.Output.Pointer,"native windows/input/output isolated per instance");
            Check(m.shape.width==3&&m.shape.height==3&&m.unitValue==400&&m.IsGameItemType("MACHINE")&&!m.IsGameItemType("MODULE"),"terminal shape3x3, value400 and native machine type");
            Check(firstSlots.ItemInput.TryCast<GameGridInventory>()?.inventoryShape.globalWidth==9&&firstSlots.ItemInput.TryCast<GameGridInventory>()?.inventoryShape.globalHeight==6&&firstSlots.Output.TryCast<GameGridInventory>()?.inventoryShape.globalWidth==9,"input/output9x6");
            Check(firstSlots.Modules?.TryCast<GameGridInventory>()?.inventoryShape.globalWidth==7&&firstSlots.Modules?.TryCast<GameGridInventory>()?.inventoryShape.globalHeight==5,"module bay7x5");
            Check(Power.GetMachineCost(m)==25,"native base batch cost25");
            foreach(var mode in new[]{"default","zero","changed","upper","addon","efficiency","empty","insufficient","full"}) {
                var markup=mode=="zero"?0:mode=="changed"?25:mode=="upper"?1000:15;
                Check(MachineRegistrationApi.ConfigureManufacturingTerminalMarkup(Owner,markup).Accepted,mode+": owner updates markup");
                var machine=ItemApi.Create(ManufacturingTerminal.ItemId,true);roots.Add(machine);MachineLayoutApi.TryGetInventory(machine,out var slots);
                var battery=ItemApi.Create(Material,true);loose.Add(battery);PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,mode=="insufficient"?0:100);Put(slots.Battery!,battery);loose.Remove(battery);
                if(mode=="efficiency") {
                    var module=ItemApi.Create(Material,true);loose.Add(module);ModuleHelper.InitModuleItem(module,"MODULE_TYPE_UNIVERSAL",0,50,0,false,"",1);Put(slots.Modules!,module);loose.Remove(module);
                    ModuleHelper.ApplyModifiedPower(machine);
                }
                if(mode!="empty") {
                    var input=ItemApi.Create(mode=="addon"?Owner+".factory_amenity":Material,true);loose.Add(input);input.unitValue=100;Put(slots.ItemInput,input);loose.Remove(input);
                }
                if(mode=="full")for(var n=0;n<54;n++){var filler=ItemApi.Create(Material,true);loose.Add(filler);Put(slots.Output,filler);loose.Remove(filler);}
                var cost=Power.GetMachineCost(machine);var energy=Power.GetEnergy(battery);var before=slots.Output.childItems.Count;
                var result=MachineRuntimeApi.ProcessBatch(machine);var success=mode is not ("empty" or "insufficient" or "full");
                var prices=slots.Output.childItems.ToArray().Select(i=>i.unitValue).ToArray();
                details.Add(new{mode,markup,cost,energyBefore=energy,energyAfter=Power.GetEnergy(battery),inputAfter=slots.ItemInput.childItems.Count,outputBefore=before,outputAfter=slots.Output.childItems.Count,prices,result});
                Check(result.Committed==success,mode+": native commit result");
                Check(Power.GetEnergy(battery)==energy-(success?cost:0),mode+": exact native energy delta");
                Check(slots.ItemInput.childItems.Count==(success||mode=="empty"?0:1),mode+": material consumption or preservation");
                Check(slots.Output.childItems.Count==before+(success?1:0),mode+": exact output delta");
                if(success)Check(prices.Single()==100+markup,mode+": actual product price uses configured markup once");
                if(mode=="addon")Check(result.RecipeId==Addon+".terminal_value",mode+": appended author selected");
                if(mode=="efficiency")Check(cost>0&&cost<25,mode+": native module efficiency affects actual debit");
                Check(!InventoryFreezeApi.IsFrozen(machine),mode+": transaction releases freeze");
            }
            Check(_reentrantBlocked,"processing callbacks cannot mutate live terminal configuration");
            Check(MachineRegistrationApi.RegisteredRecipes(ManufacturingTerminal.ItemId).Select(r=>r.OwnerId).SequenceEqual(new[]{Owner,Addon}),"markup updates preserve author order");
        }catch(Exception e){Check(false,"execution: "+e);MelonLogger.Error("[EZapi] Terminal probe: "+e);}
        finally {
            if(ManufacturingTerminal.Snapshot().MarkupOwner==Owner)MachineRegistrationApi.ConfigureManufacturingTerminalMarkup(Owner,15);
            foreach(var item in loose.Concat(roots).Reverse())try{if(item.parentInventory is {} parent)parent.Expel(item);if(item.parentInventory is null)item.Destroy();}catch(Exception e){Check(false,"cleanup: "+e.Message);}
        }
        LastReport=new{at=DateTimeOffset.Now,kind="temporary-native-regression",passed,failed,checks,details,configuration=ManufacturingTerminal.Snapshot(),scope="temporary native terminal/markup/power/module/admission/output; no save or player delivery; debug config owner lasts until process restart"};
        EZapiBootstrap.WriteDiagnostics();return $"共享终端检查：{passed}通过，{failed}失败，已导出。";
    }
    internal static string Fixture()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.TryGet(out var target))return "请进入独立新测试档。";
        var run=NativeAccess.RequireRun();if(run.SaveSlotId<531)return "共享终端夹具只在新档（槽≥531）创建。";
        var roots=new List<GameItem>();var loose=new List<GameItem>();
        try {
            EnsureRecipes();
            var items=InventoryApi.CaptureRunItems(GameSession.Resolve(run));var existing=items.Where(i=>i.identifier==ManufacturingTerminal.ItemId).ToArray();
            if(existing.Length==0)for(var n=0;n<2;n++) {
                var machine=ItemApi.Create(ManufacturingTerminal.ItemId,true);roots.Add(machine);MachineLayoutApi.TryGetInventory(machine,out var slots);
                var battery=ItemApi.Create(Material,true);loose.Add(battery);PowerHelper.InitPowerSourceItem(battery,100,100);PowerHelper.SetPowerSourceAt(battery,100);Put(slots.Battery!,battery);loose.Remove(battery);
                for(var k=0;k<2-n;k++){var input=ItemApi.Create(Material,true);loose.Add(input);input.unitValue=100;Put(slots.ItemInput,input);loose.Remove(input);}
                Put(target,machine);
            }
            var rows=InventoryApi.CaptureRunItems(GameSession.Resolve(run)).Where(i=>i.identifier==ManufacturingTerminal.ItemId).OrderBy(i=>i.uniqueId).Select(i=>{
                MachineLayoutApi.TryGetInventory(i,out var s);return new{instance=i.uniqueId,owned=GeneralHelper.IsItemOwned(i),inputs=s.ItemInput.childItems.Count,prices=s.Output.childItems.ToArray().Select(o=>o.unitValue).ToArray(),energy=Power.GetEnergy(s.Battery!.childItems[0]),night=i.GetTagReadonly("EZAPI_MACHINE_NIGHT_ATTEMPT_V1")?.valueString,inputPointer=s.ItemInput.Pointer.ToInt64(),outputPointer=s.Output.Pointer.ToInt64(),windowPointer=i.contentWindow.Pointer.ToInt64()};
            }).ToArray();
            LastReport=new{at=DateTimeOffset.Now,kind=existing.Length==0?"prepared":"inspect",run,day=StoreStation.GetDayCounter(),rows,configuration=ManufacturingTerminal.Snapshot()};
            EZapiBootstrap.WriteDiagnostics();return "已读取共享终端夹具；请正常过夜/重启载入后再次读取。";
        }catch(Exception e){
            foreach(var item in loose.Concat(roots).Reverse())try{if(item.parentInventory is {} p)p.Expel(item);if(item.parentInventory is null)item.Destroy();}catch(Exception cleanup){MelonLogger.Error("[EZapi] Terminal cleanup: "+cleanup);}
            MelonLogger.Error("[EZapi] Terminal fixture: "+e);return "终端夹具失败："+e.Message;
        }
    }
    private static void Put(GameInventory inventory,GameItem item)
    {
        var slot=inventory.TryFindOneValidInventorySlot(item);
        if(slot?.IsValid()!=true||slot.targetItem is not null||slot.inventory?.Pointer!=inventory.Pointer)throw new InvalidOperationException("terminal fixture native slot");
        try{slot.TryAcceptOnce(1);}catch when(item.parentInventory?.Pointer==inventory.Pointer){}
        if(item.parentInventory?.Pointer!=inventory.Pointer)throw new InvalidOperationException("terminal fixture native admission readback");
    }
}

using Il2Cpp;
namespace EZapi;

/// <summary>必须/可选电源的统一注册夹具；仅机器注册按钮触发临时加工，不向玩家交付或保存。</summary>
internal static class DebugMachineBatteryRequirements
{
    private const string Owner="ezapi.debug";
    private static string Id(bool required)=>Owner+(required?".power_required_probe":".power_optional_probe");
    internal static void Register()
    {
        foreach(var required in new[]{true,false}){
            var definition=new MachineDefinition(Id(required),new(){ItemInput=new(2,2),Output=new(){Size=new(2,2)},Battery=new(new(2,2),required)},
                new[]{new MachineRecipe(Owner+".required_power_recipe",new[]{new MachineIngredient(Owner+".factory_item")},new(Owner+".factory_item"))}){
                    Power=new(0),EnableNightly=false,Guide=null,ItemOptions=new(required?"EZapi必需电源检查机":"EZapi可选电源检查机")};
            var result=MachineRegistrationApi.RegisterMachine(Owner,definition);if(!result.Accepted)throw new InvalidOperationException(result.Reason);
        }
    }
    internal static void Run(Action<bool,string> check,List<object> details)
    {
        foreach(var mode in new[]{"required-empty","optional-empty","required-empty-source","required-source","optional-positive","required-frozen","required-external","required-disconnected"}){
            var required=!mode.StartsWith("optional",StringComparison.Ordinal);var roots=new List<GameItem>();var loose=new List<GameItem>();
            IDisposable? provider=null;IDisposable? frozen=null;
            try {
                var machine=ItemApi.Create(Id(required),true);roots.Add(machine);
                if(!MachineLayoutApi.TryGetInventory(machine,out var slots))throw new InvalidOperationException("battery requirement layout missing");
                check(slots.RequiresBattery==required,mode+": native layout readback retains Required");
                var input=ItemApi.Create(Owner+".factory_item",true);loose.Add(input);Put(slots.ItemInput,input);loose.Remove(input);
                GameItem? source=null;GameItem? connector=null;
                if(mode is not ("required-empty" or "optional-empty" or "optional-positive")){
                    if(mode is "required-external" or "required-disconnected"){
                        var station=ItemApi.Create(Id(false),true);roots.Add(station);MachineLayoutApi.TryGetInventory(station,out var storage);
                        source=ItemApi.Create(Owner+".factory_item",true);loose.Add(source);PowerHelper.InitPowerSourceItem(source,100,100);PowerHelper.SetPowerSourceAt(source,0);Put(storage.Battery!,source);loose.Remove(source);
                        var id=Owner+".required_connector_"+(mode=="required-external"?"online":"offline");
                        var captured=source;
                        provider=ExternalPowerApi.Register(Owner,id,_=>mode=="required-external"?Power.GetEnergy(captured):-1,(_,_,_)=>throw new InvalidOperationException("zero-cost requirement must not create power transaction"));
                        connector=ItemDirectory.CreateEmptyItem(id);loose.Add(connector);connector.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());ExternalPowerApi.InitializeConnector(connector);GeneralHelper.SetItemOwned(connector,true);
                        Put(slots.Battery!,connector);loose.Remove(connector);
                    }else{
                        source=ItemApi.Create(Owner+".factory_item",true);loose.Add(source);PowerHelper.InitPowerSourceItem(source,100,100);PowerHelper.SetPowerSourceAt(source,mode=="required-empty-source"?0:100);Put(slots.Battery!,source);loose.Remove(source);
                        if(mode=="required-frozen")frozen=InventoryFreezeApi.TryAcquire(new[]{source},Owner+".required-power")??throw new InvalidOperationException("battery freeze fixture refused");
                    }
                }
                var energy=source is null?0:Power.GetEnergy(source);var callbacks=0;
                if(mode is "required-empty-source" or "required-external" or "required-disconnected")check(energy==0,mode+": actual native source starts at zero energy");
                var recipe=new MachineRecipe(Owner+".required_power_runtime",new[]{new MachineIngredient(Owner+".factory_item")},new(Owner+".factory_item")){
                    MachineCondition=_=>{callbacks++;return true;}};
                var processor=MachineRecipeApi.CreateBatchProcessor(Owner,machine.identifier,new[]{recipe},new(mode=="optional-positive"?1:0));
                var result=mode=="required-source"?MachineRuntimeApi.ProcessBatch(machine):processor.Process(machine);
                var success=mode is "optional-empty" or "required-empty-source" or "required-source" or "required-external";
                check(result.Committed==success,mode+": expected zero/positive cost result");
                check(slots.ItemInput.childItems.Count==(success?0:1)&&slots.Output.childItems.Count==(success?1:0),mode+": resources preserved or committed exactly once");
                check(source is null||Power.GetEnergy(source)==energy,mode+": requirement check does not consume or refill energy");
                if(mode is "required-empty" or "required-frozen" or "required-disconnected")check(result.Error?.StartsWith("EZAPI_RECIPE_BATTERY_REQUIRED",StringComparison.Ordinal)==true&&callbacks==0,mode+": rejected before author callbacks and output creation");
                check(!InventoryFreezeApi.IsFrozen(machine),mode+": no machine freeze leaked");
                details.Add(new{mode,result,required,callbacks,energy});
            }finally{
                frozen?.Dispose();provider?.Dispose();
                foreach(var item in loose.AsEnumerable().Reverse())if(item.parentInventory is null)item.Destroy();
                foreach(var root in roots.AsEnumerable().Reverse()){
                    foreach(var child in SaveGraphApi.Items(root).Reverse()){
                        if(child.parentInventory is {} parent&&!parent.Expel(child))throw new InvalidOperationException("required battery fixture expel refused");child.Destroy();
                    }
                }
            }
        }
    }
    private static void Put(GameInventory target,GameItem item)
    {var marker=target.TryFindOneValidInventorySlot(item);if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("required battery fixture admission refused");}
}

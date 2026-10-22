using Il2Cpp;
namespace EZapi;

/// <summary>真实自定义窗口/显式借用/Template边界回归；只创建临时游离图，不保存。</summary>
internal static class DebugMachineInventoryBinding
{
    private const string Owner="ezapi.debug";
    internal static void Run(Action<bool,string> check,List<object> details)
    {
        foreach(var mode in new[]{"borrow-custom","borrow-bound","borrow-template-mismatch","borrow-foreign","borrow-duplicate","borrow-canonical","borrow-required-downgrade","borrow-prepare-error"}) {
            var roots=new List<GameItem>();var loose=new List<GameItem>();
            try {
                var id=Owner+".borrowed_inventory_probe";var machine=Empty(id);roots.Add(machine);
                var canonical=mode is "borrow-canonical" or "borrow-required-downgrade";
                var template=new MachineLayoutDefinition{ItemInput=new(2,2),Output=new(){Size=new(2,2)},Battery=mode=="borrow-required-downgrade"?new(new(2,2),true):null};
                MachineInventory slots;
                if(canonical)slots=MachineLayoutApi.Create(machine,template);
                else {
                    var window=new PixelWindow(true,"EZapi借用库存检查");machine.SetContentWindow(window);
                    var root=new GridPixelElement(2,1,false);var input=new GameGridInventory(2,2){identifier=id+".custom_input"};var output=new GameGridInventory(2,2){identifier=id+".custom_output"};
                    if(!window.Attach(root.Cast<PixelElement>())||!root.AttachPos(input.Cast<PixelElement>(),0,0)||!root.AttachPos(output.Cast<PixelElement>(),1,0))throw new InvalidOperationException("custom native window attachment failed");
                    ContainerHelper.InitContainerItemTagOnly(machine);
                    slots=new(null,null,input,output,new Dictionary<string,GameSlotInventory>(),null);
                }
                check(MachineLayoutApi.TryGetInventory(machine,out _)==canonical,mode+": custom window has no EZapi marker, canonical is discoverable");
                var material=ItemApi.Create(Owner+".factory_item",true);loose.Add(material);Put(slots.ItemInput,material);loose.Remove(material);
                var supplied=slots;
                if(mode=="borrow-foreign"){
                    var other=Empty(id);roots.Add(other);var foreign=MachineLayoutApi.Create(other,new(){ItemInput=new(2,2),Output=new(){Size=new(2,2)}});
                    supplied=slots with{Output=foreign.Output};
                }
                if(mode=="borrow-duplicate")supplied=slots with{Output=slots.ItemInput};
                if(mode=="borrow-required-downgrade")supplied=slots with{RequiresBattery=false};
                var callbacks=0;
                var recipe=new MachineRecipe(Owner+".borrowed_recipe",new[]{new MachineIngredient(Owner+".factory_item")},new(Owner+".factory_item")){
                    MachineCondition=_=>{callbacks++;return true;}};
                if(mode=="borrow-prepare-error")recipe=recipe with{Output=recipe.Output with{PrepareItem=(_,_)=>throw new InvalidOperationException("expected borrowed preview failure")}};
                var expected=mode is "borrow-custom" or "borrow-bound" or "borrow-canonical";
                MachineBatchProcessor processor;
                if(mode is "borrow-bound" or "borrow-template-mismatch"){
                    var binding=mode=="borrow-template-mismatch"?new MachineLayoutDefinition{ItemInput=new(3,2),Output=new(){Size=new(2,2)}}:template;
                    processor=MachineRuntimeApi.CreateBatchProcessor(Owner,new(id,binding,new[]{recipe}){Power=new(0)});
                }else processor=MachineRecipeApi.CreateBatchProcessor(Owner,id,new[]{recipe},new(0));
                var result=processor.Process(machine,supplied);
                check(result.Committed==expected,mode+": expected commit/rejection");
                check(slots.ItemInput.childItems.Count==(expected?0:1)&&slots.Output.childItems.Count==(expected?1:0),mode+": exact native material/output readback");
                if(mode is "borrow-template-mismatch" or "borrow-foreign" or "borrow-duplicate" or "borrow-required-downgrade")
                    check(callbacks==0&&result.Error?.StartsWith("EZAPI_RECIPE_INVENTORY",StringComparison.Ordinal)==true,mode+": rejected before author callbacks");
                check(!InventoryFreezeApi.IsFrozen(machine),mode+": machine freeze released");
                details.Add(new{mode,result,callbacks,canonical});
            }finally {
                foreach(var item in loose.AsEnumerable().Reverse())if(item.parentInventory is null)item.Destroy();
                foreach(var root in roots.AsEnumerable().Reverse())foreach(var child in SaveGraphApi.Items(root).Reverse()){
                    if(child.parentInventory is {} parent&&!parent.Expel(child))throw new InvalidOperationException("borrowed inventory cleanup expel failed");child.Destroy();
                }
            }
        }
    }
    private static GameItem Empty(string id)
    {var item=ItemDirectory.CreateEmptyItem(id);item.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());GeneralHelper.SetItemOwned(item,true);return item;}
    private static void Put(GameInventory target,GameItem item)
    {var marker=target.TryFindOneValidInventorySlot(item);if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("borrowed fixture material refused");}
}

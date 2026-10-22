using Il2Cpp;
namespace EZapi;

/// <summary>临时原生机器上的只读回调边界检查；不触发BeforeNight、不读写存档、不消耗持久机器材料。</summary>
internal static class DebugMachineNightResources
{
    internal static void Run(Action<bool,string> check,List<object> details)
    {
        foreach(var mode in new[]{"readonly","machine-tag","input-tag","nested-tag","nested-value","nested-state","machine-shape","nested-shape","nested-topology","nested-lock","input-quantity","binding-swap"}) {
            GameItem? machine=null,extra=null;
            try {
                machine=Empty("ezapi.debug.night_resources");
                var slots=MachineLayoutApi.Create(machine,new(){ItemInput=new(3,3),Output=new(){Size=new(3,3)}});
                var input=Empty("ezapi.debug.night_input");extra=input;
                var inner=MachineLayoutApi.Create(input,new(){ItemInput=new(2,2),Output=new(){Size=new(2,2)}});
                Put(slots.ItemInput,input);extra=null;
                var nested=Empty("ezapi.debug.night_nested");Put(inner.ItemInput,nested);
                var run=NativeAccess.RequireRun();var before=MachineNightResources.Capture(machine,slots,run);
                check(before.Same(slots,run),"night-audit-"+mode+": native graph baseline matches");
                var target=mode=="machine-tag"?machine:mode=="input-tag"?input:nested;
                var tags=NativeTagBackup.Capture(target.state);var originalState=target.state;
                var machineShape=machine.modifiedShape;var nestedShape=nested.modifiedShape;
                var value=nested.unitValue;var quantity=input.unitCount;var locked=inner.ItemInput.overrideLockInsert;
                var supplied=slots;
                try {
                    switch(mode) {
                        case "machine-tag":case "input-tag":case "nested-tag":
                            target.state.GetTag("EZAPI_DEBUG_NIGHT_READONLY").Enable().SetString(mode);target.SyncModifiedState();break;
                        case "nested-value":nested.unitValue++;break;
                        case "nested-state":nested.state=new TagSystem();break;
                        case "machine-shape":machine.SetShape(new GridShapeBuilder(2,1).SetDataFill(1).Build());break;
                        case "nested-shape":nested.SetShape(new GridShapeBuilder(1,2).SetDataFill(1).Build());break;
                        case "nested-topology":extra=Empty("ezapi.debug.night_extra");Put(inner.ItemInput,extra);break;
                        case "nested-lock":inner.ItemInput.overrideLockInsert=true;break;
                        case "input-quantity":input.SetUnitCount(2);break;
                        case "binding-swap":supplied=slots with{ItemInput=slots.Output,Output=slots.ItemInput};break;
                    }
                    var same=before.Same(supplied,run);
                    check(same==(mode=="readonly"),"night-audit-"+mode+": expected read-only observation result");
                    details.Add(new{mode="night-audit-"+mode,same,scope="native temporary graph observation only; scheduler integration covered offline"});
                }finally {
                    // 仅还原此测试自己改变的临时对象；生产审计不会替作者回滚非法回调写入。
                    target.state=originalState;tags.Restore(target.state);target.SyncModifiedState();
                    nested.unitValue=value;input.SetUnitCount(quantity);inner.ItemInput.overrideLockInsert=locked;
                    machine.SetShape(machineShape);nested.SetShape(nestedShape);
                    if(extra is not null){if(extra.parentInventory is {} parent&&!parent.Expel(extra))throw new InvalidOperationException("night audit extra expel");extra.Destroy();extra=null;}
                }
                check(before.Same(slots,run),"night-audit-"+mode+": fixture restores exact original graph");
            }finally {
                if(extra is not null&&extra.parentInventory is null)extra.Destroy();
                if(machine is not null)foreach(var item in SaveGraphApi.Items(machine).Reverse()) {
                    if(item.parentInventory is {} parent&&!parent.Expel(item))throw new InvalidOperationException("night audit cleanup expel");item.Destroy();
                }
            }
        }
    }
    private static GameItem Empty(string id)
    {var item=ItemApi.Create("ezapi.debug.factory_item",true);item.identifier=id;item.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());GeneralHelper.SetItemOwned(item,true);return item;}
    private static void Put(GameInventory inventory,GameItem item)
    {var marker=inventory.TryFindOneValidInventorySlot(item);if(marker?.IsValid()!=true||marker.TryAcceptOnce(1)!=1)throw new InvalidOperationException("night audit fixture admission");}
}

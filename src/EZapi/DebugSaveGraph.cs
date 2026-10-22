using Il2Cpp;
namespace EZapi;

/// <summary>临时原生物品图往返；只销毁本夹具创建的图，绝不移动玩家已有物品或请求保存。</summary>
internal static class DebugSaveGraph
{
    private sealed class Owned {internal readonly GameItem Item;internal bool Retired;internal Owned(GameItem item)=>Item=item;}
    internal static object? LastReport{get;private set;}
    internal static void Clear()=>LastReport=null;
    internal static string Run()
    {
        var checks=new List<object>();var details=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        var roots=new List<Owned>();
        void Track(GameItem item)=>roots.Add(new(item));
        void Retire(GameItem root){var pointers=SaveGraphApi.Items(root).Select(i=>i.Pointer).ToHashSet();root.Destroy();foreach(var owned in roots.Where(o=>!o.Retired&&pointers.Contains(o.Item.Pointer)))owned.Retired=true;}
        void Admit(GameItem root,GameItem child){
            Track(child);var grid=root.contentWindow.childElement.TryCast<GameGridInventory>()??throw new InvalidOperationException("fixture container grid missing");
            var units=child.unitCount;var slot=grid.TryInventorySlot(child,units,child.modifiedShape,null);
            if(slot?.TryAcceptOnce(units)!=units)throw new InvalidOperationException("fixture native admission failed: "+root.identifier+" <- "+child.identifier);
        }
        try{
            var run=NativeAccess.RequireRun();var store=GameSession.Resolve(run);
            var playerBefore=store.FindAllItem(true).ToArray().Select(i=>i.uniqueId).OrderBy(i=>i).ToArray();
            Check(SaveGraphApi.CanDecode&&SaveGraphApi.DecodeUnavailableReason is null,"current exact game load sequence available");
            foreach(var mode in new[]{"plain","nested","machine","liquid"}){
                var root=ItemApi.Create(mode=="nested"?"ezapi.debug.endpoint_box":mode=="machine"?"ezapi.debug.registered_machine":mode=="liquid"?"ezapi.sample.water_container":"ezapi.debug.factory_item",true);Track(root);
                root.state.GetTag("ezapi.debug.roundtrip").Enable().SetString(mode);
                root.SetValue(187);root.SyncModifiedState();
                if(mode=="nested"){
                    var inner=ItemApi.Create("ezapi.debug.endpoint_box",true);
                    // 普通容器遵循原生禁止嵌套规则；只有显式可嵌套设备可以装入父容器。
                    inner.EnableTag("NESTABLE_DEVICE_TAG");inner.SyncModifiedState();
                    Check(ContainerHelper.IsNestableDevice(inner),"nested: native explicitly nestable device prerequisite");
                    Admit(root,inner);
                    var child=ItemApi.Create("ezapi.debug.material_stack",true);child.SetUnitCount(3);child.SetValue(31);child.SyncModifiedState();Admit(inner,child);
                }
                if(mode=="machine"){
                    if(!MachineLayoutApi.TryGetInventory(root,out var slots))throw new InvalidOperationException("machine layout absent");
                    var child=ItemApi.Create("ezapi.debug.factory_item",true);Track(child);
                    if(slots.ItemInput.TryFindOneValidInventorySlot(child)?.TryAcceptOnce(1)!=1)throw new InvalidOperationException("machine input fixture admission refused");
                }
                var before=SaveGraphApi.Items(root).Select(i=>new{i.identifier,i.uniqueId,i.unitCount,parent=i.parentInventory?.GetParentItem()?.uniqueId}).OrderBy(i=>i.uniqueId).ToArray();
                var liquid=mode=="liquid"?Liquids.Capture(root):null;
                var rootId=root.uniqueId;var value=root.unitValue;
                var scratchHeap=SaveManager.itemHeap?.Pointer;var scratchDict=SaveManager.itemDict?.Pointer;var scratchUuid=SaveManager.currentUUID;var scratchJson=SaveManager.saveJson;
                var json=SaveGraphApi.Encode(root);
                Check(SaveManager.itemHeap?.Pointer==scratchHeap&&SaveManager.itemDict?.Pointer==scratchDict&&SaveManager.currentUUID==scratchUuid&&SaveManager.saveJson==scratchJson,mode+": encode restores global native scratch");
                bool conflict=false;try{SaveGraphApi.Decode(json,new HashSet<int>{rootId});}catch(InvalidDataException ex){conflict=ex.Message.Contains("CONFLICT");}
                Check(conflict,mode+": explicit live/reserved ID refuses duplicate restore");
                Retire(root);
                var restored=SaveGraphApi.Decode(json);Track(restored);
                var after=SaveGraphApi.Items(restored).Select(i=>new{i.identifier,i.uniqueId,i.unitCount,parent=i.parentInventory?.GetParentItem()?.uniqueId}).OrderBy(i=>i.uniqueId).ToArray();
                Check(before.SequenceEqual(after),mode+": all IDs/counts and parent relations restored");
                Check(restored.parentInventory is null&&restored.uniqueId==rootId&&restored.unitValue==value,mode+": root stays detached with original value");
                Check(restored.GetTagReadonly("ezapi.debug.roundtrip")?.valueString==mode,mode+": custom saved state retained");
                Check(store.currentUniqueId>after.Max(i=>i.uniqueId),mode+": future instance ID counter remains ahead");
                Check(SaveManager.itemHeap?.Pointer==scratchHeap&&SaveManager.itemDict?.Pointer==scratchDict&&SaveManager.currentUUID==scratchUuid&&SaveManager.saveJson==scratchJson,mode+": decode restores global native scratch");
                if(liquid is not null){var current=Liquids.Capture(restored);Check(liquid.CapacityParts==current.CapacityParts&&liquid.Contents.SequenceEqual(current.Contents)&&liquid.Purity==current.Purity,"liquid: exact physical mixture and purity restored");}
                if(mode=="machine")Check(MachineLayoutApi.TryGetInventory(restored,out var loaded)&&loaded.ItemInput.childItems.Count==1,"machine: saved layout and input slots restored");
                details.Add(new{mode,nodes=before.Length,bytes=System.Text.Encoding.UTF8.GetByteCount(json)});
            }
            Check(playerBefore.SequenceEqual(store.FindAllItem(true).ToArray().Select(i=>i.uniqueId).OrderBy(i=>i)),"all player-owned inventory instances unchanged");
        }catch(Exception ex){Check(false,"execution: "+ex);MelonLoader.MelonLogger.Error("[EZapi] Save graph fixture: "+ex);}
        finally{
            foreach(var owned in roots)try{
                if(owned.Retired||owned.Item.parentInventory is not null)continue;
                Retire(owned.Item);
            }catch(Exception ex){Check(false,"fixture cleanup: "+ex.Message);}
        }
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,details,scope="current native EncodeNodes/DecodeNodes and exact post-load fixups on detached fixture graphs; no disk save/reload or player delivery"};
        EZapiBootstrap.WriteDiagnostics();return $"物品图恢复：{passed}通过，{failed}失败。";
    }
}

using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>按需比较独立单件/堆叠夹具。只创建游离对象，finally 销毁，不接触玩家库存。</summary>
internal static class DebugItemInspection
{
    internal static object? LastReport { get; private set; }
    internal static void Clear() => LastReport = null;
    internal static string Run()
    {
        if (InventoryApi.IsItemDragActive) return "请先放下拖拽物品。";
        var rows = new List<object>(); var errors = new List<string>(); var checks = new List<object>();
        var results = new Dictionary<(string Mode,long Value,int Count),long>();
        int passed=0, failed=0;
        void Check(bool ok,string name) {checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try {
            NativeAccess.RequireRun();
            foreach (var mode in new[]{"plain", "uses", "innate", "market", "final", "inactive-event", "children"})
            foreach (var value in new long[]{0, 1, 137, 10000000001})
            foreach (var count in new[]{1, 2, 5}) {
                GameItem? item = null, child = null;
                try {
                    item = ItemDirectory.CreateEmptyItem("ezapi.debug.unit_value");
                    item.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());
                    item.SetUnitCount(count); item.SetValue(value); GeneralHelper.SetItemOwned(item,true);
                    if (mode == "uses") {
                        UseCountHelper.InitUseCountItem(item,10,true,false,true,11,17);
                        UseCountHelper.SetCurrentUseCount(item,3);
                    }
                    if(mode == "children") {
                        NativeContainerAdapter.Initialize(item,new ItemDefinition {Id="ezapi.debug.unit_value",Name="估值临时容器",Container=new(){Width=2,Height=2}});
                        child=ItemDirectory.CreateEmptyItem("ezapi.debug.unit_value_child");
                        child.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());
                        child.SetUnitCount(1);child.SetValue(29);GeneralHelper.SetItemOwned(child,true);child.SyncModifiedState();
                        item.SyncModifiedState();
                        var grid=item.contentWindow.childElement.TryCast<GameGridInventory>()!;
                        var slot=grid.TryFindOneValidInventorySlot(child);
                        var accepted=slot?.TryAcceptOnce(1);
                        Check(accepted==1 && child.parentInventory is not null,$"children/{value}/{count}: native child accepted");
                    }
                    if (mode is "innate" or "market" or "final" or "inactive-event") {
                        var feature = new ItemFeature(mode == "inactive-event" ? ItemFeature.FeatureType.Event : ItemFeature.FeatureType.Normale);
                        feature.valueStage = mode == "final" ? ItemFeature.ValueStage.Final : mode == "innate" ? ItemFeature.ValueStage.Innate : ItemFeature.ValueStage.Market;
                        feature.identifier="ezapi.debug."+mode; feature.category="ezapi.debug."+mode;
                        feature.parentItemUniqueId=item.uniqueId; feature.useCondition=false;
                        feature.initiallyShown=true; feature.isFeatureExposed=true;
                        feature.SetConditions(new ItemCondition {identifier="ezapi.debug.fake",category="ezapi.debug",modValue=37},new ItemCondition {identifier="ezapi.debug.real",category="ezapi.debug",modValue=37});
                        feature.SetValueModifier(37); item.AddItemFeature(feature);
                    }
                    item.SyncModifiedState();
                    var before = State(item);
                    var current = item.GetCurrentValue(false,true,true,false,0);
                    var unit = item.GetValue(null,null,true);
                    var api = ItemInspection.CurrentUnitValue(item);
                    var total = item.GetValue(null,null,false);
                    rows.Add(new {mode,value,count,actualCount=item.unitCount,current,unit,total,api,unchanged=before==State(item),detached=item.parentInventory is null});
                    Check(before==State(item),$"{mode}/{value}/{count}: read preserves native state");
                    Check(api==current,$"{mode}/{value}/{count}: API matches native current unit value");
                    Check(item.unitCount==count,$"{mode}/{value}/{count}: actual fixture count verified");
                    if(mode is "innate" or "market" or "final" && value==137)Check(current==188 && unit==137,$"{mode}/{count}: refreshed features differ from cached GetValue");
                    Check(total==unit*count,$"{mode}/{value}/{count}: native whole stack differs only by count");
                    if(mode=="plain")Check(api==value,$"plain/{value}/{count}: exact Int64 base value");
                    if(mode=="uses")Check(api==62,$"uses/{value}/{count}: native remaining-use auto value");
                    if(mode=="children")Check(api==value+29,$"children/{value}/{count}: child value retained once per unit");
                    results[(mode,value,count)]=api;
                } catch(Exception ex) { errors.Add($"{mode}/{value}/{count}: {ex}"); }
                finally {
                    // 子物品由父存储图销毁；仅对未被接纳的游离子物品单独清理。
                    if(child is not null && child.parentInventory is null)child.Destroy();
                    item?.Destroy();
                }
            }
            foreach(var mode in new[]{"plain","uses","innate","market","final","inactive-event","children"})
            foreach(var value in new long[]{0,1,137,10000000001})
                Check(new[]{1,2,5}.All(count=>results.ContainsKey((mode,value,count))) && new[]{2,5}.All(count=>results.GetValueOrDefault((mode,value,count))==results.GetValueOrDefault((mode,value,1))),$"{mode}/{value}: stack and independent single agree");
            foreach(var mode in new[]{"innate","market","final"})
                Check(results.GetValueOrDefault((mode,137,1))!=137,$"{mode}: fixture actually changes native value");
        } catch(Exception ex) { errors.Add(ex.ToString()); }
        Check(errors.Count==0,"no native fixture exceptions");
        LastReport = new {at=DateTimeOffset.Now,passed,failed,checks,rows,errors,scope="detached temporary native fixtures; no delivery, payment or save; built-in tooltip unchanged"};
        foreach(var error in errors) MelonLogger.Error("[EZapi] Item inspection probe: "+error);
        EZapiBootstrap.WriteDiagnostics();
        return $"单件估值：{passed}通过，{failed}失败；临时物品已销毁。";
    }
    private static string State(GameItem item) => $"{item.Pointer}/{item.uniqueId}/{item.unitCount}/{item.unitValue}/{item.unitBaseValue}/{item.lateUnitValue}/{item.backupUnitValue}/{item.parentInventory?.Pointer}/{item.state.Pointer}/{item.modifiedState.Pointer}/{item.itemFeatures.Count}";
}

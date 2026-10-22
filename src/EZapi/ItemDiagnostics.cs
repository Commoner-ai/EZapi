using Il2Cpp;
using MelonLoader;
using System.Text.RegularExpressions;

namespace EZapi;

/// <summary>手动按需实验。临时实例不会进入库存，结束即销毁；只保存托管结果，不缓存原生对象。</summary>
internal static class ItemDiagnostics
{
    internal static object? LastReport { get; private set; }
    internal static string Run()
    {
        if(!VisibleStoreInventory.IsReady)
            return "请先进入测试档店铺，再校验原生属性。";
        var reports=new List<object>(); int passed=0,failed=0;
        foreach(var d in ItemRegistry.AllDefinitions()) {
            if(ItemRegistry.GetStatus(d.Id)!="Applied") { failed++; reports.Add(new {id=d.Id,error="not applied"}); continue; }
            GameItem? item=null,other=null;
            try {
                item=DirectoryMaster.Item(d.Id,true); other=DirectoryMaster.Item(d.Id,true);
                if(item is null||other is null)throw new InvalidOperationException("factory returned null");
                var errors=new List<string>();
                void Check(bool ok,string field) { if(!ok)errors.Add(field); }
                var locale=NativeItemLocalization.CurrentLocaleCode;
                Check(item.identifier==d.Id && item.name==ItemText.Name(d,locale),"identity/current-locale name");
                Check(item.Pointer!=other.Pointer && item.state.Pointer!=other.state.Pointer,"independent instances");
                Check(item.shape.width==(d.Width??1) && item.shape.height==(d.Height??1),"shape dimensions");
                var mask=new List<string>();
                for(var y=0;y<item.shape.height;y++) {
                    var row=""; for(var x=0;x<item.shape.width;x++)row+=item.shape.GetLocal(x,y)>0?'1':'0';
                    mask.Add(row);
                }
                if(d.ShapeRows is not null)Check(mask.SequenceEqual(d.ShapeRows),"shape mask");
                foreach(var type in d.Types)Check(item.IsGameItemType(type),"type:"+type);
                var tags=new List<object>();
                foreach(var t in d.Tags) {
                    var s=item.state.GetTag(t.Id);
                    Check(s.IsEnabled()==t.Enabled,"tag enabled:"+t.Id);
                    if(t.StringValue is { } v)Check(s.GetString()==v,"tag string:"+t.Id);
                    if(t.IntValue is { } i)Check(s.GetInt()==i,"tag int:"+t.Id);
                    if(t.LongValue is { } l)Check(s.valueLong==l,"tag long:"+t.Id);
                    if(t.FloatValue is { } f)Check(s.GetFloat()==f,"tag float:"+t.Id);
                    if(t.DoubleValue is { } n)Check(s.GetDouble()==n,"tag double:"+t.Id);
                    if(t.BoolValue is { } b)Check(s.GetBool()==b,"tag bool:"+t.Id);
                    tags.Add(new {id=t.Id,enabled=s.IsEnabled(),stringValue=s.GetString(),intValue=s.GetInt(),longValue=s.valueLong,floatValue=s.GetFloat(),doubleValue=s.GetDouble(),boolValue=s.GetBool()});
                }
                Check(!item.MayStack(other),"single-item stack guard");
                Check(!other.MayStack(item),"reverse single-item stack guard");
                Check(item.mayStackItemFunc is not null,"native per-item stack predicate");
                if(d.Activation is not null) {
                    Check(item.mayActivateSlotItemFunc is not null&&item.canActivateSlotItemFunc is not null&&
                        item.onActivateSlotItemFunc is not null,"native activation delegates");
                    Check(!item.MayActivateSlotItem()&&!item.CanActivateSlotItem(),"unplaced activation denied without reflection failure");
                    // 临时对象不拥有库存；这里只查绑定，不执行治疗、消耗或用户回调。
                }
                Check(item.spriteAtlasPath.StartsWith("EZapi.Content/",StringComparison.Ordinal),"isolated native atlas");
                Check(item.spritePath==d.Id&&!item.useSpriteFromMod,"native sprite binding");
                var native=d.DescriptionPlacement=="native";
                Check(item.shortDescription==(native?ItemText.Resolve(d,d.LocalizedShortDescription??d.LocalizedDescription,d.ShortDescription??d.Description,locale):""),"shortDescription");
                Check(item.longDescription==(native?ItemText.Resolve(d,d.LocalizedLongDescription,d.LongDescription,locale):""),"longDescription");
                Check(item.flavorText==(native?ItemText.Resolve(d,d.LocalizedFlavorText,d.FlavorText,locale):""),"flavorText");
                Check(item.customText==(native?ItemText.Resolve(d,d.LocalizedCustomText,d.CustomText,locale):""),"customText");
                Check(item.unitCount==d.InitialCount,"initialCount");
                if(d.UseCount?.AutoValue!=true)Check(item.unitValue==(d.Value??0),"value");
                object? containerReport=null;
                object? liquidReport=null;
                if(d.LiquidContainer is {} liquid) {
                    Check(Liquids.IsContainer(item),"native liquid container identity");
                    var contents=Liquids.Capture(item);
                    Check(contents.CapacityParts==checked(liquid.Capacity*1000),"native liquid capacity parts");
                    Check(contents.TotalParts==(liquid.StartFull?checked(liquid.Capacity*1000):0),"native starting volume parts");
                    liquidReport=contents;
                }
                if(d.Container is { } box) {
                    var grid=item.contentWindow?.childElement?.TryCast<GameGridInventory>();
                    var otherGrid=other.contentWindow?.childElement?.TryCast<GameGridInventory>();
                    Check(item.contentWindow is not null&&grid is not null,"container window/grid");
                    if(grid is not null) {
                        Check(item.contentWindow!.Pointer!=other.contentWindow?.Pointer&&grid.Pointer!=otherGrid?.Pointer,"independent container graphs");
                        Check(grid.identifier==d.Id+".storage","stable child inventory key");
                        Check(grid.inventoryShape.width==box.Width&&grid.inventoryShape.height==box.Height,"internal dimensions");
                        Check(grid.inventoryShape.GetOutsideBounds()==1,"inventory exterior blocked");
                        var usableRows=new List<string>();
                        for(var y=0;y<box.Height;y++) {
                            var row="";
                            for(var x=0;x<box.Width;x++)row+=grid.inventoryShape.GetLocal(x,y)==0?'1':'0';
                            usableRows.Add(row);
                        }
                        Check(usableRows.SequenceEqual(box.ShapeRows??Enumerable.Repeat(new string('1',box.Width),box.Height).ToArray()),"usable internal cells");
                        Check(grid.childItems.Count==0&&otherGrid?.childItems.Count==0,"new container empty");
                        if(box.AllowedIds.Count>0&&grid.mayInventoryAddItemFunc is {} accepts) {
                            var denied=ItemDirectory.CreateEmptyItem("ezapi.__diagnostic_denied");
                            try { GeneralHelper.SetItemOwned(denied,true);Check(!accepts.Invoke(denied,grid),"native ID filter rejects unrelated ID"); }
                            finally {denied.Destroy();}
                            // 只检验本包可创建的允许ID；不为未知跨包ID伪造实例。
                            if(ItemRegistry.TryGet(box.AllowedIds[0],out _)) {
                                var allowed=DirectoryMaster.Item(box.AllowedIds[0],true);
                                try{GeneralHelper.SetItemOwned(allowed,true);Check(accepts.Invoke(allowed,grid),"native ID filter accepts configured ID");}
                                finally{allowed.Destroy();}
                            }
                        }
                        containerReport=new {width=grid.inventoryShape.width,height=grid.inventoryShape.height,grid.identifier,
                            windowTitle=item.contentWindow.titleString,childCount=grid.childItems.Count,usableRows,
                            admissionBound=grid.mayInventoryAddItemFunc is not null,
                            note="临时空实例结构检查；不测试实际拖放/嵌套/存档恢复"};
                    }
                }
                var nativeConsumable=d.Food is not null||d.LiquidContainer is not null||d.Container is not null||d.Module is not null||d.Node;
                if(!nativeConsumable||d.ForceDisableUse.HasValue)Check(item.forceDisableUse==(d.ForceDisableUse??false),"forceDisableUse");
                if(!nativeConsumable||d.ForceDisableActivate.HasValue)Check(item.forceDisableActivate==(d.ForceDisableActivate??false),"forceDisableActivate");
                if(!nativeConsumable||d.ActivateDefault.HasValue)Check(item.activateDefault==(d.ActivateDefault??false),"activateDefault");
                if(!nativeConsumable||d.BonusAccuracy.HasValue)Check(item.bonusAccuracy==(d.BonusAccuracy??0),"bonusAccuracy");
                if(!nativeConsumable||d.TriggerOverwatch.HasValue)Check(item.triggerOverwatch==(d.TriggerOverwatch??false),"triggerOverwatch");
                if(!nativeConsumable||d.CanUseOutsideCombat.HasValue)Check(item.canUseOutsideCombat==(d.CanUseOutsideCombat??false),"canUseOutsideCombat");
                if(d.ContrabandLevel>0)Check(ContrabandHelper.GetContrabandLevelInInt(item)==d.ContrabandLevel,"contraband");
                if(d.UseCount is { } use) {
                    var current=use.Current??use.Maximum;
                    Check(UseCountHelper.CanUse(item,current),"useCount current");
                    Check(!UseCountHelper.CanUse(item,current+1),"useCount current upper bound");
                }
                var element=item.TryCast<GameItemElement>()??throw new InvalidOperationException("factory returned data item without tooltip element");
                var tooltip=element.GetTooltip();
                var text=Regex.Replace(tooltip.nakedText,"<[^>]*>","");
                var advanced=Regex.Replace(element.GetTooltipAdvanced().nakedText,"<[^>]*>","");
                if(d.Tooltip.ShowSource) {
                    var source=(NativeItemLocalization.IsEnglish?"Source: ":"来源：")+ItemRegistry.SourceName(d);
                    Check(text.Split(source,StringSplitOptions.None).Length==2,"source once");
                    Check(advanced.Split(source,StringSplitOptions.None).Length==2,"advanced source once");
                }
                foreach(var line in d.Tooltip.Lines) {
                    var value=ItemText.Resolve(d,line.LocalizedText,line.Text,locale);
                    Check(text.Contains(value,StringComparison.Ordinal),"tooltip line:"+value);
                }
                foreach(var label in d.Tooltip.Labels) {
                    var value=ItemText.Resolve(d,label.LocalizedText,label.Text,locale);
                    Check(text.Contains(value,StringComparison.Ordinal)==(label.TagId is null||item.GetTagReadonly(label.TagId)?.IsEnabled()==true),"tooltip label:"+value);
                }
                string? dynamicTooltip=null;
                var bound=d.Tooltip.Labels.FirstOrDefault(l=>l.TagId is not null && l.ValueKind=="int");
                if(bound is not null) {
                    var s=item.state.GetTag(bound.TagId!); var old=s.GetInt();
                    s.SetInt(913); item.SyncModifiedState();
                    dynamicTooltip=Regex.Replace(element.GetTooltip().nakedText,"<[^>]*>","");
                    Check(dynamicTooltip.Contains(ItemText.Resolve(d,bound.LocalizedText,bound.Text,locale)+" = 913",StringComparison.Ordinal),"dynamic tag tooltip");
                    Check(other.state.GetTag(bound.TagId!).GetInt()==old,"tag instance isolation");
                }
                if(errors.Count==0)passed++; else failed++;
                reports.Add(new {id=d.Id,passed=errors.Count==0,errors,types=d.Types,tags,shapeRows=mask,
                    shortDescription=item.shortDescription,longDescription=item.longDescription,flavorText=item.flavorText,customText=item.customText,
                    tooltip=text,advancedTooltip=advanced,richText=tooltip.richTextForeground,dynamicTooltip,
                    container=containerReport,liquid=liquidReport,nativeConsumable=new {food=d.Food,liquidContainer=d.LiquidContainer,module=d.Module,node=d.Node,
                        mayActivateBound=item.mayActivateSlotItemFunc is not null,canActivateBound=item.canActivateSlotItemFunc is not null,
                        onActivateBound=item.onActivateSlotItemFunc is not null,item.activateDefault,item.canUseOutsideCombat,
                        note="这里只记录 helper 声明和原生行为绑定；不执行吃喝或证明消耗/容量/保鲜/保存成功"}});
                MelonLogger.Msg("[EZapi] Native check "+d.Id+": "+(errors.Count==0?"PASS":string.Join(", ",errors)));
            } catch(Exception e) { failed++; reports.Add(new {id=d.Id,error=e.ToString()}); MelonLogger.Error("[EZapi] Native check: "+e); }
            finally { item?.Destroy(); other?.Destroy(); }
        }
        var nativeTypes=new List<object>();
        foreach(var type in TypeHelper.ALL_TYPES_LIST)nativeTypes.Add(new {id=type,name=TypeHelper.GetTypeDisplayName(type)});
        LastReport=new {at=DateTimeOffset.Now,passed,failed,scope="temporary factory instances, native tooltip; no inventory/save writes",items=reports,nativeTypes};
        EZapiBootstrap.WriteDiagnostics();
        return $"原生属性：{passed} 项通过，{failed} 项失败；详情已写入诊断文件。";
    }
}

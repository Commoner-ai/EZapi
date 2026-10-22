using Il2Cpp;
using MelonLoader.Utils;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EZapi;

/// <summary>Explicit test-only disk fixture. Stage saves only the exact disposable run; Load never saves.</summary>
internal static class DebugDiskItems
{
    private const string Owner="ezapi.debug.disk_items",Other="ezapi.debug.disk_other";
    private const string EndpointOwner="ezapi.debug",Endpoint=Owner+".fixtures";
    private static string Folder=>Path.Combine(MelonEnvironment.UserDataDirectory,"EZapi","TestTools");
    private static string Baseline=>Path.Combine(Folder,"disk-items-baseline.json");
    private sealed record Data(int Level,string Text);
    private sealed record Row(string ItemId,int UniqueId,int Units,int? ParentId,string Json,string OtherJson,string? Layout);
    private sealed record Manifest(int Pid,int Slot,string RunId,int Cash,Row[] Items);
    private static PlayerStore Require()
    {
        var run=NativeAccess.RequireRun();
        if(run.SaveSlotId!=534||run.RunId!="e39e848c-36b7-40e9-ab7e-b6adcb9881b8"||InventoryApi.IsItemDragActive)
            throw new InvalidOperationException("exact disposable run without active drag required");
        return GameSession.Resolve(run);
    }
    private static InventoryEndpoint? Open(PlayerStore store,bool create=false)=>InventoryEndpointApi.Open(store,EndpointOwner,Endpoint,"ezapi.debug.endpoint_box",create);
    private static void Write(string name,object report){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,name),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));}
    internal static void Stage()
    {
        var checks=new List<object>();int passed=0,failed=0;bool saveRequested=false;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;if(!ok)throw new InvalidOperationException(name);}
        try{
            var store=Require();Check(!File.Exists(Baseline),"one-shot baseline absent; no duplicate preparation or save");
            Check(!InventoryEndpointApi.Snapshot().Any(s=>s.OwnerId==EndpointOwner&&s.EndpointId==Endpoint),"disk fixture endpoint absent before preparation");
            var rows=new List<Row>();
            var endpoint=Open(store,true)!;
            var grid=endpoint.Root.contentWindow.childElement.TryCast<GameGridInventory>()!;
                    for(var n=0;n<4;n++){
                        var item=ItemApi.Create(n<2?"ezapi.sample.field_kit":n==2?"ezapi.sample.storage_box":"ezapi.debug.machine_liquid_probe",true);
                        // Native containers require explicit nestable-device permission inside another container.
                        if(n>=2){item.EnableTag("NESTABLE_DEVICE_TAG");item.SyncModifiedState();}
                        if(n==1)item.SetName("Player disk name / 玩家存档名");
                        var marker=grid.TryFindOneValidInventorySlot(item);
                        Check(marker?.IsValid()==true&&marker.TryAcceptOnce(1)==1,"native item admitted into persisted endpoint "+n);
                    }
                foreach(var item in SaveGraphApi.Items(endpoint.Root)){
                    var data=new Data(7,"磁盘重载 / Disk reload "+item.uniqueId);
                    var revision=ItemDataApi.Read<Data>(item,Owner,"state");
                    var written=ItemDataApi.Stage(item,revision.Revision!,data,2);
                    Check(written.Status==ItemDataStatus.StagedInMemory,"owned versioned item JSON staged: "+item.identifier);
                    var other=ItemDataApi.Read<Data>(item,Other,"state");
                    var second=ItemDataApi.Stage(item,other.Revision!,new Data(99,"independent owner"));
                    Check(second.Status==ItemDataStatus.StagedInMemory,"independent owner staged: "+item.identifier);
                    rows.Add(new(item.identifier,item.uniqueId,item.unitCount,item.parentInventory?.GetParentItem()?.uniqueId,
                        written.Json!,second.Json!,item.GetTagReadonly(MachineLayoutApi.LayoutTag)?.GetString()));
                }
            Check(rows.Count==5,"exact five fixture items staged");
            Write("disk-items-baseline.json",new Manifest(Environment.ProcessId,store.saveSlotId,store.runID,store.playerCash,rows.ToArray()));
            saveRequested=true;store.SaveGame();
            Check(InventoryEndpointApi.SaveError is null,"native SaveGame returned without endpoint save error");
        }catch(Exception error){checks.Add(new{name=error.ToString(),passed=false});failed++;}
        Write("disk-items-stage.json",new{at=DateTimeOffset.Now,pid=Environment.ProcessId,passed,failed,checks,saveRequested,
            scope="explicit disposable run only; versioned item JSON, separate owners, custom name, container and machine graphs; current-day native SaveGame; cold reload remains separate"});
    }
    internal static void Load()
    {
        var checks=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try{
            var store=Require();var baseline=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Baseline))!;
            Check(baseline.Pid!=Environment.ProcessId&&baseline.Slot==store.saveSlotId&&baseline.RunId==store.runID,"new process loaded exact saved disposable run");
            Check(store.playerCash==baseline.Cash,"cash unchanged across current-day save and cold load");
            var actual=new List<GameItem>();
                var endpoint=Open(store);Check(endpoint is not null&&endpoint.IsCurrent,"endpoint restored without creation");
                if(endpoint is null)throw new InvalidOperationException("saved endpoint missing");
                var graph=SaveGraphApi.Items(endpoint.Root);actual.AddRange(graph);
                Check(graph.Count==baseline.Items.Length,"exact saved graph size");
                foreach(var row in baseline.Items){
                    var matches=graph.Where(item=>item.uniqueId==row.UniqueId).ToArray();
                    Check(matches.Length==1,"stable unique item identity: "+row.UniqueId);if(matches.Length!=1)continue;
                    var item=matches[0];var data=ItemDataApi.Read<Data>(item,Owner,"state",2);var other=ItemDataApi.Read<Data>(item,Other,"state");
                    Check(item.identifier==row.ItemId&&item.unitCount==row.Units&&GeneralHelper.IsItemOwned(item)&&
                        item.parentInventory?.GetParentItem()?.uniqueId==row.ParentId,"saved item id/count/ownership/parent: "+row.UniqueId);
                    Check(data.Status==ItemDataStatus.Present&&data.Version==2&&data.Json==row.Json&&data.Value==new Data(7,"磁盘重载 / Disk reload "+row.UniqueId),"typed versioned JSON identical after disk reload: "+row.UniqueId);
                    Check(other.Status==ItemDataStatus.Present&&other.Json==row.OtherJson&&other.Value==new Data(99,"independent owner"),"independent owner JSON identical: "+row.UniqueId);
                    if(item.identifier=="ezapi.debug.machine_liquid_probe")Check(MachineLayoutApi.TryGetInventory(item,out _)&&item.GetTagReadonly(MachineLayoutApi.LayoutTag)?.GetString()==row.Layout,"saved machine slots restored with original layout marker");
                }
            Check(actual.Count==5&&actual.Select(i=>i.uniqueId).Distinct().Count()==5,"no missing or duplicate saved fixture item");
            Check(actual.Count(i=>i.name=="Player disk name / 玩家存档名")==1,"saved arbitrary player name restored exactly");
        }catch(Exception error){Check(false,error.ToString());}
        Write("disk-items-load.json",new{at=DateTimeOffset.Now,pid=Environment.ProcessId,passed,failed,checks,scope="new-process loaded native graph; readonly endpoint opens; no fixture creation, data staging or save"});
    }
    internal static object? VerifyLocale(string locale,Action<bool,string> check)
    {
        // The optional disk fixture belongs to one historical disposable run.
        // Other explicitly bound runs still exercise transient locale surfaces,
        // but must not open or claim that disk evidence.
        var run=NativeAccess.RequireRun();
        if(run.SaveSlotId!=534||run.RunId!="e39e848c-36b7-40e9-ab7e-b6adcb9881b8")return null;
        var store=Require();if(Open(store) is null)return null;
        var graph=SaveGraphApi.Items(Open(store)!.Root);
        var en=locale=="en";var items=graph.Where(i=>i.identifier=="ezapi.sample.field_kit").ToArray();
        check(items.Length==2,locale+": disk: both saved field kits still present");
        var baseline=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Baseline))!;
        check(baseline.Pid!=Environment.ProcessId,locale+": disk: localization tested in a new process");
        var kit=items.Single(i=>i.name!="Player disk name / 玩家存档名");kit.GetDisplayName(false);
        var tooltip=Regex.Replace(kit.Cast<GameItemElement>().GetTooltip().nakedText,"<[^>]*>","");
        check(kit.name==(en?"EZapi Item Property Demo":"EZapi 属性示例工具包")&&
            kit.shortDescription==(en?"Short description: Checks item properties and tags; grants no healing effect.":"短简介：验证物品属性和标签，不提供治疗效果。"),locale+": disk: native name and description follow current language");
        check(tooltip.Contains(en?"Quality":"品质",StringComparison.Ordinal)&&tooltip.Contains(en?"Owner":"作者",StringComparison.Ordinal),locale+": disk: native tooltip labels follow current language");
        var renamed=items.Single(i=>i.name=="Player disk name / 玩家存档名");renamed.GetDisplayName(false);
        check(renamed.name=="Player disk name / 玩家存档名",locale+": disk: display refresh preserves saved player name");
        var box=graph.Single(i=>i.identifier=="ezapi.sample.storage_box");box.GetDisplayName(false);box.RestoreContentWindowPosition();
        check(box.contentWindow.titleString==(en?"EZapi Storage Box":"EZapi 储物箱")&&box.contentWindow.titleText.text.text==box.contentWindow.titleString,locale+": disk: saved container reopen resolves localized title");
        var machine=graph.Single(i=>i.identifier=="ezapi.debug.machine_liquid_probe");machine.GetDisplayName(false);machine.RestoreContentWindowPosition();
        var ready=MachineLayoutApi.TryGetInventory(machine,out var slots);
        check(ready&&machine.contentWindow.titleString==(en?"EZapi temporary machine check":"EZapi临时机器检查")&&
            slots.LiquidInputs["water"].tooltipName==(en?"Water input":"原料水容器")&&slots.Battery!.tooltipName==(en?"Battery slot":"电池槽")&&
            slots.Manual!.tooltipName==(en?"Instructions slot":"说明书槽")&&slots.Output.Cast<GameSlotInventory>().tooltipName==(en?"Output container":"产物容器"),locale+": disk: saved machine reopen resolves title and all slot labels");
        foreach(var item in graph){
            var expected=baseline.Items.Single(r=>r.UniqueId==item.uniqueId);
            check(ItemDataApi.Read<Data>(item,Owner,"state",2).Json==expected.Json&&ItemDataApi.Read<Data>(item,Other,"state").Json==expected.OtherJson,locale+": disk: language refresh preserves both JSON owners for "+item.uniqueId);
        }
        return new{locale,pid=Environment.ProcessId,savedPid=baseline.Pid,name=kit.name,tooltip,boxTitle=box.contentWindow.titleString,machineTitle=machine.contentWindow.titleString,scope="actual cold-loaded items through native display/tooltip/container and machine reopen; item JSON unchanged"};
    }
}

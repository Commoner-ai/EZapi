using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using System.Text.Json;
namespace EZapi;

internal static class DebugInventoryEndpoint
{
    private const string Owner="ezapi.debug",RootId=Owner+".endpoint_box",Endpoint=Owner+".persistent_box";
    private const string BaselineName="inventory-endpoint-fixture.json";
    private static readonly string ProcessToken=Guid.NewGuid().ToString("N");
    private sealed record FixtureItem(string Identifier,int UniqueId,int UnitCount,int? ParentUniqueId);
    private sealed record FixtureBaseline(int SlotId,string RunId,string ProcessToken,FixtureItem[] Items);
    internal static object? LastReport {get;private set;}
    internal static void Register()
    {
        var result=ItemRegistry.RegisterBatch(new[]{new ItemDefinition{Id=RootId,Name="EZapi持久库存夹具",Container=new(){Width=4,Height=4}}});
        if(result.Any(r=>!r.Accepted))throw new InvalidOperationException(result.First(r=>!r.Accepted).Message);
    }
    internal static string Run()
    {
        var run=NativeAccess.RequireRun();var store=GameSession.Resolve(run);var checks=new List<object>();int passed=0,failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        InventoryEndpoint? box=null;GameItem? item=null;var itemDestroyed=false;
        try {
            Check(InventoryEndpointApi.IsAvailable&&InventoryEndpointRuntime.Status().Ready,"all exact endpoint hooks ready");
            var count=InventoryEndpointApi.Snapshot().Count;
            Check(InventoryEndpointApi.Open(store,Owner,Owner+".absent",RootId)==null,"read-only missing does not create");
            Check(InventoryEndpointApi.Snapshot().Count==count,"read-only query leaves registry unchanged");
            box=InventoryEndpointApi.Open(store,Owner,Owner+".transient",RootId,true,true)!;
            Check(box.IsCurrent&&box.Root.parentInventory is null&&GeneralHelper.IsItemOwned(box.Root),"detached owned native root");
            Check(ReferenceEquals(box,InventoryEndpointApi.Open(store,Owner,Owner+".transient",RootId,false,true)),"same endpoint opens same handle");
            var conflict=false;try{InventoryEndpointApi.Open(store,Owner,Owner+".transient",RootId,false,false);}catch(InvalidOperationException){conflict=true;}
            Check(conflict,"charging policy conflict preserves existing root");
            var grid=box.Root.contentWindow.childElement.TryCast<GameGridInventory>()!;item=ItemApi.Create(Owner+".factory_item",true);
            var slot=grid.TryFindOneValidInventorySlot(item);Check(slot?.IsValid()==true&&slot.TryAcceptOnce(1)==1,"charging mode permits ordinary items using native admission");
            Check(!InventoryEndpointApi.Delete(Owner,Owner+".transient"),"nonempty deletion refused");
            Check(store.FindAllItem(true).ToArray().Any(x=>x.Pointer==item.Pointer),"native FindAllItem includes hidden contents");
            var json=InventoryEndpointApi.Encode(store);var parsed=InventoryEndpointApi.Parse(json,run.SaveSlotId,run.RunId);
            Check(parsed.Endpoints.Count==count+1&&parsed.Endpoints.Any(e=>e.EndpointId==Owner+".transient"&&e.ChargingOnly),"complete bounded native graph encoded");
            Check(grid.Expel(item)&&item.parentInventory is null,"native content detach");item.Destroy();itemDestroyed=true;
            Check(InventoryEndpointApi.Delete(Owner,Owner+".transient"),"empty native endpoint deleted");
            Check(!box.IsCurrent,"deleted handle invalidated");
            Check(!InventoryEndpointApi.Delete(Owner,Owner+".transient"),"delete missing is idempotent false");
            box=null;
        }catch(Exception e){Check(false,"execution: "+e);MelonLogger.Error("[EZapi] Endpoint fixture: "+e);}
        finally {
            if(GameSession.Identity==run&&item is not null&&!itemDestroyed)try {
                if(item.parentInventory is { } parent&&box is not null&&parent.GetParentItem()?.Pointer==box.Item.Pointer)parent.Expel(item);
                if(item.Pointer!=IntPtr.Zero&&item.parentInventory is null)item.Destroy();
            }catch(Exception e){Check(false,"fixture item cleanup: "+e.Message);}
            if(GameSession.Identity==run&&box is {IsCurrent:true})try {
                if(SaveGraphApi.Items(box.Item).Count==1)InventoryEndpointApi.Delete(Owner,Owner+".transient");
                else Check(false,"fixture cleanup retained nonempty endpoint");
            }catch(Exception e){Check(false,"fixture endpoint cleanup: "+e.Message);}
        }
        LastReport=new{at=DateTimeOffset.Now,kind="batch",run,passed,failed,checks};EZapiBootstrap.WriteDiagnostics();
        return $"库存端点：{passed}通过，{failed}失败。";
    }
    internal static string Persist()
    {
        var run=NativeAccess.RequireRun();var store=GameSession.Resolve(run);
        // 新档测试。保护此前所有存档，不允许历史槽位写入持久夹具。
        if(run.SaveSlotId<533)return "请先创建新测试存档（槽≥533），历史档不生成保存夹具。";
        try {
            var existing=InventoryEndpointApi.Open(store,Owner,Endpoint,RootId,false,true);
            var mode=existing is null?"prepared":"readback";
            var root=(existing??InventoryEndpointApi.Open(store,Owner,Endpoint,RootId,true,true)!).Root;
            var grid=root.contentWindow.childElement.TryCast<GameGridInventory>()!;
            GameItem? made=null;
            try {
                if(existing is null) {
                    made=ItemApi.Create(Owner+".factory_item",true);
                    var marker=grid.TryFindOneValidInventorySlot(made);
                    if(marker?.TryAcceptOnce(1)!=1)throw new InvalidOperationException("fixture admission");
                }
            }finally {
                // Only destroy the exact newly-created item while it is still detached.
                if(made is not null&&made.Pointer!=IntPtr.Zero&&made.parentInventory is null)made.Destroy();
            }
            var items=SaveGraphApi.Items(root).Select(i=>new FixtureItem(i.identifier,i.uniqueId,i.unitCount,i.parentInventory?.GetParentItem()?.uniqueId))
                .OrderBy(i=>i.UniqueId).ToArray();
            var ok=items.Length==2&&items.Count(i=>i.Identifier==Owner+".factory_item")==1;
            var folder=Path.Combine(MelonEnvironment.UserDataDirectory,"EZapi");Directory.CreateDirectory(folder);
            var baselinePath=Path.Combine(folder,BaselineName);FixtureBaseline? baseline=null;
            if(mode=="prepared") {
                baseline=new(run.SaveSlotId,run.RunId,ProcessToken,items);
                File.WriteAllText(baselinePath,JsonSerializer.Serialize(baseline));
            }else if(File.Exists(baselinePath)) {
                baseline=JsonSerializer.Deserialize<FixtureBaseline>(File.ReadAllText(baselinePath));
            }
            var sameScope=baseline is not null&&baseline.SlotId==run.SaveSlotId&&StringComparer.Ordinal.Equals(baseline.RunId,run.RunId);
            var coldRestart=sameScope&&!StringComparer.Ordinal.Equals(baseline!.ProcessToken,ProcessToken);
            var sameInstances=sameScope&&baseline!.Items.SequenceEqual(items);
            var passed=mode=="prepared"?ok:ok&&coldRestart&&sameInstances;
            LastReport=new{at=DateTimeOffset.Now,kind=mode,run,passed,fixtureValid=ok,coldRestartVerified=coldRestart,sameScope,sameInstances,
                baseline=baselinePath,items};EZapiBootstrap.WriteDiagnostics();
            return mode=="prepared"?"隐藏库存已备好；请通过原生UI保存、退出游戏并重启，再按此按钮核对实例ID。":
                coldRestart&&sameInstances?"冷启动读档通过：端点与全部物品实例ID/数量一致。":
                "尚未证明冷启动持久恢复；请检查诊断中的 baseline、sameScope、coldRestartVerified 与 sameInstances。";
        }catch(Exception e){LastReport=new{kind="persist-error",error=e.ToString()};EZapiBootstrap.WriteDiagnostics();return "端点失败："+e.Message;}
    }
    internal static string Remove()
    {
        var run=NativeAccess.RequireRun();if(run.SaveSlotId<533)return "仅新测试档可移除夹具。";
        var store=GameSession.Resolve(run);var entry=InventoryEndpointApi.Open(store,Owner,Endpoint,RootId,false,true);
        if(entry is null)return "夹具不存在。";
        var root=entry.Root;var grid=root.contentWindow.childElement.TryCast<GameGridInventory>()!;
        var children=grid.childItems.ToArray();if(children.Any(i=>i.identifier!=Owner+".factory_item"))return "存在非夹具物品，拒绝删除。";
        foreach(var item in children){if(!grid.Expel(item))throw new InvalidOperationException("fixture expel");item.Destroy();}
        var ok=InventoryEndpointApi.Delete(Owner,Endpoint);
        if(ok)try{File.Delete(Path.Combine(MelonEnvironment.UserDataDirectory,"EZapi",BaselineName));}catch(Exception e){MelonLogger.Warning("[EZapi] Could not remove endpoint fixture baseline: "+e.Message);}
        LastReport=new{at=DateTimeOffset.Now,kind="deleted",run,passed=ok,missing=InventoryEndpointApi.Open(store,Owner,Endpoint,RootId)==null};EZapiBootstrap.WriteDiagnostics();
        return "夹具移除："+ok+"；需再次原生保存/重载验证。";
    }
}

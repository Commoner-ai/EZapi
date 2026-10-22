using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>仅显式按钮在新测试档中放置夜间夹具；正常过夜/保存/重载由游戏UI完成。</summary>
internal static class DebugMachineNight
{
    private const string Owner="ezapi.debug";
    private static readonly string[] Modes={"default","dynamic","disabled","invalid"};
    private static IDisposable? _replay;
    internal static object? LastReport {get;private set;}
    internal static void Clear(){LastReport=null;_replay?.Dispose();_replay=null;}
    internal static void Register()
    {
        foreach(var mode in Modes) {
            var id=Owner+".night_"+mode;var d=new ItemDefinition{Id=id,Name="EZapi夜间检查-"+mode,Width=1,Height=1};
            LegacySpriteAdapter.Commit(d,LegacySpriteAdapter.Prepare(d,null));
            var result=MachineRegistrationApi.RegisterMachine(Owner,new(id,new(){ItemInput=new(3,3),Output=new(){Size=new(3,3)}},new[]{
                new MachineRecipe(Owner+".night_recipe_"+mode,new[]{new MachineIngredient(Owner+".factory_item")},new(Owner+".factory_item"))
            }) {CreateItem=()=>NativeItemFactory.Create(d),Power=new(0),EnableNightly=mode!="disabled",ResolveNightlyBatchCount=mode=="dynamic"?_=>2:mode=="invalid"?_=>0:null,ItemOptions=new(d.Name),Guide=null});
            if(!result.Accepted)throw new InvalidOperationException(result.Reason);
        }
        // 在真实官方BeforeNight分发链中重放同一上下文，核对去重；不会再次调用EndNight/保存。
        _replay=LifecycleApi.Subscribe(Owner,"night-replay-check",LifecyclePhase.BeforeNight,ctx=>{
            if(ctx.Run.SlotId<528)return;
            var before=Snapshot(InventoryApi.CaptureRunItems(ctx.Store));if(before.Length==0)return;
            MachineRuntimeApi.HandleNight(ctx);var after=Snapshot(InventoryApi.CaptureRunItems(ctx.Store));
            LastReport=new{at=DateTimeOffset.Now,kind="actual-before-night-replay",run=ctx.Run,passed=before.SequenceEqual(after),before,after};
            if(!before.SequenceEqual(after))MelonLogger.Error("[EZapi] Night replay changed fixture resource counts.");
        },priority());
        static int priority()=>100;
    }
    private sealed record Row(string ItemId,int InstanceId,int Input,int Output,bool Layout,string? NightMarker,bool Owned,long InputIdentity,long OutputIdentity,long WindowIdentity);
    private static Row[] Snapshot(IReadOnlyList<GameItem> items)=>items.Where(i=>Modes.Any(mode=>i.identifier==Owner+".night_"+mode)).OrderBy(i=>i.identifier,StringComparer.Ordinal).Select(i=>{
        var found=MachineLayoutApi.TryGetInventory(i,out var slots);
        return new Row(i.identifier,i.uniqueId,found?Count(slots.ItemInput):-1,found?Count(slots.Output):-1,found,i.GetTagReadonly("EZAPI_MACHINE_NIGHT_ATTEMPT_V1")?.valueString,GeneralHelper.IsItemOwned(i),found?slots.ItemInput.Pointer.ToInt64():0,found?slots.Output.Pointer.ToInt64():0,i.contentWindow?.Pointer.ToInt64()??0);
    }).ToArray();
    private static int Count(GameInventory inv){var total=0;foreach(var item in inv.childItems)total+=item.unitCount;return total;}
    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.TryGet(out var target))return "请进入新测试档店铺并放下拖拽物品。";
        var run=NativeAccess.RequireRun();
        if(run.SaveSlotId<528)return "夜间夹具只在新测试档（槽≥528）生成，保留既有档；请从主菜单创建新测试档。";
        var store=GameSession.Resolve(run);var items=InventoryApi.CaptureRunItems(store);var rows=Snapshot(items);
        if(rows.Length>0) {
            var day=StoreStation.GetDayCounter();
            var ok=rows.Length==4&&rows.All(r=>r.Layout&&r.Owned)&&rows.Select(r=>r.InputIdentity).Distinct().Count()==4&&rows.Select(r=>r.OutputIdentity).Distinct().Count()==4&&rows.Select(r=>r.WindowIdentity).Distinct().Count()==4;
            LastReport=new{at=DateTimeOffset.Now,kind="inspect",run,day,passed=ok,rows};EZapiBootstrap.WriteDiagnostics();
            return "夜间夹具已读取："+rows.Length+"台；请核对导出中的真实投入/产物/夜次，正常睡觉后再次读取。";
        }
        var roots=new List<GameItem>();var loose=new List<GameItem>();
        try {
            foreach(var mode in Modes) {
                var machine=ItemApi.Create(Owner+".night_"+mode,true);roots.Add(machine);if(!MachineLayoutApi.TryGetInventory(machine,out var slots))throw new InvalidOperationException("night fixture layout missing");
                var count=mode=="default"?2:mode=="dynamic"?3:1;
                for(var n=0;n<count;n++) {
                    var input=ItemApi.Create(Owner+".factory_item",true);loose.Add(input);Put(slots.ItemInput,input);loose.Remove(input);
                }
                Put(target,machine);
            }
            LastReport=new{at=DateTimeOffset.Now,kind="prepared",run,day=StoreStation.GetDayCounter(),passed=true,rows=Snapshot(InventoryApi.CaptureRunItems(store))};
            EZapiBootstrap.WriteDiagnostics();return "新测试档已放置4台夜间机器：默认1批/动态2批/停用/无效额度。按正常UI过夜后点此读取，不自动保存。";
        }catch(Exception e){
            // 仅处理本次工厂创建的根和未装入根的材料；父机销毁负责子物品，不重复销毁子包装。
            foreach(var item in loose.Where(i=>i.parentInventory is null).Concat(roots).Reverse().ToArray()) {
                try {
                    if(item.parentInventory is {} parent)parent.Expel(item);
                    if(item.parentInventory is null)item.Destroy();
                    else MelonLogger.Error("[EZapi] Night fixture cleanup retained attached item "+item.identifier);
                }catch(Exception cleanup){MelonLogger.Error("[EZapi] Night fixture cleanup: "+cleanup);}
            }
            MelonLogger.Error("[EZapi] Night fixture: "+e);return "夜间夹具失败："+e.Message;
        }
    }
    private static void Put(GameInventory inv,GameItem item)
    {
        var before=inv.childItems.Count;var marker=inv.TryFindOneValidInventorySlot(item);
        if(marker?.IsValid()!=true||marker.inventory?.Pointer!=inv.Pointer||marker.targetItem is not null)throw new InvalidOperationException("night fixture native admission");
        try{marker.TryAcceptOnce(1);}catch when(item.parentInventory?.Pointer==inv.Pointer){}
        if(item.parentInventory?.Pointer!=inv.Pointer||inv.childItems.Count!=before+1||!inv.childItems.ToArray().Any(i=>i.Pointer==item.Pointer))
            throw new InvalidOperationException("night fixture native admission readback");
    }
}

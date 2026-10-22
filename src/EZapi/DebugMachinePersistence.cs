using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>
/// 可丢弃档534的真实跨夜夹具。只在显式按钮创建；保存、退出、重载均走游戏原生流程。
/// 外电回调每次按当前周目重新寻找电站，不把任何原生包装对象保留到下一帧或下一进程。
/// </summary>
internal static class DebugMachinePersistence
{
    private const string Owner="ezapi.debug", Prefix=Owner+".persist_";
    private const int Slot=534;
    private const string RunId="e39e848c-36b7-40e9-ab7e-b6adcb9881b8";
    private const string Counter="EZAPI_PERSIST_COMMITTED", Probe="EZAPI_PERSIST_PROBE";
    private static readonly string[] Modes={"station","external","filled","blocked"};
    private static readonly List<IDisposable> Leases=new();
    internal static object? LastReport {get;private set;}
    internal static object? ReplayReport {get;private set;}
    private static bool IsTestRun()=>GameSession.TryResolve(null,out _)&&NativeAccess.RequireRun() is {} r&&r.SaveSlotId==Slot&&r.RunId==RunId;
    internal static void Clear(){foreach(var l in Leases.AsEnumerable().Reverse())l.Dispose();Leases.Clear();LastReport=null;ReplayReport=null;}

    internal static void Register()
    {
        // 先登记提供者，再登记连接器工厂；工厂只初始化新的游离、未拥有连接器。
        Leases.Add(ExternalPowerApi.Register(Owner,Prefix+"connector",_=>FindSource() is {} s?Power.GetEnergy(s):-1,
            (_,_,amount)=>new SourceTransaction(FindSource()??throw new InvalidOperationException("persistent source disconnected"),amount)));
        Submit(ItemApi.RegisterItem(Owner,Prefix+"battery",()=>{
            var i=ItemDirectory.CreateEmptyItem(Prefix+"battery");i.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());
            PowerHelper.InitPowerSourceItem(i,100,100);PowerHelper.SetPowerSourceAt(i,100);return i;
        },new("EZapi跨夜电池")));
        Submit(ItemApi.RegisterItem(Owner,Prefix+"connector",()=>{
            var i=ItemDirectory.CreateEmptyItem(Prefix+"connector");i.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());
            ExternalPowerApi.InitializeConnector(i);return i;
        },new("EZapi跨夜外电连接器")));
        foreach(var mode in Modes) {
            var id=Prefix+mode;
            var d=new ItemDefinition{Id=id,Name="EZapi保存重载-"+mode,Width=1,Height=1};
            LegacySpriteAdapter.Commit(d,LegacySpriteAdapter.Prepare(d,null));
            var liquid=mode=="external";
            var ingredients=mode=="filled"?new[]{new MachineIngredient(Owner+".material_stack")}:
                mode=="station"?Array.Empty<MachineIngredient>():new[]{new MachineIngredient("ezapi.sample.storage_box"){WholeStack=true}};
            MachineItemOutput output=liquid?new MachineContainerOutput(new[]{new MachineLiquidAmount("water",1,ResolveValue:_=>8){ResolveQualityBasis=_=>3}}):
                mode=="filled"?new MachineItemOutput("ezapi.sample.water_container"){
                    Contents=new[]{new MachineLiquidAmount("water",1,ResolveValue:_=>8){ResolveQualityBasis=_=>3}}}:
                new MachineItemOutput(Owner+".factory_item");
            var recipe=new MachineRecipe(Prefix+"recipe_"+mode,ingredients,output){
                MaterialContents=mode=="external"?MachineMaterialContentsPolicy.ConsumeGraph:MachineMaterialContentsPolicy.Reject,
                LiquidInputs=mode is "external" or "filled"?new[]{new MachineLiquidIngredient("water",1)}:Array.Empty<MachineLiquidIngredient>(),
                AfterCommitted=(plan,_)=>{
                    var machine=plan.Context.Machine;
                    var count=machine.GetTagReadonly(Counter)?.GetInt()??0;
                    machine.state.GetTag(Counter).Enable().SetInt(count+1);machine.SyncModifiedState();
                }
            };
            Submit(MachineRegistrationApi.RegisterMachine(Owner,new(id,new(){ItemInput=new(6,4),
                Output=new(){Kind=liquid?"container":"items",Size=new(3,3)},Battery=new(new(2,2),mode!="blocked"),
                LiquidInputs=mode is "external" or "filled"?new[]{new MachineContainerSlot{SlotId="water",Size=new(3,3)}}:Array.Empty<MachineContainerSlot>()},
                mode=="station"?Array.Empty<MachineRecipe>():new[]{recipe}){
                    CreateItem=()=>NativeItemFactory.Create(d),ItemOptions=new(d.Name),Power=new(mode=="external"?7:mode=="filled"?5:0),
                    EnableNightly=mode!="station",Guide=null
                }));
        }
        Leases.Add(LifecycleApi.Subscribe(Owner,"persistence-replay",LifecyclePhase.BeforeNight,ctx=>{
            if(!IsTestRun()||!ctx.IsCurrent)return;
            var roots=Roots(InventoryApi.CaptureRunItems(ctx.Store));if(roots.Length==0)return;
            var before=System.Text.Json.JsonSerializer.Serialize(State(roots));
            MachineRuntimeApi.HandleNight(ctx);
            var after=System.Text.Json.JsonSerializer.Serialize(State(roots));
            ReplayReport=new{at=DateTimeOffset.Now,run=ctx.Run,passed=before==after,kind="same-native-before-night-replay",before,after};
            if(before!=after)MelonLogger.Error("[EZapi] Persistent night replay changed resources.");
        },order:200));
    }
    private static void Submit(ContentSubmissionResult r){if(!r.Accepted)throw new InvalidOperationException(r.Reason);}
    private static GameItem[] Roots(IReadOnlyList<GameItem> items)=>items.Where(i=>Modes.Any(m=>i.identifier==Prefix+m)).OrderBy(i=>i.identifier,StringComparer.Ordinal).ToArray();
    private static GameItem? FindSource()
    {
        if(!IsTestRun())return null;
        var run=NativeAccess.RequireRun();var items=InventoryApi.CaptureRunItems(GameSession.Resolve(run));
        var station=items.Where(i=>i.identifier==Prefix+"station"&&GeneralHelper.IsItemOwned(i)&&i.parentInventory is not null).ToArray();
        if(station.Length!=1||!MachineLayoutApi.TryGetInventory(station[0],out var s)||s.Battery?.childItems.Count!=1)return null;
        var source=s.Battery.childItems[0];return source.identifier==Prefix+"battery"&&GeneralHelper.IsItemOwned(source)?source:null;
    }
    private static int Commits(GameItem machine)=>machine.GetTagReadonly(Counter)?.GetInt()??0;
    private static object[] State(GameItem[] roots)=>roots.Select(machine=>{
        if(!MachineLayoutApi.TryGetInventory(machine,out var slots))throw new InvalidOperationException("persistent layout unavailable");
        return (object)new{machineId=machine.identifier,instanceId=machine.uniqueId,committed=Commits(machine),owned=GeneralHelper.IsItemOwned(machine),
            parent=machine.parentInventory?.identifier,layout=machine.GetTagReadonly(MachineLayoutApi.LayoutTag)?.valueString,
            marker=machine.GetTagReadonly("EZAPI_MACHINE_NIGHT_ATTEMPT_V1")?.valueString,
            input=slots.ItemInput.childItems.ToArray().OrderBy(i=>i.uniqueId).Select(i=>new{instanceId=i.uniqueId,itemId=i.identifier,count=i.unitCount}).ToArray(),
            output=slots.Output.childItems.ToArray().OrderBy(i=>i.uniqueId).Select(i=>new{instanceId=i.uniqueId,itemId=i.identifier,count=i.unitCount}).ToArray(),
            battery=slots.Battery?.childItems.Count==1?new{instanceId=slots.Battery.childItems[0].uniqueId,itemId=slots.Battery.childItems[0].identifier,energy=Power.GetEnergy(slots.Battery.childItems[0])}:null,
            graph=SaveGraphApi.Items(machine).OrderBy(i=>i.uniqueId).Select(i=>new{instanceId=i.uniqueId,itemId=i.identifier,count=i.unitCount,value=i.unitValue,
                parentItem=i.parentInventory?.GetParentItem()?.uniqueId,parentInventory=i.parentInventory?.identifier,
                probe=i.GetTagReadonly(Probe)?.valueString,nestable=i.IsTag("NESTABLE_DEVICE_TAG"),
                liquid=i.identifier=="ezapi.sample.water_container"?Liquids.CaptureProduction(i):null}).ToArray()};
    }).ToArray();

    internal static string Run()
    {
        if(InventoryApi.IsItemDragActive||!VisibleStoreInventory.TryGet(out var target)||!IsTestRun())return "仅测试档534指定周目可使用跨夜保存夹具。";
        var checks=new List<object>();var passed=0;var failed=0;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        var kind="inspect";
        try {
            var run=NativeAccess.RequireRun();var store=GameSession.Resolve(run);var roots=Roots(InventoryApi.CaptureRunItems(store));
            if(roots.Length==0){Prepare(target);kind="prepared";roots=Roots(InventoryApi.CaptureRunItems(store));}
            Check(roots.Length==4&&roots.Select(i=>i.identifier).Distinct().Count()==4,"all four persisted roots present exactly once");
            Check(roots.All(i=>GeneralHelper.IsItemOwned(i)&&i.parentInventory?.Pointer==target.Pointer),"all roots owned in visible store");
            Check(roots.Select(i=>i.contentWindow.Pointer).Distinct().Count()==4,"four independent native windows");
            var external=roots.Single(i=>i.identifier==Prefix+"external");var filled=roots.Single(i=>i.identifier==Prefix+"filled");
            var blocked=roots.Single(i=>i.identifier==Prefix+"blocked");var e=Commits(external);var f=Commits(filled);
            Check(e==f&&e is >=0 and <=2,"both successful machines advance together within two supplied batches");
            Check(MachineLayoutApi.TryGetInventory(external,out var es)&&MachineLayoutApi.TryGetInventory(filled,out _)&&MachineLayoutApi.TryGetInventory(blocked,out _),"layouts reconstructed from registered factories");
            MachineLayoutApi.TryGetInventory(filled,out var fs);MachineLayoutApi.TryGetInventory(blocked,out var bs);
            Check(es.ItemInput.childItems.Count==2-e&&es.ItemInput.childItems.ToArray().All(i=>SaveGraphApi.Items(i).Count==3),"whole three-level material graph consumed once per night");
            Check(bs.ItemInput.childItems.Count==1&&SaveGraphApi.Items(bs.ItemInput.childItems[0]).Count==3&&Commits(blocked)==0&&bs.Output.childItems.Count==0,"default reject preserves complete nested material graph");
            Check(FindSource() is {} source&&Power.GetEnergy(source)==100-7*e,"external native station debited exactly once per committed batch");
            Check(es.Battery?.childItems.Count==1&&ExternalPowerApi.IsConnected(es.Battery.childItems[0])&&Power.GetEnergy(es.Battery.childItems[0])==100-7*e,"reconstructed connector resolves current-run station");
            Check(fs.Battery?.childItems.Count==1&&Power.GetEnergy(fs.Battery.childItems[0])==100-5*f,"ordinary saved battery retains exact energy");
            Check(es.LiquidInputs["water"].childItems.Count==1&&Liquids.CaptureProduction(es.LiquidInputs["water"].childItems[0]).TotalParts==(10-e)*1000,"external source liquid exact saved debit");
            Check(fs.LiquidInputs["water"].childItems.Count==1&&Liquids.CaptureProduction(fs.LiquidInputs["water"].childItems[0]).TotalParts==(10-f)*1000,"ordinary source liquid exact saved debit");
            var output=Liquids.CaptureProduction(es.Output.childItems[0]);
            Check(output.TotalParts==e*1000&&output.Contents.Sum(c=>c.Value)==e*8&&output.Contents.Sum(c=>c.QualityBasis)==e*3,"existing output vessel retains produced parts/value/quality");
            Check(fs.ItemInput.childItems.Count==1&&fs.ItemInput.childItems[0].unitCount==3-f&&fs.Output.childItems.Count==f,"stack debit and filled item delivery once per batch");
            Check(fs.Output.childItems.ToArray().All(i=>{var s=Liquids.CaptureProduction(i);return s.TotalParts==1000&&s.Contents.Sum(c=>c.Value)==8&&s.Contents.Sum(c=>c.QualityBasis)==3;}),"new liquid-filled products retain metadata after disk load");
            var graphs=roots.SelectMany(i=>SaveGraphApi.Items(i)).ToArray();
            Check(graphs.Select(i=>i.uniqueId).Distinct().Count()==graphs.Length,"no duplicate instance identity in fixture graphs");
            Check(graphs.All(i=>GeneralHelper.IsItemOwned(i)&&!InventoryFreezeApi.IsFrozen(i)),"all surviving descendants owned and unfrozen");
            Check(graphs.Where(i=>i.IsTag("NESTABLE_DEVICE_TAG")).All(i=>i.GetTagReadonly(Probe)?.valueString=="nested:"+i.uniqueId&&i.unitValue==13),"saved nested material tags and value preserved");
            if(e>0)Check(roots.Where(i=>i.identifier!=Prefix+"station").All(i=>!string.IsNullOrWhiteSpace(i.GetTagReadonly("EZAPI_MACHINE_NIGHT_ATTEMPT_V1")?.valueString)),"night attempt markers persisted on successful and rejected machines");
            LastReport=new{at=DateTimeOffset.Now,kind,passed,failed,checks,run,day=StoreStation.GetDayCounter(),committed=e,state=State(roots),replay=ReplayReport,
                scope="exact disposable slot/run; actual native UI night/save/cold-load performed separately; inspect never processes or saves"};
        }catch(Exception error){Check(false,error.ToString());LastReport=new{at=DateTimeOffset.Now,kind,passed,failed,checks};MelonLogger.Error("[EZapi] Persistent machine fixture: "+error);}
        EZapiBootstrap.WriteDiagnostics();return $"跨夜保存夹具：{kind}，{passed}通过，{failed}失败。";
    }
    private static void Prepare(GameInventory target)
    {
        // 逐个追踪新对象；出现准入失败时整次夹具回收，不留下半套已拥有机器。
        var made=new List<GameItem>();
        GameItem New(string id){var item=ItemApi.Create(id,true);made.Add(item);return item;}
        GameItem Water(int ml){var i=New("ezapi.sample.water_container");WaterHelper.EmptyContainer(i);if(ml>0)WaterHelper.AddLiquid(i,"water",checked(ml*1000));return i;}
        GameItem Graph(){var root=New("ezapi.sample.storage_box");var child=New("ezapi.sample.storage_box");var grand=Water(2);
            foreach(var i in new[]{root,child}){i.EnableTag("NESTABLE_DEVICE_TAG");i.state.GetTag(Probe).Enable().SetString("nested:"+i.uniqueId);i.SetValue(13);i.SyncModifiedState();}
            Put(Grid(child),grand);Put(Grid(root),child);return root;}
        try {
            foreach(var mode in Modes){
                var machine=New(Prefix+mode);MachineLayoutApi.TryGetInventory(machine,out var s);
                if(mode is "station" or "filled")Put(s.Battery!,New(Prefix+"battery"));
                if(mode=="external"){Put(s.Battery!,New(Prefix+"connector"));Put(s.Output,Water(0));for(var n=0;n<2;n++)Put(s.ItemInput,Graph());}
                if(mode=="filled"){var stack=New(Owner+".material_stack");stack.SetUnitCount(3);Put(s.ItemInput,stack);}
                if(mode=="blocked")Put(s.ItemInput,Graph());
                if(mode is "external" or "filled")Put(s.LiquidInputs["water"],Water(10));
                Put(target,machine);
            }
        }catch{
            // 子物品先拆再销毁；不访问已销毁包装对象，也不安装Destroy补丁。
            foreach(var i in made.AsEnumerable().Reverse())try{if(i.parentInventory is {} p&&!p.Expel(i))throw new InvalidOperationException("fixture cleanup expel");i.Destroy();}catch(Exception cleanup){MelonLogger.Error("[EZapi] Persistence cleanup: "+cleanup);}
            throw;
        }
    }
    private static GameGridInventory Grid(GameItem i)=>i.contentWindow.childElement.TryCast<GameGridInventory>()??throw new InvalidOperationException("fixture nested grid unavailable");
    private static void Put(GameInventory inv,GameItem i){var marker=inv.TryFindOneValidInventorySlot(i);if(marker?.IsValid()!=true||marker.TryAcceptOnce(i.unitCount)!=i.unitCount||i.parentInventory?.Pointer!=inv.Pointer)throw new InvalidOperationException("persistent fixture admission: "+i.identifier+" -> "+inv.identifier);}
    private sealed class SourceTransaction:IPowerTransaction
    {
        // 仅本次同步批次借用；不放进静态字段、跨夜回调或持久数据。
        private readonly GameItem _source;private readonly int _before,_amount;
        internal SourceTransaction(GameItem source,int amount)=>(_source,_before,_amount)=(source,Power.GetEnergy(source),amount);
        public bool Validate()=>IsTestRun()&&FindSource()?.Pointer==_source.Pointer&&Power.GetEnergy(_source)==_before;
        public void Debit(){if(!Validate()||!PowerHelper.DrawPowerSource(_source,_amount))throw new InvalidOperationException("persistent native station debit rejected");}
        public bool Verify()=>IsTestRun()&&FindSource()?.Pointer==_source.Pointer&&Power.GetEnergy(_source)==_before-_amount;
        public bool Restore(){PowerHelper.SetPowerSourceAt(_source,_before);return Power.GetEnergy(_source)==_before;}
        public void Fault(string reason)=>MelonLogger.Error("[EZapi] Persistent station fault: "+reason);
    }
}

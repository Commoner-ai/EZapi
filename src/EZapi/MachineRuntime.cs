using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>机器运行时门控和官方BeforeNight调度。手动加工只执行一批，夜间才解析批次数；无自动SaveGame调用。</summary>
public static class MachineRuntimeApi
{
    private const string AttemptTag="EZAPI_MACHINE_NIGHT_ATTEMPT_V1";
    private static IDisposable? _night;
    private static bool _started,_scanning;
    private static long _generation;
    private static GameRunIdentity? _run;private static int _day=-1;
    private static readonly HashSet<int> Attempted=new();
    private static readonly Queue<object> History=new();
    public static bool RuntimeInstalled=>_started&&GameSession.OnThread&&MachineRecipeApi.Ready&&LifecycleApi.GetRuntimeStatus().Ready&&
        InventoryAdmissionApi.IsAvailable&&InventoryFreezeApi.IsAvailable;
    public static bool TryGetInventory(GameItem machine,out MachineInventory inventory)=>MachineLayoutApi.TryGetInventory(machine,out inventory);
    public static MachineBatchResult ProcessBatch(GameItem machine)=>MachineRecipeApi.ProcessBatch(machine);
    public static MachineBatchProcessor CreateBatchProcessor(string ownerId,MachineDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return MachineRecipeApi.CreateBoundProcessor(ownerId,definition);
    }
    internal static void Start()
    {
        if(_started)return;_started=true;_generation++;
        _night=LifecycleApi.Subscribe("ezapi.machine-runtime","before-night",LifecyclePhase.BeforeNight,HandleNight,order:0);
    }
    internal static void Stop(){_started=false;_generation++;_night?.Dispose();_night=null;Attempted.Clear();History.Clear();_run=null;_day=-1;}
    internal static object Report()=>new{ready=RuntimeInstalled,scanning=_scanning,generation=_generation,run=_run,day=_day,attempted=Attempted.Count,history=History.ToArray(),scope="BeforeNight; persistent per-instance attempt marker; synchronous max64 batches; no automatic saving"};
    internal static void HandleNight(LifecycleContext context)
    {
        if(!RuntimeInstalled||_scanning||!context.IsCurrent||!context.ItemsComplete||InventoryApi.IsItemDragActive)return;
        var run=NativeAccess.RequireRun();var day=context.Run.Day;
        if(day<0||run.SaveSlotId!=context.Run.SlotId||run.RunId!=context.Run.RunId)return;
        _scanning=true;var generation=_generation;
        try {
            if(_run!=run||_day!=day){_run=run;_day=day;Attempted.Clear();}
            var token=run.SaveSlotId.ToString(System.Globalization.CultureInfo.InvariantCulture)+"/"+run.RunId+"/"+day.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // 前一个订阅者可能已经消费并销毁材料图；Context.Items是此前的惰性快照，不能再解引用
            // 其中的旧材料包装对象。每次调度从当前原生库存图重新捕获，只保留本次机器列表。
            var targets=InventoryApi.CaptureScoped(context.Store,()=>context.IsCurrent)
                .Where(i=>MachineRegistrationApi.Contains(i.identifier)).OrderBy(i=>i.uniqueId)
                .Select(i=>(Id:i.identifier,Instance:i.uniqueId,Pointer:i.Pointer)).ToArray();
            foreach(var target in targets) {
                if(!_started||generation!=_generation||!context.IsCurrent||!NativeAccess.SameRun(run))break;
                // 上一台机器可能把另一台机器当作材料整图消费；只保存托管身份，再从活图解析。
                var machine=InventoryApi.CaptureScoped(context.Store,()=>context.IsCurrent)
                    .FirstOrDefault(i=>i.uniqueId==target.Instance&&i.identifier==target.Id&&i.Pointer==target.Pointer);
                if(machine is null)continue;
                if(!GeneralHelper.IsItemOwned(machine)||machine.unitCount!=1||machine.parentInventory is null||
                    !MachineRegistrationApi.TryNightly(machine.identifier,out var resolve)||!Attempted.Add(machine.uniqueId))continue;
                var instance=machine.uniqueId;var pointer=machine.Pointer;var id=machine.identifier;var parent=machine.parentInventory.Pointer;
                try {
                    if(machine.GetTagReadonly(AttemptTag)?.valueString==token)continue;
                    if(MachineFaultGuard.Blocked(machine)) {Add(id,instance,day,0,"quarantined");continue;}
                    if(!MachineLayoutApi.TryGetInventory(machine,out _)){Add(id,instance,day,0,"slots-unavailable");continue;}
                    bool Current()=>_started&&generation==_generation&&context.IsCurrent&&NativeAccess.SameRun(run)&&machine.Pointer==pointer&&machine.uniqueId==instance&&
                        machine.identifier==id&&machine.unitCount==1&&GeneralHelper.IsItemOwned(machine)&&machine.parentInventory?.Pointer==parent;
                    // 先记本夜已尝试，再执行；失败/重复回调不会变成追加生产。随游戏本次正常保存一起持久化。
                    machine.state??=new TagSystem();machine.state.GetTag(AttemptTag).Enable().SetString(token);machine.SyncModifiedState();
                    if(machine.GetTagReadonly(AttemptTag)?.valueString!=token||!Current())throw new InvalidOperationException("night attempt marker readback failed");
                    int Resolve() {
                        var slots=TryGetInventory(machine,out var s)?s:throw new InvalidOperationException("layout disappeared");
                        var before=MachineNightResources.Capture(machine,slots,run);var count=resolve?.Invoke(machine)??1;
                        if(!Current()||!TryGetInventory(machine,out var currentSlots)||!before.Same(currentSlots,run))throw new InvalidOperationException("nightly count resolver changed resources");return count;
                    }
                    var result=MachineNightCycle.Run(Resolve,()=>ProcessBatch(machine),Current);Add(id,instance,day,result.Committed,result.Status);
                }catch(Exception e){Add(id,instance,day,0,"callback-error: "+e.Message);MelonLogger.Warning("[EZapi] EZAPI_MACHINE_NIGHT: "+id+"/"+instance+": "+e.Message);}
            }
        }finally{_scanning=false;}
    }
    private static void Add(string id,int instance,int day,int committed,string status)
    {History.Enqueue(new{machineId=id,instanceId=instance,day,committed,status});while(History.Count>128)History.Dequeue();MelonLogger.Msg("[EZapi] Night machine="+id+" instance="+instance+" day="+day+" committed="+committed+" status="+status);}
}

/// <summary>批次数解析器只读审计：完整机器图（标签/身份/内装物/位置）及库存、电量、液体；不修复作者的非法写入。</summary>
internal sealed class MachineNightResources
{
    private readonly NativeInventorySnapshot[] _inventories;private readonly long[] _values;private readonly int _energy;
    private readonly Dictionary<string,MachineLiquidSnapshot> _liquids;
    private readonly MachineMaterialGraph? _state;
    private MachineNightResources(GameItem machine,MachineInventory s,GameRunIdentity run):this(s,run)
    {_state=MachineMaterialGraph.CaptureState(machine);}
    private MachineNightResources(MachineInventory s,GameRunIdentity run)
    {
        _inventories=Inventories(s).Select(i=>InventoryChecks.Read(i,run)).ToArray();var values=new List<long>();foreach(var item in s.ItemInput.childItems)values.Add(item.unitValue);_values=values.ToArray();
        _energy=s.Battery?.childItems.Count==1?Power.GetEnergy(s.Battery.childItems[0]):0;
        _liquids=s.LiquidInputs.Where(e=>e.Value.childItems.Count==1).ToDictionary(e=>e.Key,e=>Liquids.CaptureProduction(e.Value.childItems[0]),StringComparer.Ordinal);
        if(s.Output.TryCast<GameSlotInventory>() is not null&&s.Output.childItems.Count==1)_liquids.Add(".output",Liquids.CaptureProduction(s.Output.childItems[0]));
    }
    internal static MachineNightResources Capture(GameItem machine,MachineInventory s,GameRunIdentity run)=>new(machine,s,run);
    internal bool Same(MachineInventory s,GameRunIdentity run)
    {
        if(_state?.Matches()!=true)return false;
        var now=new MachineNightResources(s,run);
        return _inventories.Length==now._inventories.Length&&_inventories.Select((a,i)=>InventoryChecks.Equal(a,now._inventories[i])).All(x=>x)&&_values.SequenceEqual(now._values)&&
            _energy==now._energy&&_liquids.Count==now._liquids.Count&&_liquids.All(e=>now._liquids.TryGetValue(e.Key,out var b)&&LiquidMetadata.Same(e.Value,b));
    }
    private static IEnumerable<GameInventory> Inventories(MachineInventory s)
    {
        yield return s.ItemInput;yield return s.Output;if(s.Battery is {} b)yield return b;if(s.Modules is {} m)yield return m;if(s.Manual is {} n)yield return n;
        foreach(var slot in s.LiquidInputs.OrderBy(e=>e.Key,StringComparer.Ordinal))yield return slot.Value;
    }
}

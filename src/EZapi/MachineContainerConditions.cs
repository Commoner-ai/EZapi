using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>仅托管规则视图，不保留或暴露物品/库存引用。</summary>
public sealed record MachineContainerConditionView(string OwnerId,string MachineId,IReadOnlyList<string> Slots,bool Faulted,string? Error);

/// <summary>
/// 精确机器类型与槽位的C#容器条件。统一注册自动安装；低层布局使用者显式Register并释放句柄。
/// 不把委托写进存档；作者初始化时重新声明。多个作者AND组合，不能取消原生拒绝。
/// </summary>
public static class MachineContainerConditionApi
{
    private sealed class Rule:IDisposable
    {
        internal readonly string Owner,Machine,Layout;internal readonly IReadOnlyDictionary<string,Func<GameItem,bool>> Conditions;
        internal bool Faulted;internal string? Error;private int _disposed;
        internal bool Disposed=>Volatile.Read(ref _disposed)!=0;
        internal Rule(string owner,string machine,string layout,IReadOnlyDictionary<string,Func<GameItem,bool>> conditions)
            =>(Owner,Machine,Layout,Conditions)=(owner,machine,layout,conditions);
        public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
    }
    private static readonly List<Rule> Rules=new();
    private static readonly HashSet<(IntPtr,IntPtr)> Evaluating=new();
    private static bool _started;private static long _generation;
    internal static void Start(){_started=true;_generation++;}
    internal static void Stop(){_started=false;_generation++;foreach(var rule in Rules)rule.Dispose();Rules.Clear();Evaluating.Clear();}
    /// <summary>初始化主线程调用。复制模板/委托，空条件返回无操作句柄；同owner/机器重复规则拒绝。句柄释放只撤销自身限制。</summary>
    public static IDisposable Register(string ownerId,string machineId,MachineLayoutDefinition template)
    {
        GameSession.RequireThread();MachineRecipeRules.Owner(ownerId);MachineRecipeRules.Id(machineId);
        if(!_started||Evaluating.Count!=0)throw new InvalidOperationException("EZAPI_MACHINE_CONDITION_NOT_READY: stopped or evaluating.");
        var spec=JsonMachineLayout.Copy(template);var conditions=new Dictionary<string,Func<GameItem,bool>>(StringComparer.Ordinal);
        if(spec.Output.ContainerCondition is {} output)conditions.Add("output",output);
        foreach(var slot in spec.LiquidInputs)if(slot.Condition is {} condition)conditions.Add("liquid."+slot.SlotId,condition);
        var rule=new Rule(ownerId,machineId,JsonMachineLayout.Serialize(spec),conditions);
        if(conditions.Count==0)return rule;
        Rules.RemoveAll(r=>r.Disposed);
        if(Rules.Count>=256)throw new InvalidOperationException("EZAPI_MACHINE_CONDITION_LIMIT: max256 rules.");
        if(Rules.Any(r=>r.Owner==ownerId&&r.Machine==machineId))throw new ArgumentException("EZAPI_MACHINE_CONDITION_DUPLICATE: dispose old rule before replacing.");
        if(Rules.Any(r=>r.Machine==machineId&&r.Layout!=rule.Layout))throw new ArgumentException("EZAPI_MACHINE_CONDITION_LAYOUT: all rules must describe the same layout.");
        Rules.Add(rule);return rule;
    }
    public static IReadOnlyList<MachineContainerConditionView> Snapshot()
    {
        GameSession.RequireThread();return Array.AsReadOnly(Rules.Where(r=>!r.Disposed).Select(r=>new MachineContainerConditionView(r.Owner,r.Machine,
            Array.AsReadOnly(r.Conditions.Keys.OrderBy(k=>k,StringComparer.Ordinal).ToArray()),r.Faulted,r.Error)).ToArray());
    }
    internal static void RequireRegistered(string machineId,MachineLayoutDefinition spec)
    {
        var required=spec.LiquidInputs.Where(s=>s.Condition is not null).Select(s=>(Slot:"liquid."+s.SlotId,Condition:s.Condition!)).ToList();
        if(spec.Output.ContainerCondition is {} condition)required.Add(("output",condition));
        var layout=JsonMachineLayout.Serialize(spec);
        if(required.Any(c=>!Rules.Any(r=>!r.Disposed&&!r.Faulted&&r.Machine==machineId&&r.Layout==layout&&r.Conditions.TryGetValue(c.Slot,out var active)&&active==c.Condition)))
            throw new InvalidOperationException("EZAPI_MACHINE_CONDITION_MISSING: low-level layouts must Register conditions before creating instances.");
    }
    internal static bool Allows(GameItem item,GameInventory inventory,GameItem machine,string layout,string slot)
    {
        if(!_started)return true;
        if(!GameSession.OnThread)return false;
        // 读档重建阶段不执行作者条件；加工入口在当前周目再次强制检查。
        if(!GameSession.TryResolve(null,out _))return true;
        var rules=Rules.Where(r=>!r.Disposed&&r.Machine==machine.identifier&&r.Conditions.ContainsKey(slot)).ToArray();
        if(rules.Length==0)return true;
        if(!Evaluating.Add((item.Pointer,inventory.Pointer)))return false;
        var key=(item.Pointer,inventory.Pointer);var generation=_generation;var run=NativeAccess.RequireRun();
        try {
            foreach(var rule in rules) {
                if(rule.Disposed)continue;
                if(rule.Faulted||rule.Layout!=layout)return false;
                try {
                    var identity=InventoryChecks.Item(item);var ownerIdentity=InventoryChecks.Item(machine);
                    var parent=item.parentInventory?.Pointer??IntPtr.Zero;var ownerParent=machine.parentInventory?.Pointer??IntPtr.Zero;
                    var state=item.state.Pointer;var dict=item.state.dict.Pointer;var value=item.unitValue;
                    var ownerState=machine.state.Pointer;var ownerDict=machine.state.dict.Pointer;var ownerValue=machine.unitValue;
                    var tags=NativeTagBackup.Capture(item.state);var ownerTags=NativeTagBackup.Capture(machine.state);
                    var target=InventoryChecks.Read(inventory,run);
                    var permitted=rule.Conditions[slot](item);
                    if(!_started||generation!=_generation||!NativeAccess.SameRun(run))return false;
                    if(identity!=InventoryChecks.Item(item)||ownerIdentity!=InventoryChecks.Item(machine)||
                        (item.parentInventory?.Pointer??IntPtr.Zero)!=parent||(machine.parentInventory?.Pointer??IntPtr.Zero)!=ownerParent||
                        item.state.Pointer!=state||item.state.dict.Pointer!=dict||item.unitValue!=value||!tags.Matches(item.state)||
                        machine.state.Pointer!=ownerState||machine.state.dict.Pointer!=ownerDict||machine.unitValue!=ownerValue||!ownerTags.Matches(machine.state)||
                        inventory.GetParentItem()?.Pointer!=machine.Pointer||!InventoryChecks.Equal(target,InventoryChecks.Read(inventory,run)))
                        throw new InvalidOperationException("read-only predicate changed observed item, machine or inventory");
                    if(!permitted)return false;
                }catch(Exception error) {
                    rule.Faulted=true;rule.Error=error.GetType().Name+": "+error.Message;
                    MelonLogger.Warning("[EZapi] EZAPI_MACHINE_CONDITION_CALLBACK: "+rule.Owner+" "+rule.Machine+"/"+slot+": "+rule.Error);
                    return false;
                }
            }
            return _started&&generation==_generation&&NativeAccess.SameRun(run);
        }finally{Evaluating.Remove(key);}
    }
}

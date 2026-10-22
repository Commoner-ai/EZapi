using Il2Cpp;
using MelonLoader;
namespace EZapi;

public enum MachineAutomationPhase { BeforeBatch, AfterBatch }

/// <summary>仅同步回调有效。Before只读资源，可选择配方或单向Block；不同订阅选择冲突时Block，不以优先级抢写。</summary>
public sealed class MachineAutomationContext
{
    private readonly GameItem _machine;private readonly MachineInventory _inventory;private Func<bool> _current;
    private readonly long _generation;private bool _active=true,_after,_blocked;private string? _recipe;
    private MachineBatchResult? _result;
    internal MachineAutomationContext(GameItem machine,MachineInventory inventory,Func<bool> current,long generation)
        =>(_machine,_inventory,_current,_generation)=(machine,inventory,current,generation);
    public bool IsCurrent {get{try{return _active&&GameSession.OnThread&&MachineAutomationApi.Current(_generation)&&_current();}catch{return false;}}}
    public GameItem Machine {get{Require();return _machine;}}
    public MachineInventory Inventory {get{Require();return _inventory;}}
    public string? RecipeId {get{Require();return _recipe;}}
    public bool Blocked {get{Require();return _blocked;}}
    public string? Result {get{Require();return _result is null?null:_result.Committed?"produced:"+_result.RecipeId:_result.Error??_result.Transaction?.Error??"rejected";}}
    public MachineBatchResult? BatchResult {get{Require();return _result;}}
    public void SelectRecipe(string recipeId)
    {
        RequireControl();MachineRecipeRules.Id(recipeId);
        if(_recipe is not null&&_recipe!=recipeId)_blocked=true;
        _recipe=recipeId;
    }
    public void Block(){RequireControl();_blocked=true;}
    private void Require(){if(!IsCurrent)throw new InvalidOperationException("EZAPI_MACHINE_AUTOMATION_STALE: context expired or resources changed.");}
    private void RequireControl(){Require();if(_after)throw new InvalidOperationException("EZAPI_MACHINE_AUTOMATION_PHASE: control only in BeforeBatch.");}
    internal bool IsBlocked=>_blocked;
    internal string? Selection=>_recipe;
    internal void Reject()=>_blocked=true;
    internal void Complete(MachineBatchResult result,Func<bool> current){_after=true;_result=result;_recipe=result.RecipeId;_current=current;}
    internal void Close()=>_active=false;
}

/// <summary>主线程有序机器回调；每owner/机器/phase一条订阅，Dispose后可重订。异常禁用该订阅，Before失败阻止本批。</summary>
public static class MachineAutomationApi
{
    private sealed class Subscription:IDisposable
    {
        internal readonly string Owner,Machine;internal readonly MachineAutomationPhase Phase;internal readonly int Order;
        internal readonly Action<MachineAutomationContext> Callback;private int _disposed;
        internal bool Disposed=>Volatile.Read(ref _disposed)!=0;
        internal Subscription(string owner,string machine,MachineAutomationPhase phase,Action<MachineAutomationContext> callback,int order)
            =>(Owner,Machine,Phase,Callback,Order)=(owner,machine,phase,callback,order);
        public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
    }
    private static readonly List<Subscription> Subscriptions=new();private static readonly Queue<object> Errors=new();
    private static bool _started;private static long _generation;
    internal static void Start(){_started=true;_generation++;}
    internal static void Stop(){_started=false;_generation++;foreach(var s in Subscriptions)s.Dispose();Subscriptions.Clear();Errors.Clear();}
    internal static bool Current(long generation)=>_started&&generation==_generation;
    internal static MachineAutomationContext Create(GameItem machine,MachineInventory inventory,Func<bool> current)=>new(machine,inventory,current,_generation);
    public static IReadOnlyList<MachineRecipe> Recipes(string machineId)=>MachineRegistrationApi.Recipes(machineId);
    public static IReadOnlyList<RegisteredMachineRecipe> RegisteredRecipes(string machineId)=>MachineRegistrationApi.RegisteredRecipes(machineId);
    public static IDisposable Subscribe(string ownerId,string machineId,MachineAutomationPhase phase,Action<MachineAutomationContext> callback,int priority=0)
    {
        GameSession.RequireThread();MachineRecipeRules.Owner(ownerId);MachineRecipeRules.Id(machineId);ArgumentNullException.ThrowIfNull(callback);
        if(!_started)throw new InvalidOperationException("EZAPI_MACHINE_AUTOMATION_NOT_READY: initialize EZapi first.");
        if(!Enum.IsDefined(typeof(MachineAutomationPhase),phase))throw new ArgumentOutOfRangeException(nameof(phase));
        Subscriptions.RemoveAll(s=>s.Disposed);
        if(Subscriptions.Any(s=>s.Owner==ownerId&&s.Machine==machineId&&s.Phase==phase))throw new ArgumentException("EZAPI_MACHINE_AUTOMATION_DUPLICATE: owner/machine/phase already subscribed.");
        if(Subscriptions.Count>=256)throw new InvalidOperationException("EZAPI_MACHINE_AUTOMATION_LIMIT: max256 subscriptions.");
        var result=new Subscription(ownerId,machineId,phase,callback,priority);Subscriptions.Add(result);return result;
    }
    internal static void Dispatch(string machineId,MachineAutomationPhase phase,MachineAutomationContext context)
    {
        if(!_started)return;
        foreach(var s in Subscriptions.Where(s=>!s.Disposed&&s.Machine==machineId&&s.Phase==phase).OrderBy(s=>s.Order).ThenBy(s=>s.Owner,StringComparer.Ordinal).ToArray()) {
            if(!context.IsCurrent)break;if(s.Disposed)continue;
            try{s.Callback(context);}catch(Exception e){
                s.Dispose();if(phase==MachineAutomationPhase.BeforeBatch)context.Reject();
                var error=e.GetType().Name+": "+e.Message;Errors.Enqueue(new{s.Owner,s.Machine,phase,error});while(Errors.Count>64)Errors.Dequeue();
                MelonLogger.Warning("[EZapi] EZAPI_MACHINE_AUTOMATION_CALLBACK "+s.Owner+"/"+s.Machine+"/"+phase+": "+error);
            }
        }
        Subscriptions.RemoveAll(s=>s.Disposed);
    }
    internal static object Report()=>new{ready=_started,generation=_generation,subscriptions=Subscriptions.Where(s=>!s.Disposed).Select(s=>new{s.Owner,s.Machine,s.Phase,s.Order}).ToArray(),errors=Errors.ToArray()};
}

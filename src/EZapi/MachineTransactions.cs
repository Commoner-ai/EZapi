using Il2Cpp;
namespace EZapi;

/// <summary>
/// 一次同步加工的资源事务。Validate只读；Apply负责材料/液体/电力和产物交付；Verify核对实际差额。
/// Restore必须覆盖Apply部分完成的状态，并将新产品移回游离态，不能销毁新产品；Fault只记录故障，不写库存。
/// 不能跨回调缓存原生对象。
/// 框架不猜测配方、不自动扣费；具体资源语义由事务实现，后续标准配方处理器复用同一契约。
/// </summary>
public interface IMachineBatchTransaction
{
    bool Validate();
    void Apply(IReadOnlyList<GameItem> products);
    bool Verify();
    bool Restore();
    void Fault(string reason);
}

/// <summary>每个Count创建独立单件，不自动堆叠。Prepare只允许配置本次新产品，不得消费资源或交付。</summary>
public sealed record MachineProductDefinition(string ItemId,int Count=1,Action<MachineBatchContext,GameItem>? Prepare=null);
public enum MachineTransactionStatus { Committed, Rejected, Restored, RecoveryFailed, Reentrant }
/// <summary>纯托管收据，不持有原生物品。Created不表示已交付；只有Committed表示事务完成。</summary>
public sealed record MachineProductReceipt(string ItemId,int InstanceId);
public sealed record MachineTransactionResult(MachineTransactionStatus Status,IReadOnlyList<MachineProductReceipt> Created,
    string? Error,IReadOnlyList<string> RecoveryFailures,string? CompletionError=null)
{
    public bool Committed=>Status==MachineTransactionStatus.Committed;
    /// <summary>恢复失败时不可自动重试；完成通知异常仍然Committed，也不可再次扣材料。</summary>
    public bool MayRetry=>Status is MachineTransactionStatus.Rejected or MachineTransactionStatus.Restored;
}

/// <summary>
/// 加工事务执行器：只创建本次新产物；预检→创建/配置→复核→一次Apply→Verify→成功通知。
/// 失败先调用资源补偿，再清理仍游离的本次新产物。已附着或身份不明的产物保留并报告RecoveryFailed。
/// 不调用SaveGame、不排程、不自动添加模块工作效果；成功通知由作者显式提供。
/// </summary>
public static class MachineTransactionApi
{
    private static bool _started,_running;
    private static long _generation,_committed,_restored,_faults;
    private static string? _lastError;
    internal static void Start(){_started=true;_generation++;}
    internal static void Stop(){_started=false;_generation++;}
    internal static object Report()=>new{ready=_started,running=_running,committed=_committed,restored=_restored,
        recoveryFailed=_faults,lastError=_lastError,scope="synchronous custom resource transaction; no recipe resolver, scheduler or automatic saving"};

    /// <summary>
    /// 主线程/当前周目；最多64新单件，0产品用于作者自管液体产出。事务在Validate中检查全部资格、输出空间和冻结。
    /// 产品在Apply前一直游离，禁止Prepare交付。调用后不要保留transaction/context中的原生引用。
    /// afterCommitted异常只记录CompletionError，不回滚已确认成功的加工，不再次发出通知。
    /// </summary>
    public static MachineTransactionResult Run(string ownerId,MachineBatchContext context,
        IReadOnlyList<MachineProductDefinition> products,IMachineBatchTransaction transaction,
        Action<MachineBatchContext,IReadOnlyList<GameItem>>? afterCommitted=null)
    {
        GameSession.RequireThread();
        if(ownerId is null||ownerId.Length>160||!System.Text.RegularExpressions.Regex.IsMatch(ownerId,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z"))
            throw new ArgumentException("EZAPI_BATCH_OWNER: full lowercase owner ID<=160 required.");
        ArgumentNullException.ThrowIfNull(context);ArgumentNullException.ThrowIfNull(products);ArgumentNullException.ThrowIfNull(transaction);
        if(context.Items.Any(t=>t.Item is null)||context.LiquidContainers.Values.Any(i=>i is null))
            throw new ArgumentException("EZAPI_BATCH_CONTEXT: null native resource reference.");
        var definitions=products.ToArray();
        if(context.OutputCount is <1 or >1000000000 || definitions.Length>64 || definitions.Any(p=>p is null ||
            string.IsNullOrWhiteSpace(p.ItemId)||p.ItemId.Length>160||p.ItemId.Any(char.IsControl)||p.Count is <1 or >64) ||
            definitions.Sum(p=>(long)p.Count)>64 || definitions.Length>0&&definitions.Sum(p=>(long)p.Count)!=context.OutputCount)
            throw new ArgumentException("EZAPI_BATCH_PRODUCTS: max64 new single items; nonempty product count must equal context.OutputCount.");
        var created=new List<Product>();var recovery=new List<string>();
        MachineTransactionResult Result(MachineTransactionStatus status,string? error=null,string? completion=null)=>
            new(status,Array.AsReadOnly(created.Select(p=>p.Receipt).ToArray()),error,Array.AsReadOnly(recovery.ToArray()),completion);
        if(!_started)return Result(MachineTransactionStatus.Rejected,"EZAPI_BATCH_NOT_READY");
        if(_running)return Result(MachineTransactionStatus.Reentrant,"EZAPI_BATCH_REENTRANT");
        var run=NativeAccess.RequireRun();var machine=NativeAccess.RequireItem(context.Machine);
        if(machine.unitCount!=1||!GeneralHelper.IsItemOwned(machine)||InventoryApi.IsItemDragActive)
            return Result(MachineTransactionStatus.Rejected,"EZAPI_BATCH_MACHINE: owned single machine and no drag required.");
        var pointer=machine.Pointer;var instance=machine.uniqueId;var id=machine.identifier;
        var parent=machine.parentInventory?.Pointer??IntPtr.Zero;var generation=_generation;
        var borrowed=new HashSet<IntPtr>(context.Items.Select(t=>t.Item.Pointer).Concat(context.LiquidContainers.Values.Select(i=>i.Pointer))) {pointer};
        var attempted=false;_running=true;
        bool Current()=>_started&&generation==_generation&&NativeAccess.SameRun(run)&&machine.Pointer==pointer&&
            machine.uniqueId==instance&&machine.identifier==id&&machine.unitCount==1&&GeneralHelper.IsItemOwned(machine)&&
            (machine.parentInventory?.Pointer??IntPtr.Zero)==parent;
        bool SameScope()=>_started&&generation==_generation&&NativeAccess.SameRun(run);
        void Fault(string reason) {
            _faults++;_lastError=ownerId+": "+reason;
            // Fault是纯诊断回调，即使周目变化也只能记录故障，不允许作者触碰旧库存。
            try{transaction.Fault(reason);}catch(Exception e){recovery.Add("fault: "+e.GetType().Name+": "+e.Message);}
        }
        MachineTransactionResult Fail(string error) {
            if(attempted) {
                try {
                    if(!Current())recovery.Add("resources: machine/run/lifecycle changed; recovery callback refused.");
                    else if(!transaction.Restore()||!Current())recovery.Add("resources: Restore returned false or changed machine identity.");
                }catch(Exception e){recovery.Add("resources: "+e.GetType().Name+": "+e.Message);}
            }
            // Restore异常不能跳过产物清理；只处理本次框架目录调用返回的新实例，不查找或清空其它库存。
            for(var i=created.Count-1;i>=0;i--) {
                var p=created[i];
                try {
                    if(!SameScope()||!p.Matches()) {recovery.Add("product "+p.Receipt.InstanceId+": stale identity; preserved.");continue;}
                    if(p.Item.parentInventory is not null){recovery.Add("product "+p.Receipt.InstanceId+": still attached; preserved.");continue;}
                    p.Item.Destroy();
                }catch(Exception e){recovery.Add("product "+p.Receipt.InstanceId+": "+e.GetType().Name+": "+e.Message);}
            }
            if(recovery.Count>0){Fault(error);return Result(MachineTransactionStatus.RecoveryFailed,error);}
            if(attempted){_restored++;return Result(MachineTransactionStatus.Restored,error);}
            return Result(MachineTransactionStatus.Rejected,error);
        }
        try {
            if(!transaction.Validate()||!Current())return Fail("EZAPI_BATCH_PRECHECK: resource validation or machine identity changed.");
            foreach(var definition in definitions)for(var i=0;i<definition.Count;i++) {
                if(!Current())return Fail("EZAPI_BATCH_STALE: context changed before product creation.");
                var item=Items.Create(definition.ItemId,true);
                if(borrowed.Contains(item.Pointer)) {
                    recovery.Add("factory returned borrowed context item; preserved.");
                    return Fail("EZAPI_BATCH_PRODUCT: factory returned a resource owned by the caller.");
                }
                if(created.Any(p=>p.Item.Pointer==item.Pointer))return Fail("EZAPI_BATCH_PRODUCT: factory reused a product instance.");
                var product=new Product(item);created.Add(product);
                if(item.unitCount!=1)
                    return Fail("EZAPI_BATCH_PRODUCT: fresh independent single product required.");
                definition.Prepare?.Invoke(context,item);
                if(!Current()||!product.Detached())return Fail("EZAPI_BATCH_PREPARE: product or machine changed/was delivered during preparation.");
            }
            var nativeProducts=Array.AsReadOnly(created.Select(p=>p.Item).ToArray());
            if(!transaction.Validate()||!Current()||InventoryApi.IsItemDragActive||created.Any(p=>!p.Detached()))
                return Fail("EZAPI_BATCH_RECHECK: resources changed before Apply.");
            attempted=true; // Apply可能写入一半后抛异常；该路径也必须调用Restore。
            transaction.Apply(nativeProducts);
            if(!Current()||!transaction.Verify()||!Current()||created.Any(p=>!p.Delivered()))
                return Fail("EZAPI_BATCH_VERIFY: resource delta, product delivery or machine readback failed.");
            _committed++;_lastError=null;
            if(afterCommitted is not null) {
                try{afterCommitted(context,nativeProducts);}
                catch(Exception e){return Result(MachineTransactionStatus.Committed,completion:e.GetType().Name+": "+e.Message);}
            }
            return Result(MachineTransactionStatus.Committed);
        }catch(Exception e){return Fail("EZAPI_BATCH_CALLBACK: "+e.GetType().Name+": "+e.Message);}
        finally{_running=false;}
    }

    private sealed class Product
    {
        internal readonly GameItem Item;internal readonly MachineProductReceipt Receipt;
        private readonly IntPtr _pointer;
        internal Product(GameItem item){Item=item;_pointer=item.Pointer;Receipt=new(item.identifier,item.uniqueId);}
        internal bool Matches()=>Item.Pointer==_pointer&&Item.uniqueId==Receipt.InstanceId&&Item.identifier==Receipt.ItemId&&Item.unitCount==1&&GeneralHelper.IsItemOwned(Item);
        internal bool Detached()=>Matches()&&Item.parentInventory is null;
        internal bool Delivered()=>Matches()&&Item.parentInventory is {Pointer: var p}&&p!=IntPtr.Zero;
    }
}

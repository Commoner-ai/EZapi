using Il2Cpp;
namespace EZapi;

/// <summary>外电作者的同步事务。Validate/Verify只读；Debit至多一次；Restore必须幂等且恢复真实电源，不能只修连接器标签。</summary>
public interface IPowerTransaction
{
    bool Validate();
    void Debit();
    bool Verify();
    bool Restore();
    void Fault(string reason);
}

/// <summary>精确物品ID的外电提供方。负查询值表示断开，0表示已连接但没电。初始化可登记，IsAvailable才表示原生拦截就绪。</summary>
public static class ExternalPowerApi
{
    internal sealed class Provider:IDisposable
    {
        internal readonly string Owner,ItemId;
        internal readonly Func<GameItem,int> Energy;
        internal readonly Func<GameItem,GameItem,int,IPowerTransaction> Plan;
        internal readonly MachineMaterialContentsPolicy FurnaceMaterials;
        private int _disposed;
        internal bool Disposed=>Volatile.Read(ref _disposed)!=0;
        internal bool Calling,Reentered;
        internal Provider(string owner,string id,Func<GameItem,int> energy,Func<GameItem,GameItem,int,IPowerTransaction> plan,MachineMaterialContentsPolicy furnaceMaterials)
            =>(Owner,ItemId,Energy,Plan,FurnaceMaterials)=(owner,id,energy,plan,furnaceMaterials);
        // 不移除已交给在途事务的委托；即使作者在Debit中释放注册，仍必须调用原提供方Restore。
        public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
    }
    private static readonly Dictionary<string,Provider> Providers=new(StringComparer.Ordinal);
    private static bool _started;
    private static int _journals;
    public static bool IsAvailable=>GameSession.OnThread&&_started&&ExternalPowerRuntime.VerifyReady();
    public static IDisposable Register(string ownerId,string itemId,Func<GameItem,int> energy,
        Func<GameItem,GameItem,int,IPowerTransaction> plan)
        =>Register(ownerId,itemId,energy,plan,MachineMaterialContentsPolicy.Reject);
    /// <summary>登记外电及原版熔炉材料内装物策略。Reject保留旧版保护；ConsumeGraph显式允许整件材料及后代一起消耗。
    /// 已有产物的内装物始终保留，不受此策略控制。普通电池和EZapi配方各自的材料策略不变。</summary>
    public static IDisposable Register(string ownerId,string itemId,Func<GameItem,int> energy,
        Func<GameItem,GameItem,int,IPowerTransaction> plan,MachineMaterialContentsPolicy furnaceMaterials)
    {
        GameSession.RequireThread();MachineRecipeRules.Owner(ownerId);MachineRecipeRules.Id(itemId);
        ArgumentNullException.ThrowIfNull(energy);ArgumentNullException.ThrowIfNull(plan);
        if(!Enum.IsDefined(typeof(MachineMaterialContentsPolicy),furnaceMaterials))throw new ArgumentOutOfRangeException(nameof(furnaceMaterials));
        if(!_started)throw Error("NOT_READY","register after EZapi initialization");
        if(!itemId.StartsWith(ownerId+".",StringComparison.Ordinal)||itemId.Any(char.IsWhiteSpace))throw Error("ID","connector ID must belong to owner namespace");
        if(_journals!=0||Providers.Values.Any(p=>p.Calling))throw Error("REENTRANT","cannot register during a provider callback or open power transaction");
        if(Providers.TryGetValue(itemId,out var old)&&!old.Disposed)throw Error("DUPLICATE","connector already registered");
        if(Providers.Count(p=>!p.Value.Disposed)>=128)throw Error("LIMIT","max128 active providers");
        foreach(var key in Providers.Where(p=>p.Value.Disposed).Select(p=>p.Key).ToArray())Providers.Remove(key);
        var provider=new Provider(ownerId,itemId,energy,plan,furnaceMaterials);Providers[itemId]=provider;return provider;
    }
    /// <summary>仅精确identifier；不跟随fake ID、别名或标签。释放后不再是连接器。</summary>
    public static bool IsConnector(GameItem item)
    {
        GameSession.RequireThread();return Find(item) is not null;
    }
    /// <summary>连接器物品工厂中初始化原生电源准入标签。只接受未拥有、游离、无子窗口的新单件；失败须丢弃实例。</summary>
    public static void InitializeConnector(GameItem connector)
    {
        GameSession.RequireThread();
        if(!IsAvailable||connector is null||connector.Pointer==IntPtr.Zero||Find(connector) is null||connector.unitCount!=1||
            connector.parentInventory is not null||connector.contentWindow is not null||GeneralHelper.IsItemOwned(connector)||
            connector.state.dict.ContainsKey("EZAPI_EXTERNAL_CONNECTOR_V1"))
            throw Error("INITIALIZE","registered fresh detached unowned connector required");
        // 首次写入即标记，原生初始化部分失败时不能再次重置这个实例冒充原子成功。
        connector.state.GetTag("EZAPI_EXTERNAL_CONNECTOR_V1").Enable();
        ExternalPowerRuntime.Initialize(connector);
    }
    /// <summary>断开、回调异常、重入、钩子失效返回0；连接状态可用IsConnected区分。</summary>
    public static int GetEnergy(GameItem connector)
    {
        NativeAccess.RequireItem(connector);return Math.Max(0,Read(Find(connector),connector));
    }
    public static bool IsConnected(GameItem connector)
    {
        NativeAccess.RequireItem(connector);return Read(Find(connector),connector)>=0;
    }
    internal static Provider? Find(GameItem? item)=>_started&&item is not null&&item.Pointer!=IntPtr.Zero&&
        item.identifier is {} id&&Providers.TryGetValue(id,out var provider)&&!provider.Disposed?provider:null;
    internal static int Read(Provider? provider,GameItem connector)
    {
        if(provider is null||provider.Disposed||!IsAvailable)return -1;
        if(provider.Calling){provider.Reentered=true;return -1;}
        provider.Calling=true;provider.Reentered=false;
        try{var result=provider.Energy(connector);return provider.Reentered?-1:result;}catch(Exception e){ExternalPowerRuntime.Record("query",e);return -1;}
        finally{provider.Calling=false;}
    }
    internal static PowerJournal Open(GameItem machine,GameItem connector,int amount)=>new(machine,connector,amount,
        Find(connector)??throw Error("PROVIDER","connector registration unavailable"));
    internal static void Start(){_started=true;}
    internal static void Stop(){_started=false;foreach(var provider in Providers.Values)provider.Dispose();Providers.Clear();ExternalPowerRuntime.Stop();}
    internal static object Report()=>new{available=IsAvailable,openTransactions=_journals,providers=Providers.Values.Where(p=>!p.Disposed).Select(p=>new{ownerId=p.Owner,itemId=p.ItemId}).ToArray(),
        native=ExternalPowerRuntime.Status(),furnace=NativeFurnacePower.Report(),scope="EZapi synchronous batches and exact vanilla furnace callback; no asynchronous or crash recovery"};
    internal static InvalidOperationException Error(string code,string message)=>new("EZAPI_EXTERNAL_POWER_"+code+": "+message);

    /// <summary>本批独占提供方，保留原委托和周目身份。没有全局冻结绕过，也不调用原生SetPowerSourceAt补偿外电。</summary>
    internal sealed class PowerJournal:IDisposable
    {
        private readonly Provider _provider;private readonly GameItem _machine,_connector;private readonly GameRunIdentity _run;
        private readonly IntPtr _machinePointer,_connectorPointer,_parent;private readonly int _machineId,_connectorId,_amount,_before;
        private IPowerTransaction? _transaction;private bool _attempted,_disposed,_restoreCalled,_restored;
        internal PowerJournal(GameItem machine,GameItem connector,int amount,Provider provider)
        {
            _run=NativeAccess.RequireRun();NativeAccess.RequireItem(machine);NativeAccess.RequireItem(connector);
            if(amount<0||!IsAvailable||provider.Calling||_journals!=0)throw Error("REENTRANT","one synchronous external transaction at a time");
            (_provider,_machine,_connector,_amount)=(provider,machine,connector,amount);
            (_machinePointer,_connectorPointer,_machineId,_connectorId,_parent)=(machine.Pointer,connector.Pointer,machine.uniqueId,connector.uniqueId,connector.parentInventory?.Pointer??IntPtr.Zero);
            _before=Read(provider,connector);if(_before<amount||_before<0)throw Error("INSUFFICIENT","disconnected or insufficient external energy");
            _journals++;
            try {
                if(amount>0) {
                    _transaction=Invoke(()=>provider.Plan(machine,connector,amount))??throw Error("PLAN","null transaction");
                }
                if(!Current()||Read(provider,connector)!=_before)throw Error("PLAN","plan factory changed energy or registration");
            }catch{Dispose();throw;}
        }
        private bool Identity()=>NativeAccess.SameRun(_run)&&_machine.Pointer==_machinePointer&&_machine.uniqueId==_machineId&&
            _connector.Pointer==_connectorPointer&&_connector.uniqueId==_connectorId&&_connector.unitCount==1&&_connector.parentInventory?.Pointer==_parent;
        private bool Current()=>!_disposed&&Identity()&&ReferenceEquals(Find(_connector),_provider)&&IsAvailable;
        private T Invoke<T>(Func<T> action)
        {
            if(_provider.Calling)throw Error("REENTRANT","nested provider callback");
            _provider.Calling=true;_provider.Reentered=false;
            try{var result=action();if(_provider.Reentered)throw Error("REENTRANT","provider recursively queried its connector");return result;}finally{_provider.Calling=false;}
        }
        internal bool Validate()=>Current()&&Read(_provider,_connector)==_before&&(_amount==0||Invoke(_transaction!.Validate))&&Current()&&Read(_provider,_connector)==_before;
        internal void Debit()
        {
            if(_attempted||!Validate())throw Error("STALE","power journal reused or invalid");
            if(_amount==0)return;
            // Debit即使抛异常也可能已经写入，必须留下需要Restore的标记。
            _attempted=true;Invoke(()=>{_transaction!.Debit();return true;});
            if(!Verify())throw Error("VERIFY","external debit verification failed");
        }
        internal bool Verify()=>Current()&&(_amount==0||_attempted&&Invoke(_transaction!.Verify))&&Current()&&Read(_provider,_connector)==_before-_amount;
        internal bool Restore()
        {
            if(_restoreCalled)return _restored;_restoreCalled=true;
            if(!Identity())return false;
            if(!_attempted)return _restored=Read(_provider,_connector)==_before;
            // 注册释放不取消恢复责任；查询原提供方时允许已释放句柄，仅在相同周目/实例内调用。
            try {
                var restored=Invoke(_transaction!.Restore);
                var actual=Invoke(()=>_provider.Energy(_connector));
                return _restored=restored&&Identity()&&actual==_before;
            }catch(Exception e){ExternalPowerRuntime.Record("restore",e);return false;}
        }
        internal bool IsRestored=>_restored&&Identity()&&Invoke(()=>_provider.Energy(_connector))==_before;
        internal void Fault(string reason)
        {
            if(_transaction is null)return;
            try{Invoke(()=>{_transaction.Fault(reason);return true;});}catch(Exception e){ExternalPowerRuntime.Record("fault",e);}
        }
        public void Dispose(){if(_disposed)return;_disposed=true;_journals--;}
    }
}

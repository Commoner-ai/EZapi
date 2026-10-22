using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>独立外电连接器示例。调用者实现查找规则；不缓存跨场景原生实例，不把此示例自动装进游戏。</summary>
public static class ExternalPowerExamples
{
    private const string Owner="author.power",ConnectorId=Owner+".connector";
    /// <summary>初始化时声明；先登记提供方再允许创建。每次由游戏工厂建立新的未拥有实例，重载时同样重建。</summary>
    public static ContentSubmissionResult RegisterConnectorItem()
        =>ItemApi.RegisterItem(Owner,ConnectorId,()=>{
            var item=ItemDirectory.CreateEmptyItem(ConnectorId);
            try {
                item.SetShape(new GridShapeBuilder(1,1).SetDataFill(1).Build());item.unitValue=10;
                GeneralHelper.SetItemOwned(item,false);ExternalPowerApi.InitializeConnector(item);return item;
            }catch{item.Destroy();throw;}
        },new NativeItemOptions("外部电力连接器","连接到作者提供的外部电池。",SourceName:"外电示例"));
    /// <summary>
    /// 初始化时登记并持有返回句柄，停用时释放。resolveSource每次按当前连接器查找真实单件原生电池。
    /// 连接器物品自身须另行登记，在其工厂中初始化原生电源标签；能量来自外部电池，不能与连接器为同一实例。
    /// </summary>
    public static IDisposable Register(Func<GameItem,GameItem?> resolveSource)
    {
        ArgumentNullException.ThrowIfNull(resolveSource);
        return ExternalPowerApi.Register(Owner,ConnectorId,connector=>{
            var source=resolveSource(connector);
            return source is null||source.Pointer==connector.Pointer||source.unitCount!=1||ExternalPowerApi.IsConnector(source)?-1:Power.GetEnergy(source);
        },(_,connector,amount)=>new BatteryTransaction(resolveSource,connector,amount));
    }
    private sealed class BatteryTransaction:IPowerTransaction
    {
        private readonly Func<GameItem,GameItem?> _resolve;private readonly GameItem _connector,_source;
        private readonly IntPtr _pointer,_parent;private readonly int _instance,_before,_amount;private readonly RunDataStore _run;
        private bool _attempted;
        internal BatteryTransaction(Func<GameItem,GameItem?> resolve,GameItem connector,int amount)
        {
            _resolve=resolve;_connector=connector;_source=resolve(connector)??throw new InvalidOperationException("external source disconnected");
            if(_source.Pointer==connector.Pointer||_source.unitCount!=1||ExternalPowerApi.IsConnector(_source))throw new InvalidOperationException("one separate native source required");
            (_pointer,_instance,_parent)=(_source.Pointer,_source.uniqueId,_source.parentInventory?.Pointer??IntPtr.Zero);
            (_before,_amount,_run)=(Power.GetEnergy(_source),amount,RunData.Open(Owner));
        }
        private bool Current()=>_run.IsCurrent&&_resolve(_connector)?.Pointer==_pointer&&_source.uniqueId==_instance&&
            _source.unitCount==1&&(_source.parentInventory?.Pointer??IntPtr.Zero)==_parent;
        public bool Validate()=>Current()&&Power.GetEnergy(_source)==_before&&PowerHelper.CanDrawPowerSource(_source,_amount);
        public void Debit()
        {
            if(_attempted||!Validate())throw new InvalidOperationException("stale or reused power transaction");
            _attempted=true; // Draw抛异常也可能已扣电，因此先记尝试。
            if(!PowerHelper.DrawPowerSource(_source,_amount))throw new InvalidOperationException("native source refused debit");
        }
        public bool Verify()=>Current()&&_attempted&&Power.GetEnergy(_source)==_before-_amount;
        public bool Restore()
        {
            if(!Current())return false;
            if(_attempted&&Power.GetEnergy(_source)!=_before)PowerHelper.SetPowerSourceAt(_source,_before);
            return Current()&&Power.GetEnergy(_source)==_before;
        }
        // Fault只能诊断/隔离，不补发产物、不继续扣电、不自动重试。
        public void Fault(string reason)=>MelonLoader.MelonLogger.Error("[author.power] external source requires manual recovery: "+reason);
    }
}

using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>DLL扩展例，显式由内容作者调用；默认JSON包不自动执行加工。</summary>
public static class MachineTransactionExamples
{
    /// <summary>
    /// 作者的事务必须拥有材料/液体/电力与产物交付的完整补偿实现。不是空事务示例，必须传入实际实现。
    /// transaction.Validate须核对空间/冻结/原生资格，Apply只执行一次，Verify读回差额；Restore恢复部分完成并Expel新产品。
    /// </summary>
    public static MachineTransactionResult Produce(GameItem machine,IReadOnlyList<MachineItemTake> materials,
        IMachineBatchTransaction transaction)
    {
        var batch=new MachineBatchContext(machine,materials,new Dictionary<string,GameItem>(),1) {ProductionMarkupPercent=20};
        return MachineTransactionApi.Run("ezapi.sample",batch,
            new[]{new MachineProductDefinition("ezapi.sample.field_kit",Prepare:(context,product)=>
                ProductionValueApi.SetBasis(product,ProductionValueApi.Calculate(context,20)))},transaction);
    }
}

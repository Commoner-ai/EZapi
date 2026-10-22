using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>手动同步调用示例；默认JSON包不会执行，不生成/保存物品。</summary>
public static class LiquidTransferExamples
{
    /// <summary>先由调用者确认本次选中的真实源/目标；只在当前周目主线程使用。</summary>
    public static LiquidTransferResult TransferOneMillilitre(GameItem source,GameItem target,Func<bool> stillAllowed)
        => LiquidTransferApi.Pour(source,target,1000,stillAllowed);

    /// <summary>作者明确知道整批价值时设置；totalValue/totalQuality是整批合计，不是每ml。</summary>
    public static TransactionResult AnnotateKnownBatch(GameItem container,decimal totalValue,decimal totalQuality)
    {
        if(totalValue<0||totalQuality<0)throw new ArgumentOutOfRangeException(nameof(totalValue));
        var actual=MachineLiquidSnapshot.From(Liquids.Capture(container));
        if(actual.TotalParts==0&&(totalValue>0||totalQuality>0))throw new ArgumentException("空瓶不能携带正液体价值。");
        // 使用差量分配最后一条，避免decimal舍入后各成分合计偏离作者给定总值。
        var last=actual.Contents.ToList().FindLastIndex(p=>p.Parts>0);
        decimal valueLeft=totalValue,qualityLeft=totalQuality;
        var parts=actual.Contents.Select((p,index)=>{
            var ratio=actual.TotalParts==0?0m:p.Parts/(decimal)actual.TotalParts;
            var value=index==last?valueLeft:totalValue*ratio;
            var quality=index==last?qualityLeft:totalQuality*ratio;
            valueLeft-=value;qualityLeft-=quality;
            return p with {Value=value,QualityBasis=quality};
        }).ToArray();
        return Liquids.SetProductionMetadata(container,new(actual.CapacityParts,parts));
    }

    /// <summary>只算消耗总价值，不操作原生容器。</summary>
    public static decimal ConsumedValue(MachineLiquidSnapshot before,MachineLiquidSnapshot after)
        =>LiquidCompositionMath.BaseValue(LiquidCompositionMath.Consumed(before,after));
}

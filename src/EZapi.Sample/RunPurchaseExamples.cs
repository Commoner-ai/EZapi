using Il2Cpp;
namespace EZapi.Sample;

/// <summary>同步业务示例。交付资格和幂等由作者掌握；重开游戏后重新定位库存和物品。</summary>
public static class RunPurchaseExamples
{
    /// <summary>将已经准备在来源库存中的单件商品交给目标库存，失败时核对退回原来源。</summary>
    public static TransactionResult BuyOne(int cost,GameItem item,GameGridInventory source,GameGridInventory destination)
    {
        var pointer=item.Pointer;var id=item.uniqueId;
        bool Same()=>item.Pointer==pointer&&item.uniqueId==id&&item.unitCount==1&&InventoryApi.IsPlayerOwned(item);
        bool At(GameGridInventory inventory)=>Same()&&item.parentInventory?.Pointer==inventory.Pointer;
        return RunPurchaseApi.Purchase(cost,new[]{new TransactionStep("delivery",
            ()=>At(source)&&InventoryApi.PreviewWholeGridTransfer(item,source,destination).CanTransfer,
            ()=>{var moved=ItemTransferApi.Move(item,destination,1);return moved.Moved==1&&!moved.Indeterminate&&At(destination);},
            ()=>{
                if(At(source))return true;
                if(!At(destination))return false;
                var moved=ItemTransferApi.Move(item,source,1);return moved.Moved==1&&!moved.Indeterminate&&At(source);
            })});
    }
}

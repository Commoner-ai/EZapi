using Il2Cpp;
using UnityEngine;
using MelonLoader;
namespace EZapi;

/// <summary>调试生成专用；不参与目录注册或 tooltip。每次重新获取当前库存，不自动保存。</summary>
internal static class DebugItemSpawner
{
    internal static bool TrySpawnDebug(string itemId, int expectedSlot, string expectedRun, out string message, bool emptyLiquid=false)
    {
        message = "未知物品";
        if (!ItemRegistry.TryGet(itemId,out var definition)) return false;
        if(emptyLiquid&&definition.LiquidContainer is null){message="只有液体容器支持生成空瓶";return false;}
        if (ItemRegistry.GetStatus(itemId) != "Applied") { message = "物品尚未成功应用到游戏目录"; return false; }
        GameItem? item = null;
        try
        {
            var store = PlayerStore.instance;
            // PlayerStore.gridInv 不是当前店铺窗口绑定的主库存；必须取界面的真实目标。
            // 每次点击重新获取，禁止把场景库存缓存到下一次读档或切场景。
            var visible = VisibleStoreInventory.TryGet(out var inventory);
            if (store is null || !visible)
            { message = "请先进入店铺并显示主库存"; return false; }
            if (expectedSlot < 0 || string.IsNullOrEmpty(expectedRun) || store.saveSlotId != expectedSlot || store.runID != expectedRun)
            { message = "存档会话已变化，请重新点击生成"; return false; }
            if (ItemMouseDragHandler.current?.IsDraggingItem == true)
            { message = "请先放下正在拖拽的物品"; return false; }
            item = DirectoryMaster.Item(itemId, true);
            if (item is null || item.Pointer == IntPtr.Zero || item.identifier != itemId || item.parentInventory is not null)
            {
                message = "游戏目录未返回有效物品实例";
                return false;
            }
            GeneralHelper.SetItemOwned(item, true);
            item.SetUnitCount(1);
            // 仅处理本次新建、尚未放置的实例，绝不清空玩家已有容器。
            if(emptyLiquid) {
                WaterHelper.EmptyContainer(item);
                if(!Liquids.IsContainer(item)||Liquids.Capture(item).TotalParts!=0)
                    throw new InvalidOperationException("EZAPI_DEBUG_EMPTY_LIQUID: native empty operation was not confirmed.");
            }
            var beforeCount = inventory.childItems.Count;
            var bounds = inventory.inventoryShape;
            if (bounds is null || bounds.width <= 0 || bounds.height <= 0 || (long)bounds.width * bounds.height > 4096)
            {
                message = "主背包尚未生成有效网格";
                return false;
            }
            var shape = new GridShapeBuilder(item.modifiedShape);
            SlotMarker? marker = null;
            for (var y = bounds.minY; y <= bounds.maxY && marker is null; y++)
                for (var x = bounds.minX; x <= bounds.maxX; x++)
                {
                    shape.SetPosition(x, y);
                    var candidate = inventory.TryInventorySlot(item, item.unitCount,
                        new Vector2(float.NaN, float.NaN), shape.shape, null);
                    if (candidate is not null && candidate.IsValid() && candidate.inventory?.Pointer == inventory.Pointer &&
                        candidate.targetItem is null && candidate.numTransfer >= item.unitCount)
                    { marker = candidate; break; }
                }
            if (marker is null)
            {
                message = "主背包没有可用格子";
                return false;
            }
            try { marker.TryAcceptOnce(item.unitCount); }
            catch when (item.parentInventory?.Pointer == inventory.Pointer) { }
            var present = false;
            foreach (var stored in inventory.childItems)
                if (stored.Pointer == item.Pointer) { present = true; break; }
            if (item.parentInventory?.Pointer != inventory.Pointer || !present || inventory.childItems.Count != beforeCount + 1)
            {
                message = "游戏未确认物品已放入主背包";
                return false;
            }
            // 原生接受已经成功，刷新呈现失败也不能重发或销毁已放入的实例。
            try { inventory.Validate(); }
            catch (Exception e) { MelonLogger.Warning("[EZapi] Debug inventory refresh: " + e); }
            MelonLogger.Msg("[EZapi] Debug spawned " + itemId + "; slot=" + store!.saveSlotId + "; parent verified=True; target=EmporiumEntry.invElement/" + inventory.identifier +
                "; before=" + beforeCount + "; count=" + inventory.childItems.Count + "; emptyLiquid="+emptyLiquid+"; no automatic save");
            message = "已放入屏幕底部店铺主库存（未自动保存）";
            return true;
        }
        catch (Exception e)
        {
            MelonLogger.Error("[EZapi] EZAPI_DEBUG_SPAWN_FAILED " + itemId + ": " + e);
            message = "生成失败：" + e.Message;
            return false;
        }
        finally
        {
            // 只销毁本次创建且从未落入容器的实例。原生提交抛错也不能销毁已放入的物品。
            if (item is not null && item.Pointer != IntPtr.Zero)
            {
                try { if (item.parentInventory is null) item.Destroy(); }
                catch (Exception e) { MelonLogger.Error("[EZapi] Debug cleanup: " + e); }
            }
        }
    }
}

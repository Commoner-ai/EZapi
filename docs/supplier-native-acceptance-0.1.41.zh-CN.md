# SupplierApi 0.1.41 实机验收记录

验收日期：2026-10-21。游戏：Probably Stolen 0.46D；加载器：MelonLoader IL2CPP .NET 6；可丢弃存档槽：533；本次进程仅加载 EZapi 0.1.41。

## 操作与结果

第 2 天测试访客到达并显示 `officer_lun` 肖像。逐步打开报价预览、返回、确认报价、继续刷新至第 2 页，然后离开供应商对话，流程完成。最终诊断状态为 `Closed`，`queued=0`、`arrived=1`、`active=0`；`captureCalls=2`、`purchaseCalls=1`、`dismissCalls=1`，`errors=[]`。

确认回调只增加测试诊断计数，没有扣款、发放物品、修改库存或写入存档。供应商操作前后余额均为 530；此前从第 1 天的 535 变为 530 是夜间 5 币电费结算，不是供应商扣款。因此这份记录只证明访客排程、肖像、EZapi 报价对话和离场流程，不证明真实物品交易。

## 指纹与证据

- 游戏程序集 `Assembly-CSharp.dll` SHA-256：`E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF`
- 安装并加载的 `EZapi.dll` SHA-256：`A2620447D65E9E1ACFB51AD373D90FF2C8F19887B8B12DE14AD2B890B9E61447`
- 最终诊断中的供应商请求：`ezapi.debug.supplier.request.1`；存档槽 533；周目 `1e854590-f7cb-4e77-85af-d5c487542717`；肖像键 `officer_lun`。
- 证据目录：发布包内 `verification/native-test-0.1.41/`，包含归档的 `Latest.log`、`diagnostics.json`、安装记录和操作截图。
- 关键截图：`queue-test-supplier.png`、`inspect-supplier-preview-unhover.png`、`supplier-result-buttons-visible.png`、`supplier-refresh-result-unhover.png`、`read-final-supplier-state.png`。

## 验收边界

没有测试真实扣款、库存变更、发货、补偿、原生货架商品、`LootRegistry` 随机供应商掉落或保存重载。上述交易责任仍由内容 Mod 实现并单独验收。

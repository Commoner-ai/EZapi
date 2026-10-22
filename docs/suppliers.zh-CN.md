# SupplierApi：供应商与动态报价

`SupplierApi` 用官方 `ModHook.OnGenerateCustomerVeryLate` 把一个对话型供应商排入后续顾客队列。官方模组指南把顾客生成钩子列为首选扩展入口，并说明顾客生成顺序是 `VeryEarly → Early → Normal → Late → VeryLate`；指南没有记录独立“供应商注册”API，且将修改原生商店行为/内容列为暂不支持。本机当前游戏程序集确认 `StoreClient` 构造函数和 `StoreClientManager.AddNextClient(StoreClient)` 可用，所以 EZapi 创建独立 `DIALOGUE` 意图访客。它不会修改原生货架、供应商掉落表或采购流程。需要让原生供应商随机出售模组物品时，请使用 `LootTables`/官方 `LootRegistry` 掉落表，而非此对话供应商 API。

示例见 [`SupplierExamples.cs`](../src/EZapi.Sample/SupplierExamples.cs)。供应商 ID、报价 ID 和请求 ID 都必须使用模组自己的命名空间。队列、状态、回调和对话图只在当前进程与存档载入 epoch 有效；EZapi 不自动保存，也不自动改变库存或金钱。

```csharp
var result = SupplierApi.Queue(
    ownerId: "author.vendor",
    requestId: "author.vendor.visit.day_1",
    runId: context.Run.RunId,
    slotId: context.Run.SlotId,
    actorId: "author.vendor.stockist",
    spriteName: "stockist",
    definition: SupplierExamples.Definition);

if (result.Status != PresentationStatus.Queued)
    MelonLogger.Warning(result.Reason);
```

`actorId` 是访客实例身份，`spriteName` 是已经载入的模组顾客肖像键，不含路径和扩展名。Supplier 使用独立 `StoreClient`，原生意图为 `DIALOGUE`，并关闭常规出售、议价和多买行为。框架把它送入官方顾客队列；到访时由 EZapi 对话运行时显示菜单。该入口不依赖原生客户蓝图交易规则，也不把 Mod 商品注入普通商店。

`SupplierDefinition.Capture` 在入队时生成首个菜单快照；玩家选择“继续交易”后会再次调用，以便按当前周目状态刷新报价。每项 `SupplierOffer.Purchase` 只在该报价的确认按钮被选择后执行一次。再次刷新会创建新的对话执行寿命，因此需要限制重复购买时，模组应在回调中重新检查余额、库存、资格或已领取状态，并完成自己的扣款、发货和补偿。

`AllowMenuRefresh` 与 `CanContinue` 同时允许时，确认结果页显示 `ContinueLabel`。`RequireConfirmation` 控制报价是否先经过预览页；报价自己的 `ConfirmLabel` 可覆盖默认按钮文字。分页由对话图自动处理。空报价页仍提供离开按钮。

当 `SupplierPage.AutomaticOfferId` 指向当前报价时，EZapi 只在该按钮确实显示时开始计时；达到 `AutomaticDelaySeconds` 后，会通过原生选项选择流程选择该报价。需要确认的自动报价只会打开预览，仍须玩家确认。玩家切换到别的对话页或按钮被界面遮住时，计时会重新开始。

`Dismiss` 在玩家离开供应商对话或原生访客离场时回调。`SupplierApi.Status` 返回当前请求状态；`Queued` 代表顾客队列读回成功，`Arrived` 代表到访回调触发，不代表交易完成。入队失败、载入/场景切换和框架关闭不会调用 `Dismiss`。

### 原生验收夹具

在“EZapi 调试”面板点击“排入测试供应商”，框架会从当前已加载的原生顾客队列读取一个有效肖像键，再排入一位测试访客。测试报价的确认回调只增加诊断计数，不扣钱、不发物品、不改库存，也不写存档；玩家可在原生报价预览中返回、确认、继续刷新并离开。再次打开调试面板点击“读取供应商状态”，或导出诊断后查看 `supplierDebug` 中的 `status`、`captureCalls`、`purchaseCalls`、`dismissCalls` 与 `spriteName`。请在可丢弃测试档中运行。

### 0.1.41 游戏内验收（2026-10-21）

在 Probably Stolen 0.46D 的可丢弃存档槽 533 中，只加载 EZapi 0.1.41 完成完整访客流程。第 2 天测试访客到达并显示肖像；报价预览、返回、确认、继续刷新至第 2 页和离场均成功。游戏诊断记录 `status=Closed`、`queued=0`、`arrived=1`、`active=0`，回调计数为 `captureCalls=2`、`purchaseCalls=1`、`dismissCalls=1`，`errors=[]`。确认回调只增加测试计数；本次未扣款、发放物品、修改库存或写入存档。供应商操作前后余额均为 530；前一晚的 535→530 是 5 币电费结算。

本次验证的是供应商访客和 EZapi 对话/报价界面的运行链，不是实际物品交易。真实扣款、库存、发货和补偿仍由 Mod 作者实现并单独验收；也没有验证原生货架、`LootRegistry` 随机供应商掉落或保存重载。

运行指纹：游戏 `Assembly-CSharp.dll` SHA-256 `E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF`；已安装 `EZapi.dll` SHA-256 `A2620447D65E9E1ACFB51AD373D90FF2C8F19887B8B12DE14AD2B890B9E61447`。完整日志、最终诊断与截图位于本次交付包 `verification/native-test-0.1.41/`；验收记录见 [`supplier-native-acceptance-0.1.41.zh-CN.md`](supplier-native-acceptance-0.1.41.zh-CN.md)。官方文档与原生接口指纹见 [`official-sources.index.json`](official-sources.index.json)。

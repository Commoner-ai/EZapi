# 物品交付凭据与对账

当前版本以 release-status.json 为准；当前 DLL 原生交付、跨夜和保存重载待验收。离线检查不等于实机通过。准确状态见 [release-status.json](release-status.json)。[English](delivery-receipts.en.md) · [机器索引](delivery-receipts.index.json) · [独立调用示例](examples/delivery-receipt-dll/ReceiptDelivery.cs)。

礼包、订单、一次性奖励可以共享同一条流程：在调用方账本记录稳定操作编号，为待交付物品写凭据，交付后重新扫描库存对账。EZapi 提供标记和观察；物品工厂、目标选择、原生接纳、支付、账本与保存由调用 Mod 负责。

## 最小恢复调用

```csharp
var receipt = new DeliveryReceipt(savedOperationId, "YOUR_ITEM_ID", Units: 1);
var scan = ItemReceiptApi.Capture("author.mod", "delivery-receipts");
var result = DeliveryReceiptAudit.Reconcile(scan, receipt, savedLedgerState);
```

`savedOperationId` 在发放前生成一次，例如 `Guid.NewGuid().ToString("N")`，并随调用方账本保存；读档、空间不足和异常后继续使用同一编号。不要每次点击生成新编号绕过去重。`ownerId` 是小写 Mod ID，`key` 是 1..120 位 ASCII 字母、数字、点、横线或下划线，专用于本功能。框架以 `ItemDataApi` 的命名空间隔离标签；不要把这个 key 与其他 JSON 结构混用。

| 账本状态 / 当前观察 | 决策 | 调用方动作 |
| --- | --- | --- |
| Pending，完整扫描未找到编号 | CanAttempt | 仅表示预检允许准备；Pending 必须确实从未尝试原生交付 |
| Pending 或 InFlight，恰好一个编号、ID/数量匹配、玩家拥有且在库存中 | Observed | 可比较暂存账本为 Delivered；账本提交不明时保留 InFlight |
| InFlight，没有凭据 | Indeterminate | 停止自动补发，人工或业务流程对账 |
| 重复编号、货物/数量错误、无主或仍是游离物品 | Conflict | 停止并报告冲突；不合并数量掩盖重复 |
| Delivered，包括已售出、消耗或暂时无法扫描 | Recorded | 账本优先，不能重新发奖；不对当前库存作有效性承诺 |
| 扫描失败或输入损坏 | Indeterminate / Invalid | 不当作“空库存” |

Pending、InFlight、Delivered 是**调用方账本**的含义，不是 EZapi 自动记录的结果。保存槽、周目和比较提交可复用 [RunDataApi](run-data-records.zh-CN.md)。`Stage` 只是内存暂存；物品标签与账本也不是磁盘事务。必须通过游戏正式保存完成与冷启动读回，才能声明持久交付完成。

## 单次交付顺序

1. 读取调用方账本。Delivered 直接结束；InFlight 只恢复对账，不创建第二件物品。
2. Pending 才创建调用方拥有的游离物品。设置好数量、普通/赃物状态和其他物品数据，再调用 `ItemReceiptApi.StampDetached`。只有 `Present` 或 `StagedInMemory` 且有 `Stamp` 才继续。不同旧凭据、旧版本或损坏标签会保留并拒绝覆盖。
3. 预检一个能容纳整件/整堆的独立空位，不合堆。取得交付前 `Capture` 快照。将账本比较暂存为 InFlight 并读回，写前再次核对周目、库存和准入。
4. 设置调用方的 `attempted=true`，再调用**恰好一次**原生交付。`SlotMarker.TryAcceptOnce` 与 `GraphUtils.EmporiumTryAdd` 的路径不同，需核对实际游戏构建的语义。不要先手动清父图或 `Expel`。
5. 从调用原生的那一刻起，**不要再读取、销毁、回滚或复用旧物品包装对象**。原生失败或抛异常也可能已经销毁对象。只通过新的 `Capture` 和托管 `Stamp` 调用 `CheckHandoff`。
6. `Observed` 表示同载入内恰好新增那一件带凭据的玩家物品，且已有观察字段未变；可继续比较提交账本。其它结果保留待核对状态。即使前后库存完全不变，也不能仅凭此将 InFlight 重置为 Pending。

独立示例展示整件空位的 `TryAcceptOnce`。返回 `NativeHandoffAttempted=true` 后，调用方绝不能在 `finally` 中访问或销毁旧物品。返回 false 也不能无条件销毁：先确认对象仍归调用方所有，且没有被其他回调交付。示例不自动清理对象、不补发、不请求保存。原生返回值或异常可以写入诊断，交付事实以新扫描为准。

## 两种对账的边界

`CheckHandoff(before, after, stamp, requiredInventoryIdentity)` 用于同一载入内的写后差额核对，比较操作编号、原生实例身份、游戏实例编号、物品 ID、数量、归属和父库存，并要求所有原有观察记录一致。目标参数不传时允许其它玩家库存，传入时必须正好进入该库存。原生指针只作同 Epoch 比较，不能序列化或转换回对象。

`Reconcile` 用于读档后的业务恢复，只用持久操作编号和物品内容，不要求沿用旧指针或旧 Epoch。它观察当前物品，不能证明曾在哪天交付，也不能证明标签没有被其它 Mod 复制。凭据是协作防重复机制，不是防作弊或加密签名。

快照只覆盖所列字段，不检查几何坐标、锁、物品全部标签、容器内容正确性、金钱或磁盘存档。空容器可整件核对；带子物品的奖励可能新增多项记录，`CheckHandoff` 会保守拒绝“只新增一件”的结论。拆堆、合堆、拆分交付和批量奖励应由调用方拆成独立稳定编号并设计自己的账本，不要把不匹配强制解释为成功。

## 线程、限额、语言

`StampDetached` / `Capture` 需要已初始化的游戏主线程、当前载入周目和已支持的游戏构建；写标签还要求冻结门控可用、物品未被冻结且没有拖动。扫描复用当前物品图的有界遍历，并新扫描两遍比较，最多 4096 件物品、每遍 1 MiB 凭据 JSON 字符总量；超限或任何坏标签均不返回部分成功。不在每帧 UI 或 `OnGUI` 中扫描。

`DeliveryReceiptAudit` 是纯托管分析，可以用于离线和工作线程；输入应是稳定、完整的快照，分析时不可修改集合。中文和英文使用相同编号、枚举、数量与错误码；玩家提示通过 `GameLanguage.Resolve` 根据游戏语言生成，不能用本地化物品名称当作身份。

当前源码链接测试使用显式原生边界替身，覆盖重复、错误数量/归属、换档、空扫描、重入、限额、标签写后不明和旧对象失效。真实接纳、其他 Mod、次日保存及冷启动恢复仍待当前 DLL 实机验收。

带内容容器请用 [DeliveryGraphAudit 整图核验](delivery-graphs.zh-CN.md)，不要放宽单件接口的规则。

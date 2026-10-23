# 带内容容器的交付核验

当前版本和验收状态以 [release-status.json](release-status.json) 为准；当前 DLL 原生接纳、跨夜与保存重载待验收。[English](delivery-graphs.en.md) · [机器索引](delivery-graphs.index.json) · [可编译示例](examples/delivery-graph-dll/GraphDelivery.cs)。

装有子物品的物资箱进入库存时会新增多件物品，原来的 [DeliveryReceiptAudit.CheckHandoff](delivery-receipts.zh-CN.md) 只允许新增一件。现在可用 `ItemGraphReceiptApi` 复制完整观察图，再用纯托管 `DeliveryGraphAudit` 比较根物品、子物品和库存边。这套接口不执行接纳、支付、账本更新或保存。

```csharp
// receipt 的 OperationId 由调用方提前生成并保存，异常/读档后沿用同一编号。
var tagged = ItemReceiptApi.StampDetached(crate, "author.mod", "receipts", receipt);
if (tagged.Stamp is null) return;
var detached = ItemGraphReceiptApi.CaptureDetached(crate, "author.mod", "receipts");
var before = ItemGraphReceiptApi.CaptureRun("author.mod", "receipts");
// 调用方预检整件空位、比较暂存 InFlight，并复核两份图后，只执行一次原生接纳。
// 原生接纳开始后，不再访问 crate、子物品或旧 SlotMarker。
var after = ItemGraphReceiptApi.CaptureRun("author.mod", "receipts");
var audit = DeliveryGraphAudit.CheckHandoff(before, after, detached, targetIdentity);
```

上述片段展示观察顺序，实际原生接纳与账本门控见[完整示例](examples/delivery-graph-dll/GraphDelivery.cs)。不可省掉门控后直接复制发奖。

## 单次交付与恢复

1. 读取同存档槽、同周目的调用方账本。Delivered 直接结束；InFlight 只恢复对账。Pending 必须确实从未尝试接纳，才能准备物品。
2. 通过[预制工厂](prepared-items.zh-CN.md)或已核对的原生路径创建物品，完成所有内容设置后，用 `StampDetached` 标记玩家拥有的游离根物品。`CaptureDetached` 要求根无父节点，子图闭合，根凭据唯一且 ID/数量匹配；不会填充、修复或改写子物品。
3. `CaptureRun` 扫描当前周目物品图，包括主库存和可保存袋库存，保留空目标库存。查找不合堆的整件空位；调用方比较暂存并读回 InFlight 后，再核对周目、准入和两份图未变。
4. 恰好一次原生接纳。开始调用后，即使原生抛错或返回失败，也不再读、销毁或复用旧根、子物品与标记对象。重新 `CaptureRun` 后，调用 `CheckHandoff`。
5. 只有 Observed 才确认本次观察范围内的整图交付；再比较暂存 Delivered。其他结果保留待核对状态，不自动补发、清理或重置 Pending。框架不会请求存档。

`CheckHandoff` 要求前、后、游离图的槽/周目/Epoch 和命名空间一致；游离图不能借用原图节点。写后必须恰好新增该图，原有节点/物品不变，只有既有目标库存新增根边，根恰好进入该目标。省略目标参数时允许任一被原图观察到的库存；传入 0 是无效输入。内部边、原生身份、物品 ID、数量、实例编号、归属、父库存和该命名空间凭据均须匹配。

原生工厂的未拥有子物品可能共享实例编号 0，框架保留这一状态。默认不允许子物品归属变化。只有明确传 `allowChildOwnershipInitialization: true`，才允许子物品从未拥有变为拥有并分配实例编号；仍检查唯一拥有编号、指针/ID/数量/父库存/凭据/内部边。该选项不会允许根物品归属变化或内容变化。

`Reconcile(snapshot, receipt, ledgerState)` 沿用单件凭据的决策：Delivered→Recorded；Pending 且完整扫描未找到→CanAttempt；InFlight 且缺失→Indeterminate；重复或不匹配→Conflict；当前库存中存在唯一正确根凭据→Observed。**读档恢复仅确认当前根凭据，不证明原来的子物品内容。** 需要冷启动核对原始货物时，调用方须另存稳定的预期内容清单并核对；不要持久化原生指针或 Epoch 作为跨载入身份。

## 范围、性能和语言

原生捕获仅在已初始化的游戏主线程、当前已载入周目和支持构建下执行；扫描两遍并比较，变化、坏标签、异常或超限均不返回部分结果。上限为 8192 节点、4096 物品、16384 条父子引用合计和每遍 1 MiB 凭据 JSON 字符。不要在每帧 UI 中扫描。

纯托管分析可以离线/工作线程执行，输入须完整且分析期间不变。节点数字仅为同 Epoch 比较身份，不能转换回对象。快照不覆盖几何位置、锁、其他标签、品质/热度、金钱、所有游戏状态或磁盘存档。协作式快照/凭据不是可信签名；其他 Mod 仍可能复制或改写它们。拆堆、合堆、部分转移、批量独立奖励或实际图拓扑不符合上述契约时，保留不确定结果，不绕过检查。

ID、错误码和枚举不随语言改变；玩家文案用 `GameLanguage.Resolve` 随游戏语言选择。离线用例覆盖带内容图、损坏/重复/缺失、归属初始化、换档、限额、重入和旧对象失效；它们不是原生库存接纳、子图保存或混合 Mod 的验收。

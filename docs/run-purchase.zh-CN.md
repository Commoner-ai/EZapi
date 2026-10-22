> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 周目购买事务

`RunPurchaseApi.Purchase(cost, steps)` 在当前已载入周目扣除现金，并执行同步交付步骤；另一个重载接受 `PlayerStore`，必须与当前周目的原生实例完全相同。可编译的单件库存交付示例见 `../src/EZapi.Sample/RunPurchaseExamples.cs`。精确重载见 `API.index.zh-CN.json`。

```csharp
var result = RunPurchaseApi.Purchase(7, new[] {
    new TransactionStep("delivery", CanDeliver, DeliverAndVerify, RestoreAndVerify)
});
if (result.Committed) { /* 本次付款和交付已同步核对 */ }
```

cost 为非负整数现金单位；0 费用不调用原生扣款，仍检查交付。允许 0..128 个唯一命名的作者步骤；空列表表示仅付款。`ezapi.purchase.cash` 和 `ezapi.purchase.verify` 为保留名称。参数错误抛异常；初始化主线程/当前周目以外不允许调用。显式 store 不匹配返回 Rejected。余额不足或负余额在作者 Check 执行前拒绝，不调用 Apply。

框架复用 `Transactions`：先执行所有只读 Check，付款后逐步复核，再执行 Apply，末尾核对现金。作者 Check 必须只读；Apply 返回 true 前核对确切交付，部分完成或异常时 Restore 必须恢复自己已写入的部分并核对。作者步骤不可自行修改现金、触发保存、切换场景、读档或启动异步操作。框架使用当前游戏 `GetCash`、`PayMoney(cost,false)`、`ModCash(cost,false)`，无全局资金补丁，不直接覆盖现金字段。

成功付款要求 GetCash、原始 playerCash、周目、实例身份和预期差额一致。作者步骤前后均复核。付款完成后交付失败，先逆序恢复已尝试的作者步骤，包括抛异常的当前步骤；全部作者恢复得到明确 true 且现金仍为准确扣款后余额时，才归还本次付款并核对。原生付款抛错后只有确认完整扣款才能补偿；未写入时确认原余额，部分扣款或不明差额不会猜测退款。

`Committed` 仅表示本次同步核对成功。`Rejected` 为写入前拒绝；`Restored` 表示所有本次尝试的写入均确认恢复。交付恢复 false/抛异常、现金出现不明变化、周目或原生实例失效时返回 `RecoveryFailed`；`Restored=false`，保留具体 FailedStep、AppliedSteps、RecoveryFailures 和 Error。作者步骤恢复失败时不会自动退款，以免商品仍被保留又退钱。仍会尝试恢复其它更早的作者步骤，但不会在新周目调用旧业务 Restore 或写旧现金。此时停止自动重复该购买，先核对回执和实际业务状态。

购买和通用事务共用排他执行机制；购买嵌套购买、通用事务中购买、购买中通用事务均返回 Reentrant，内层不会扣款。它不是存档事务，不提供跨进程原子性、自动保存、跨夜补偿、网络支付、通知幂等或自动重试。作者需要使用自己的稳定业务键防止玩家重复购买；不能把 GameRunIdentity.Epoch 当持久业务 ID。运行时只核对现金和作者声明的步骤结果，不推断作者 Restore 返回 true 是否诚实。

“购买事务检查”按钮仅用于可丢弃测试档；诊断为 runPurchase/runPurchaseDebug。夹具使用真实现金和临时原生源/目标库存，记录产品接口结果后显式移除自己创建的物品、归还核对过的测试差额。这个测试清理不是产品接口隐藏的自动退款行为。框架不把测试物品交付玩家主库存，不主动保存。

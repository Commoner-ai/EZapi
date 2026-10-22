# EZapi 0.1.10：事务与原生运行时服务

这些接口为库存和机器功能提供基础，独立于 Forge。它们有实际原生调用和离线失败路径检查；原生扣电、液体单位、窗口显示与保存结果仍待游戏验证。全量最低范围与差距见 `FORGE-PARITY.zh-CN.md`。

## 调用边界

`Items`、`ItemInspection`、`Power`、`Liquids` 只能在已载入周目的游戏主线程使用，例如 `GameEvents` 同步回调。读档或切场景后必须重新取得物品；不要把物品、玩家、库存、tooltip 或回调上下文存进异步任务或静态缓存。`Transactions` 也要求初始化主线程，但纯托管步骤不要求已载入周目。

`FrameworkInfo.GetCapabilities()` 表示入口已实现，不能代替当前周目就绪检查。游戏目录保持只加载 EZapi；本版没有通过引用 Forge DLL 实现接口。

## 原生目录与实例检查

```csharp
// 放在当前游戏事件中。Exists/GetDisplayName 本身不创建物品。
if (Items.Exists("ezapi.sample.storage_box")) {
    var name = Items.GetDisplayName("ezapi.sample.storage_box");
}

// actualItem 是本次同步回调刚取得的当前实例。
var price = ItemInspection.CurrentUnitValue(actualItem);
var tooltip = ItemInspection.BuildTooltip(actualItem, advanced: false);
```

`Items.Create(id, owned:true)` 返回一个未放入库存的新实例。必须通过原生接纳路径交付，或失败后 Destroy；它不会自动发物或保存。不要在每帧、列表刷新或悬停中创建临时物品。

`CurrentUnitValue` 调用 `GameItem.GetCurrentValue(false,true,true,false,0)`，读取当前一件的原生价值，支持任何正数堆叠数量；保留子项、特征、原生生效事件与使用次数计价。它不拆分、不改变数量，不按数量相除，也不是整叠合计或最终交易价格。0.1.49 实机验证中，特征使当前价从137变为188，而 `GetValue(null,null,true)` 仍返回缓存137，因此不能用后者替换当前估值。`BuildTooltip` 直接走 `GameItemElement.GetTooltip/GetTooltipAdvanced` 的完成结果，游戏负责构建，不再手动调用 builder.Build。返回的原生 RichText 仅在当前同步展示中使用；要长期记录时复制为普通字符串。

## 补偿事务

```csharp
var before = myManagedState.Count;
var result = Transactions.Run(new[] {
    new TransactionStep(
        "change-count",
        Check: () => myManagedState.Count == before,
        Apply: () => { myManagedState.Count = before + 1; return myManagedState.Count == before + 1; },
        Restore: () => { myManagedState.Count = before; return myManagedState.Count == before; })
});
if (result.Status == TransactionStatus.RecoveryFailed) {
    // 记录 result.RecoveryFailures，停止下一批；不能当作已回滚或再次自动执行。
}
```

每个步骤必须有唯一可读名称、只读 `Check`、同步 `Apply` 和可验证的 `Restore`。每次最多128步，框架先固定步骤快照并整组预检，再在每次 Apply 前复核资格。回调不得通过修改原始列表改变正在执行的事务。

Apply 返回 false 或抛异常时，连同可能部分写入的当前步骤，按反序调用 Restore。某个 Restore 失败不会阻止其它恢复步骤，但结果必须为 RecoveryFailed。`AppliedSteps` 表示曾确认写入过的步骤，恢复后也保留用于诊断，不代表仍然提交。`Error` 记录原异常；恢复异常另列于 RecoveryFailures。

| 状态 | 含义 |
| --- | --- |
| Committed | 所有步骤返回成功 |
| Rejected | 写入前预检拒绝或异常 |
| Restored | 已尝试写入的步骤均确认恢复 |
| RecoveryFailed | 存在无法确认恢复的步骤，需内容作者处理 |
| Reentrant | 拒绝嵌套执行，没有进入内部步骤 |

这是同步补偿工具，不是原生数据库事务，也不锁定所有游戏对象。框架无法替调用者证明 Check 无副作用、Apply 的成功条件或 Restore 的完整性。涉及原生物品、金币、液体、能源时，内容作者必须快照真实状态、核对身份/所有权/数量、在步骤内读回，并处理原生方法可能产生的其它副作用。它不自动保存、推进日程或重试失败批次。

## 电力

```csharp
var cost = Power.GetMachineCost(machine); // 当前原生机器电耗
var energy = Power.GetEnergy(battery);   // 关闭带宽限制的总可用电量
var debit = Power.TryDebit(battery, cost);
if (!debit.Debited) {
    // 当前批次不得继续产出；RecoveryFailed 需停止并记录，不自动重试。
}

// 纯规划多个当前电源；返回值不代表已扣电。
var plan = PowerExamples.Plan(currentPowerSources, cost);
if (cost > 0 && plan.Count == 0) {
    // 电量不足或没有可扣电的电源。
}
```

GetMachineCost 读取 MachineryHelper；GetEnergy 读取 PowerHelper，单位为原生整数能源。TryDebit 支持当前周目、玩家拥有、已放入库存的原生电源，包括原生允许的堆叠项。amount 必须非负；不足、未拥有、未放置或原生 CanDraw 拒绝时不扣电。一次调用只调用一次原生扣电，不按 unitCount 放大金额，也不拆分堆叠。

调用原生 Draw 后必须实际减少指定数量才返回 Debited。拒绝、部分写入或异常时尝试恢复原值并读回；周目、父库存、堆叠数量、物品 ID 或实例身份发生变化时，不继续向变化后的上下文写入，返回 RecoveryFailed。Before/After 为实际读取值；After=-1 表示变化后不能安全读取。零费用仍需原生 CanDraw 认可，但不执行扣电写入。

`PowerBatchMath.Plan` 是纯托管规划器：验证每个实例 ID 为正且唯一、电量非负，使用 64 位总量检查溢出，并按实例 ID 升序分配费用。费用为零或总电量不足时返回空列表；负费用、无效快照或重复实例会抛出带 `EZAPI_POWER_BATCH_*` 标识的参数异常。它只规划，不扣电。需要从多个电源实际扣费时，把每项 `Power.TryDebit` 放进 `Transactions.Run`，每步重新核对身份与 Before 值，并提供原生读回恢复；不能把规划结果当成锁定或提交凭证。

0.1.43 已在 Probably Stolen 0.46D 的店铺测试档运行“电力接口检查”：单件和 5 件堆叠电源共 20 项原生检查通过，0 项失败；两种夹具均实际扣除 7 电并读回 93，恢复为 100 后销毁，且身份、数量和电池库存位置未改变。完整范围、存档校验和测试限制见 [0.1.43 原生验收记录](native-power-0.1.43.zh-CN.md)。`PowerBatchMath.Plan` 的 24 项纯托管回归另列于同一记录，不代表游戏内多源事务已验收。

独立调用不支持外部电源连接器，因为缺少机器上下文；连接器应使用机器批次事务。TryDebit 不执行原生带宽分摊或机器调度。不要在原生已经扣电的处理回调中再次调用，避免重复消耗。

## 液体只读快照

```csharp
if (Liquids.IsContainer(container)) {
    var snapshot = Liquids.Capture(container);
    // snapshot.Contents: 每种原生液体ID及 Parts（包括0）
    // TotalParts、CapacityParts、FreeParts、Millilitres、Purity、ConditionId
    var grade = Liquids.WaterQuality(container); // 纯水3，高品质2，基础1，其余0
}
```

Capture 扫描已经就绪的原生 Liquid.Liquids（最多4096种），通过每种 currentPart 标签读取真实成分，不按物品名称或 tooltip 推断。重复液体ID/体积标签、负数量、整数溢出、成分总和与原生总量不符、无效容量都会明确拒绝。

返回纯托管不可变快照，不保留物品/液体包装对象；之后原生内容变化不会改写旧快照。Parts 保留原生单位；当前对照契约按1000 parts=1ml计算 Millilitres，实际游戏读数仍待玩家核对。Purity 是原生整数，不直接叫作百分比；ConditionId 和 WaterQuality 使用原生水质条件。

本版没有提供倒液或成分写入，不会把等量的不同液体当作恢复成功。液体转移与混合事务在全量覆盖清单中继续待开发。

## 调试和错误定位

调试面板新增“读取物品/资源”。只在玩家点击后扫描当前周目 FindAllItem 根列表，上限4096件、最多64条资源详情；导出的 `resourcesDebug` 全是普通字符串、数值和只读快照。按钮不创建物品、不扣电、不倒液、不搬运、不保存，不能证明尚未执行的写入功能已通过。

常见错误：`EZAPI_NATIVE_NOT_READY`（等待当前周目）、`EZAPI_NATIVE_ITEM`（重新取得有效实例）、`EZAPI_TRANSACTION_STEPS`（检查步骤声明）、`EZAPI_LIQUID_CATALOG/STATE`（目录或真实成分不一致）。完整入口和错误见 API.index.zh-CN.json 与 errors.index.json。

0.1.49 实机估值验收：84组临时夹具、425项断言通过。覆盖数量1/2/5、0/1/137/10000000001价值、使用次数、Innate/Market/Final修正、未生效Event、容器子物品。未生效Event只验证保留原生结果，不声称覆盖所有活动事件。535个ES3文件哈希不变。参见 verification/item-inspection-native-0.1.49.json。

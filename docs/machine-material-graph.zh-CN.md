> 0.1.62：复杂材料图、液体物品/容器产出、普通电池及外电已补充两晚真实保存和冷启动验证；下方历史边界以本条限定的矩阵更新，完整范围见[跨夜指南](machine-persistence.zh-CN.md)。不包含原版熔炉所有材料策略或崩溃恢复。

# 带内部物品的机器材料

0.1.57 新增 `MachineRecipe.MaterialContents`。这只控制框架标准事务；自定义 `Transaction` 仍须自己实现材料去向和失败补偿。

| 值 | 标准事务行为 |
| --- | --- |
| `Reject`（默认） | 材料包含内部物品时，在创建产物和扣资源之前拒绝。旧配方保持原行为。 |
| `ConsumeGraph` | 明确消费根材料及所有层级的内部物品，包含内部液体容器本身；不返还空容器。 |

作者必须确认这符合玩法。只想使用容器内的液体，应声明 `MachineLiquidIngredient`，不要用整图消费。

## 最短示例

```csharp
var recipe = new MachineRecipe(
    "author.tools.recycle_box",
    new[] { new MachineIngredient("author.tools.scrap_box") { WholeStack = true } },
    new MachineItemOutput("author.tools.recycled_part"))
{
    MaterialContents = MachineMaterialContentsPolicy.ConsumeGraph
};
MachineRecipeApi.RegisterRecipes("author.tools", machineId,
    new[] { recipe }, new MachinePowerRule(7));
```

`WholeStack` 的含义仍是所选整堆的件数总和恰好等于需求数量。上例要求整堆共一件。带内部物品的根材料不能被拆堆消费；即使未填 `WholeStack`，标准事务也会检查实际选中数量必须等于根堆的完整数量。可编译完整示例见 `src/EZapi.Sample/MachineRecipeExamples.cs` 的 `RegisterWholeContainer`。

## 一次加工的顺序

先检查原生所有权、移除资格、图结构和数量。Apply 时冻结机器、产物和所有材料后代，将原根实例连同内容物移入临时保管库存，随后扣液体、电量并交付产物。

Verify 通过前不销毁材料。输出拒绝、部分交付或 Verify 失败时，将材料原实例退回原落点，恢复全部已记录标签、价值、内部层级和原生身份；液体与电量按标准事务日记退回。结构/身份已经损坏而不能恢复时报告 `RecoveryFailed`，保留剩余资源并隔离该机器，不伪造恢复成功。

确认提交后，按图拓扑从最深层到根逐件调用原生 Expel/Destroy。每步先检查剩余原生物品图，不再读取已销毁包装对象。清理发生异常时仍为 `Committed`，并在 `Transaction.CompletionError` 报告；不能退款、再次消费或自动重试。`AfterCommitted` 的通知也不能用来重复发放产物。

条件、动态解析器、事务工厂必须只读；`PrepareItem` 只能配置新产物。处理器也审计材料后代，发现回调改了内部物品便拒绝继续。这是检测边界，不会替恶意回调自动还原它直接写坏的资源。

## 限制与诊断

- 仅同一主线程同步调用；不跨帧，不提供断电、退出、跨夜或崩溃后的材料事务恢复。
- 单批最多 64 个根材料堆、4096 个材料物品；单根图最多 8192 个节点/边、16384 个标签，超限在写入前拒绝。
- 原生容器的嵌套限制继续生效。框架不自动给物品添加可嵌套标签，不绕过作者准入规则。
- 不包含自动保存；本阶段临时图验证不能代表实际磁盘保存重载已经验收。
- `EZAPI_MACHINE_MATERIAL_GRAPH` 表示图审计/恢复/提交清理异常；先检查 `Status`、`RecoveryFailures` 与 `CompletionError`，不要只看外层 `Error`。
- 条件和 Prepare 不能缓存 `GameItem`、原生指针或本次 `MachineRecipePlan` 到下一帧/下个周目。

官方 `PurpleBloodSynthesizer.FinishSynthesis` 提供了调用原生 Destroy 的示例，未定义嵌套材料的事务语义。本实现复用原生接纳、冻结与公开图结构，整图日记及失败策略由 EZapi 独立实现。

实际验收范围和结果以同版本 `verification.json` 为准；本指南不代替运行证据。

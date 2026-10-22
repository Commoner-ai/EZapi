> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 机器自定义资源事务

`MachineTransactionApi.Run` 执行一次同步的自定义加工资源事务。它补齐 Forge `IForgeBatchTransaction` 对应的 Validate/Apply/Verify/Restore/Fault 契约，并统一管理新产物的创建与失败清理。复用 `MachineBatchContext`，不重新定义批次价值或液体单位。此入口尚未提供标准配方匹配、机器注册、自动冻结、跨夜调度或外部电网。

0.1.23额外修复owner严格全文校验：执行器不依赖RunData旧版`$`正则。离线测试已移除owner校验替身，直接覆盖真实执行器，拒绝尾部换行、空值、非法大小写和段落。

## AI必须先确认

- 使用当前已载入周目的初始化主线程；机器必须是玩家拥有的单件，无拖拽物品。不能在后台线程、读档前或异步回调使用原生引用。
- 本接口是 C# 委托/接口扩展，没有独立JSON加工导入器。布局JSON只建窗口，图鉴JSON只展示说明。
- 内容作者提供真正的资源事务实现。空Validate、恒定Verify或不做写后核验的事务不代表生产成功。
- 资源事务负责材料、液体组成、价值、电源与产物落点。需要冻结时显式管理原生冻结租约；冻结期间不能直接用被阻止的 Expel/TryAcceptOnce 冒充已消耗。
- `Run` 不自动保存、不重试、不销毁材料。成功资源消耗与其完成后的暂存物清理由事务作者负责；它只清理本次框架创建、失败后已游离且身份可确认的新产品。

## 调用顺序

```text
Validate（完整只读预检）
→ 对每个输出单件 Items.Create
→ Prepare（只配置本次新物品）
→ Validate（再次检查全部资源与落点）
→ Apply（一次，允许部分写入后失败）
→ Verify（资源差额与图关系）
→ 核对全部新产品已交付
→ afterCommitted（最多一次）
```

任何Apply之后的异常/错误结果都会进入Restore，再清理本次新产物。每次回调后检查机器/周目/生命周期代数；身份变化后不调用旧资源恢复。跨场景、停用后重新启动不能使旧事务恢复为有效。

## 必须实现的五个方法

| 方法 | 契约 |
| --- | --- |
| `bool Validate()` | 只读，完整检查材料数量、真实属性、容量、输出空间、电力、机器身份与自己的冻结。可能调用两次，不能扣资源或生成物品 |
| `void Apply(IReadOnlyList<GameItem> products)` | 负责本次真实资源写入和新产品交付；任意中途异常均可能触发Restore |
| `bool Verify()` | 从原生状态读回本次消耗/交付/能源/液体组成；不能只信返回值或记忆中的变量 |
| `bool Restore()` | 恢复Apply全部或部分写入；逐资源恢复并读回；把本次新产品Expel到游离态，**不得销毁这些新产品** |
| `void Fault(string reason)` | 恢复无法证实时的诊断，只记录纯托管故障，不再写库存；可在周目变化后调用 |

`Restore` 不得因为一个资源恢复失败就跳过其它恢复。不要依赖原生 `TagSystem.Clone()` 是完整深拷贝；已实机发现标签可继续被活物品修改。本版限量倒液改为逐 `TagState.Clone()`，并拒绝独立指针不成立的备份。恢复只回填原state/dict，不能覆盖外部已替换的状态。

## 产品与结果

`MachineProductDefinition(ItemId, Count=1, Prepare=null)`：每份Count产生一个独立单件；总数最多64，非空产品组总数必须等于 `MachineBatchContext.OutputCount`。`Prepare` 可以设置本次新产品的价值/标签/图像，不能消费、移入库存或返回旧实例。零产品组用于作者自行管理液体产出的事务，仍须提供正的批次OutputCount。

`MachineTransactionResult.Created` 是本次创建过的纯托管ID/实例号收据，不是已交付保证，不含原生引用。

可将结果直接用默认 `System.Text.Json.JsonSerializer.Serialize(result)` 输出JSON；输出契约见 `schemas/machine-transaction-result.schema.json`，使用PascalCase字段和数值Status（Committed0/Rejected1/Restored2/RecoveryFailed3/Reentrant4）。该schema是结果格式，不是配方JSON导入器。

| Status | 意义 | MayRetry |
| --- | --- | --- |
| Committed | 资源Verify和产品交付均通过 | false；再次执行会生产另一批 |
| Rejected | 未进入Apply且新产品安全清理 | true，但需重新检查资源 |
| Restored | 已进入Apply，Restore成功且新产品安全清理 | true，但需重新计划 |
| RecoveryFailed | 恢复/身份/清理无法确认 | false；先核对故障，不能自动补发 |
| Reentrant | 当前已有加工事务执行 | false；不要在回调内递归执行 |

`afterCommitted` 抛异常时仍返回Committed，单独填 `CompletionError`。不要因为工作效果/提示异常而退还已消耗资源或重跑加工。成功通知只在全部确认之后调用，失败批次不会触发。

新产品若仍附着库存、身份被替换或与借入的机器/材料重合，会保留并报告RecoveryFailed，避免清理玩家物品。作者不得将Destroyed实例再次传回执行器；恢复时将产品安全移回游离态即可。

## 查找源码与验证

- 公共执行器/接口/结果：`src/EZapi/MachineTransactions.cs`。
- 原生标签逐项备份：`src/EZapi/NativeTagBackup.cs`，内部实现，不是公开存档格式。
- 最短调用范例：`src/EZapi.Sample/MachineTransactionExamples.cs`，需要作者实际资源事务。
- 原生故障注入示例：`src/EZapi/DebugMachineTransactions.cs`，只操作临时自定义目录物品；不是常规配方处理器。
- 调试“加工检查”按钮：`(634,372)`，同次执行无写入拒绝、成功、部分写入失败、Verify失败、完成通知异常、源/目标限量倒液失败恢复。

开发者不要复制调试事务当作通用冻结加工实现。当前标准配方、材料消费、跨夜去重和持久状态已有独立入口，见[配方](machine-recipes.zh-CN.md)、[机器自动化](machine-automation.zh-CN.md)与[机器持久化](machine-persistence.zh-CN.md)。功能实现不表示当前 DLL 已完成实机验收；0.1.23 的阶段记录已归档。

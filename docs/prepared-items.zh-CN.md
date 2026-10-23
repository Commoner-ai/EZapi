# 原生预制物品：物资箱、随机模块与真品注射器

当前版本以 release-status.json 为准；当前 DLL 原生内容、开箱效果和保存重载仍待验收；签名检查与离线替身不证明实机效果。见 [release-status.json](release-status.json)、[English](prepared-items.en.md)、[机器索引](prepared-items.index.json)、[独立示例](examples/prepared-item-dll/PreparedReward.cs)。

`ItemApi.Create(id)` 读取普通物品目录模板。有些原版玩法需要专门工厂继续填充、随机化或设置真伪条件。实战订单曾把空物资箱模板当成完整货物，后来不得不另写原地修复；新 API 将正确工厂入口列为显式选择，不修改普通创建行为，也不自动修复旧物品。

```csharp
var result = PreparedItemApi.Create(PreparedItemKind.SecurityCrate);
if (result.Status == PreparedItemStatus.Created)
{
    var detachedCrate = result.Item!;
    // 调用方拥有游离对象；下一步是自己的原生接纳/发奖流程。
}
```

| Kind | 实际物品 ID | 原生工厂 |
| --- | --- | --- |
| SecurityCrate | sec_box | LootCrateSecurity |
| MedicalCrate | med_box | LootCrateMedical |
| EngineeringCrate | eng_box | LootCrateEngineering |
| ServiceCrate | service_box | LootCrateService |
| EvidenceCrate | evidence_box | LootCrateEvidence |
| PerformanceModule | system_module_performance | RandomPerformanceModule |
| EfficiencyModule | system_module_efficiancy | RandomEfficiencyModule |
| QualityModule | system_module_quality | RandomQualityModule |
| OverclockModule | system_module_overclock | RandomOverclockModule |
| EcoModule | system_module_eco | RandomEcoModule |
| FinenessModule | system_module_fineness | RandomFinenessModule |
| GenuineInjector | large_purple_injector | CreateRealGenuineInjector |

`system_module_efficiancy` 的拼写沿用真实原生 ID，不能改成显示名称或英文纠错后的 ID。`Describe()` 是纯托管只读表，离线、工作线程和 AI 索引都可读取，不生成任何物品。框架不接管原生工厂补丁，其他 Mod 的工厂行为仍可能影响结果。

## 创建的含义

`Create(kind, owned: true)` 需要已初始化主线程、当前载入周目、已支持的游戏构建和可用冻结门控；正在拖动物品时拒绝。每次只调用一个明确原生工厂。物资箱保留工厂随机填充内容，核对非空子物品图和 `LOOT_CRATE_ID_STRING`，随后只在这件新物品上调用 `LockHelper.LoadLockedContainer` 并读回锁定状态；不调用 Fill、Refill 或解锁。

成功要求根物品数量为 1、ID 匹配、未入库存、与原有周目图不重叠，及请求的**根物品**归属读回一致。必要时只通过 `GeneralHelper.SetItemOwned` 设置根归属，保留子物品原生状态。未归属游离子物品可以共用原生实例编号 0，不据此强制改写或拒绝。`NestedItems` 是图中除根外的物品实例数，不是单位总量。随机模块的具体数值、注射器效果和子物品合法性沿用原生工厂，不承诺特定随机结果。

图检查有上限：8192 节点、4096 物品、16384 条父/子引用总量，要求游离结果图引用完整、双向对应、无环、无外部父节点。超限或不一致保守失败，不截断成“成功”。观察原有周目图前后指定字段一致，不代表所有游戏状态都未变：其他标签、RNG、金钱和磁盘并未纳入事务。

| Status | 含义 |
| --- | --- |
| Created | Item 非空，调用方接管这件游离对象 |
| Invalid | 无效预制类型，未调用工厂 |
| Unavailable | 线程、周目、构建、目录或预检不可用，未调用工厂 |
| Conflict | 拖动、重入或预检过期，未调用工厂 |
| Indeterminate | 已进入工厂，结果未通过确认；停止自动重试 |

`FactoryAttempted` 是恢复依据：一旦为 true，即使创建抛异常或清理完成，也可能已消耗随机数或运行其他 Mod 回调。`Indeterminate` 不暴露不可靠的 Item，不代表“没发生任何事”。不得以此自动再次调用工厂刷新奖励。

失败清理只对当前周目中可重新核对为独立、无已存在图节点、无库存归属的游离结果调用一次 Destroy；已入库存、含借用节点、图坏、被冻结或周目变化时保留对象并报告。`DestroyReturned` 仅表示清理调用返回，不是销毁效果或 RNG 回滚验收；`SkippedUnprovenGraph`、`SkippedStaleRun`、`Failed` 和 `FactoryDidNotReturn` 分别报告边界。框架不盲目拆图、清父节点或删除其他 Mod 的对象。

## 与其他 EZapi 能力组合

- 普通/赃物及固定热度由 [ItemOriginApi](item-origin.zh-CN.md) 明确处理，不因预制类型或店铺来源自动决定。
- [交付凭据](delivery-receipts.zh-CN.md) 可标记根物品，但 `DeliveryReceiptAudit.CheckHandoff` 严格要求只新增一件实例。**装满子物品的物资箱不满足这一前提**；不可强行把返回的不确定当作成功。此类发奖使用新的 [DeliveryGraphAudit 整图核验](delivery-graphs.zh-CN.md)；调用方仍负责账本与实际原生接纳。
- 创建后调用方负责原生接纳或确认仍拥有游离对象后清理；原生接纳后绝不能再读取或 Destroy 旧包装对象。
- 不把普通目录用于的 [物品目录扫描](item-catalog.zh-CN.md) 改成预制工厂，否则仅浏览货物就会填箱、消耗随机数和创建大量子对象。

中文与英文使用相同 Kind、稳定 ID、状态及数量。物品原生显示名称按游戏语言处理；调用方提示使用 `GameLanguage.Resolve`，不要把工厂名或内部 ID 当作玩家标签。

离线套件直接链接生产源码，使用明确工厂/图/归属/锁替身，覆盖十二条路线、重复调用、借用节点、坏图、回调入库、换档、超限、清理失败和预检拒绝。真实随机分布、锁/开箱/子图保存与其他 Mod 组合仍待当前 DLL 实机验证。

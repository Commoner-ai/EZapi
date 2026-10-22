> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 液体转移、成分和批次元数据

0.1.22修复：备份改为逐TagState.Clone，不依赖TagSystem.Clone的深拷贝假设。实机对源扣液后失败和目标加液后失败两种情形核对双方精确组成、价值恢复及冻结释放；见 `machine-transactions-0.1.22.zh-CN.md`。此修复不扩大原生过滤反应的可恢复范围。

EZapi 独立调用当前游戏公开接口，不依赖 Forge。新增 LiquidTransferApi 的两个 Pour 重载和 LiquidCompositionMath；对应 Forge 液体转移组与成分辅助契约。模块、机器配方、外部电力等仍在完整待办中，不把此阶段视作整个框架完成。

## 选择入口

| 需要 | 入口 | 写入与限制 |
| --- | --- | --- |
| 看实际数量/纯度 | Liquids.Capture | 只读，不解释显式价值标签 |
| 看批次总价值/品质 | Liquids.CaptureProduction | 只读；自有元数据过期/损坏抛错，保留原文 |
| 按原生规则整次倒液 | LiquidTransferApi.Pour(source,target) | TransferLiquid仅调用一次；保留原生过滤，不保证可回滚 |
| 限量倒液 | LiquidTransferApi.Pour(source,target,maximumParts,stillAllowed) | 双容器冻结+原生Remove/AddLiquid，拒绝过滤器；失败尝试补偿 |
| 计算消耗/转换/合并 | LiquidCompositionMath | 纯托管，不改变物品、不注册新液体 |
| 显式设置/迁移价值 | Liquids.SetProductionMetadata | 冻结单件，容量/ID/数量必须匹配实物；返回TransactionResult |

所有原生入口仅允许游戏初始化主线程、当前载入周目、有效物品。倒液对象必须是已拥有、已放入库存的单件液体容器；源移除锁、目标插入锁、拖拽或外部冻结会拒绝。同一物品不得同时参与嵌套操作，源目标互换也被拦住。不要缓存GameItem跨帧或跨读档。

单位是原生parts，**1000 parts = 1ml**。maximumParts必须正数，实际转移上限同时受源存量和目标空间限制。Value/QualityBasis是该成分的**合计值**；不再乘Parts。缺少Value时基础价值为parts/25000；缺少QualityBasis时依次用Value、默认基础价值作为计算依据。这些计算不注册水质、效果或新液体。

## 结果处理

```csharp
var r = LiquidTransferApi.Pour(source, target, 1000, () => myRuleStillAllows);
if (r.Indeterminate) {
    // 停止本批，记录r和真实源/目标状态；绝不能自动重试同一次倒液。
    return;
}
if (!r.Poured) return;
// r.PartsMoved是确认转移的parts。
```

Poured表示核对成功；PouredReadback表示原生调用抛错，但实际数量、成分和元数据证明转移已完成，Error保留原始错误。EmptyOrFull没有调用倒液。Rejected/Unavailable/Reentrant拒绝提交；Restored表示写入曾开始且确认恢复；RecoveryFailed和Indeterminate意味着状态未知，必须停止。**PartsMoved=0并不保证实际没有变化**，尤其不能据此重复执行。

无参上限的原生路径不会调用Restore：过滤损耗、反应、身份变化、无法证明守恒或写后同步失败都报告Indeterminate。原生函数运行后即使没有液体移动，也不能证明滤芯耐久等副作用未发生。目标过滤导致成分变化时，不把它误报为普通守恒转移。

限量路径要求库存冻结钩子就绪，先用官方SplitProportionally得到当前游戏的整数分配，再写Remove/AddLiquid。stillAllowed必须只读且同步；每次回调后重查周目、对象、成分和冻结。它可能被调用多次，不能依赖精确调用次数。部分写入后，只有原周目、身份、归属、父库存和state/dict都未替换才恢复双容器标签与unitValue；一边恢复抛错仍会尝试另一边。恢复不了便明确报告，不跨周目写回。此行为是内存补偿，不是存档事务；不自动保存、重试或回滚未知原生过滤反应。

倒液与元数据设置内部使用Transactions.Run；不要从另一Transactions.Run的Apply中调用它们，嵌套事务返回Reentrant。调用方应在自己的同步操作边界检查结果后再继续后续步骤。

## 成分纯计算

- Consumed(before,after)：同容量与同ID集合，禁止任意成分或元数据增加；只返回正消耗的条目和合计元数据差量。
- ReplaceComponent(contents,sourceId,targetId)：源必须存在、两ID不同；合并到目标，保留合计价值/品质。纯转换结果不等于游戏已经注册或转换了该液体。
- Consume(snapshot,parts,liquidId=null)：指定成分，或使用确定性的最大余数法；余数相同按输入顺序。托管算法只计算快照，不能假装与原生Remove完全等价。
- Add(target,components)：只能合并目标快照中已存在的ID；超容量、重复ID、负值和溢出拒绝。
- BaseValue(contents)：显式Value优先，否则parts/25000，再求和。

上限4096条成分，ID最长160且不得含控制字符；零parts不能携带正价值/品质。快照复制输入，不改调用方集合。整数与decimal溢出抛异常，不能截断或饱和。JSON成分的排列顺序不影响原生倒液的舍入：读取保存元数据时按当前原生目录重排。

## 元数据保存与兼容

EZAPI_LIQUID_METADATA_V1是EZapi自有的物品标签，内部为Version=1的JSON，不兼容或迁移其他框架标签。只在作者显式设置过总价值/品质后存在；普通倒液不会给所有游戏容器写新标签。不要手工拼接内部标签，使用SetProductionMetadata。

```csharp
var actual = MachineLiquidSnapshot.From(Liquids.Capture(container));
var annotated = new MachineLiquidSnapshot(actual.CapacityParts,
    actual.Contents.Select(p => p with {
        Value = p.Parts / 1000m * myValuePerMl,
        QualityBasis = p.Parts / 1000m * myQualityPerMl
    }).ToArray());
var result = Liquids.SetProductionMetadata(container, annotated);
if (!result.Committed) return; // RecoveryFailed停止；不得当作成功
```

CaptureProduction严格检查版本、字段、重复键、容量、ID、实际数量；损坏或过期保留原文，不猜测价值。通过游戏原生喝水、其他Mod或旧代码改变了液体而没有同步EZapi元数据，下一次生产读取会明确拒绝。作者确认新价值后，显式SetProductionMetadata可修复这类过期/损坏记录；失败尝试恢复原始记录，包括损坏原文。若要主动放弃历史价值，显式设置MachineLiquidSnapshot.From(Liquids.Capture(container))，这会清除自有标签，不能自动替玩家作此决定。

公开快照输出的JSON形状见../schemas/liquid-snapshot.schema.json；它是**输出契约**，不是items.json的新增字段或运行时导入器。跨字段的总量、容量、ID唯一性由C#快照校验。默认示例物品JSON无修改，无需迁移既有内容包。C#调用者可在pack.json声明minEzapiVersion:"0.1.17"及runtime.liquid-transfer、production.liquid-composition、production.liquid-metadata能力。

## 同一次游戏会话测试

1. 使用测试档，确认日志0.1.17。先点“一次批量检查”，同时覆盖物品/tooltip/容器、库存、资源、生产计算/效果状态和生命周期记录。Batch collected仅表示报告收集完毕，核对各领域明细。
2. 准备两件已拥有的液体容器，一件有液体、一件留空位，均不带过滤器。点“倒液预览”，面板显示源名称/实例号→目标名称/实例号。不会写液体；选择不符合预期时不要执行。
3. 点“执行预览≤1ml”。一次预览只提交一次，最多1000parts；diagnostics.json的liquidTransferDebug含源/目标前后成分、状态和PartsMoved。若预览后容器或内容改变，会拒绝并要求重选；场景切换清除预览。按钮不会自动发物品、喝水或保存。
4. 在同一次会话继续拖放、吃喝、睡觉、外出回店、正常保存和载入，再批量检查并导出。通过示例代码显式写过元数据时，还要核对保存重载后的Value/QualityBasis。跨进程持久性需下一次正常启动回读。

本次离线替身覆盖转移、回调、补偿、元数据、调试预览，但不证明原生舍入、冻结detour、过滤耐久、Unity布局或存档重载。未自动启动游戏；参考技能所需专用原生测试流程仍缺失。正式证据以随包verification.json为准，不沿用旧0.1.14进程日志宣称新版本通过。

# 知识库扩展：升级、服务和前置条件

`ProgressionApi.ReadUpgrade(id)` 和 `ReadService(id)` 将当前原生对象转换为纯托管快照。网络升级分别返回实际价格、解锁、就绪、前置、Demo 锁和冷却；服务分别返回已介绍、已解锁、使用中、实际生效和冷却。不能用一个布尔值替代这些状态。

查询只在已载入周目的主线程、已核对游戏构建中可用。`Ready`、`Missing`、`Unavailable`、`Invalid` 分别表示成功观察、原生未找到、当前不能可靠读取、参数错误；游戏原生查找抛错时返回 Unavailable，不能假定不存在。快照保留本次槽/周目/Epoch 身份，不保留原生对象。快照不会自动刷新，执行玩法操作前应重新检查。

框架不调用 `Unlock`、`PayMoney`、`UpdateCost`、目录初始化或服务修复。查询到可购买不代表已经付款；真正付费仍走原生购买入口或明确的 `RunPurchaseApi` 事务。

`RequirementApi.Evaluate(definition)` 为机器、工坊、对话和事件提供同一套只读判断。树支持以下种类：

| kind | 参数 | 含义 |
| --- | --- | --- |
| all / any / not | children | 全部 / 任一 / 取反；not 必须一个子项 |
| upgrade-unlocked / upgrade-ready | targetId | 原生升级已解锁 / 原生就绪 |
| service-unlocked / service-introduced / service-active / service-off-cooldown | targetId | 服务独立状态 |
| day-at-least / day-at-most | amount | 原生游戏日下限 / 上限，不自行加一天 |
| credits-at-least | amount | 当前实际余额下限，不扣钱 |

结果有 `Satisfied`、`Unsatisfied`、`Unavailable`。取反保留 Unavailable；未知对象或读取异常绝不因 not 变成放行。all 中一个明确不满足即可判不满足；any 中一个明确满足即可满足，其余未知仍在子结果中保留。`Satisfied` 便捷属性仅在明确满足时为 true。空 all 表示没有限制，但当前周目入口在未加载时仍不可用。

声明会先整树校验，再执行任何事实查询：最多 128 节点、8 层、每组 32 子项。禁止循环、空 any、未知 kind、混用叶参数和子项。数值 0..Int32.MaxValue。同次判断缓存重复事实，并在返回前复核周目身份；没有跨回调缓存。

```csharp
var condition = new RequirementDefinition {
    Children = new[] {
        new RequirementDefinition { Kind="day-at-least", Amount=3 },
        new RequirementDefinition { Kind="credits-at-least", Amount=100,
            Message=new("至少100信用点", "At least100 credits") }
    }
};
var result = RequirementApi.Evaluate(condition);
if (result.Satisfied) { /* 只说明当前资格满足；不是消费或发奖 */ }
```

JSON 采用相同的 kind/targetId/amount/message/children 字段，见 `examples/requirements.json` 和 `schemas/ezapi-requirements.schema.json`。`Requirements.Parse(stream)` 严格拒绝未知、重复、null 字段，最多64KiB；JSON Schema 可辅助编辑，但循环/总节点/深度及只读运行语义以解析器为准。`Requirements.Evaluate(definition, IRequirementFacts, locale)` 是纯托管入口，便于离线工具或作者受控的事实源。

`Message` 使用 `LocalizedItemText`；当前游戏语言由原生 locale 选择，缺失一侧回退另一侧。网络升级标题来自原生本地化，服务 `NativeDisplayName` 按原生值原样提供；它可能是键或固定文字，不能承诺已翻译。`Reason` 是开发诊断，不应直接代替玩家文案。

知识库依据是 Probably Stolen 技能的商店进度、每日事件与存档场景；技能只作参考。实际查询签名对照当前 050REV5-L 的 Assembly-CSharp，优先使用已公开原生判定，不重建原生付费/解锁逻辑。本阶段未增加自动日程、委托奖励或存档迁移。

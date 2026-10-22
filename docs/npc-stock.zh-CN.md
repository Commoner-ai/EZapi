> 0.1.64：固定商人货位、机器分类和预制机器空ID兼容已补充实测。见[新指南](npc-fixed-stock.zh-CN.md)。下方0.1.51历史验证数字不包含本轮。

# NPC 货源：声明、筛选与原生矿工

EZapi 0.1.51 为 JSON 和 DLL 工厂提供同一份 `NpcTradeStockOptions`。这是独立设计的候选池与当前游戏适配器；Forge 仅作为公共功能范围参考，没有 Forge DLL 依赖。

## 最少配置

```json
[
  {
    "id": "author.minerals.crystal",
    "name": "蓝晶矿",
    "value": 250,
    "npcTrade": {
      "category": "Ore",
      "minerWeight": 2
    }
  }
]
```

内容包 `pack.json` 需声明 `minEzapiVersion: "0.1.51"`。普通物品注册不自动加入所有商人；必须选择分类和对应的已实现路径。

```csharp
var result = ItemApi.RegisterItem("author.minerals", "author.minerals.crystal", CreateCrystal,
    new NativeItemOptions {
        NpcTrade = new NpcTradeStockOptions(NpcTradeStockCategory.Ore) {
            MinerWeight = 2,
            MinimumDay = 5,
            SkipWhenOwned = true
        }
    });
if (!result.Accepted) LoggerInstance.Error(result.Reason);
```

只接受已应用为 `Applied` 的声明。工厂每次创建独立、ID 正确的游离实例，后续放置和购买仍由游戏处理。

## 选项与默认值

| C# / JSON | 默认 | 含义 |
| --- | --- | --- |
| `Category` / `category` | `None` | `Material, Ore, Module, Machine, Household, Food, Medical` 可组合，逗号分隔。分类表示参与哪类候选池，不改变物品类型或使用行为 |
| `Weight` / `weight` | 1 | 普通供应的相对权重，有限且大于 0、不超过 1,000,000,000；不是百分比 |
| `Suppliers` / `suppliers` | `Any` | `Thief`、`Inventor` 或二者组合；矿工使用自己的权重，不受这一筛选限制 |
| `IncludeInNightShop` / `includeInNightShop` | true | 在夜间随机候选池中允许出现；不等同于每次直接补货的 `nightShop` |
| `SkipWhenOwned` / `skipWhenOwned` | false | 当前玩家持有时排除，含子容器；不是永久限购 |
| `MinimumDay` / `minimumDay` | 0 | 原生 `StoreStation.GetDayCounter()` 达到此数值时允许，不自行加 1 |
| `PreserveLootTableWeight` / `preserveLootTableWeight` | true | 基础表已有同 ID 时保留基础权重；false 才覆盖。已有条目仍遵守天数、持有等限制 |
| `MinerWeight` / `minerWeight` | 未设置 | 矿工独立相对权重；未设置或 0 不参与，有限且在 0—1,000,000,000 之间 |

这些默认值是 EZapi 自身的明确契约，未把参考框架未公开的属性初始化值当成依据。JSON 枚举只接受声明过的名称，大小写不敏感；拒绝整数、数字字符串、未知名、重复名、零值与其他标志混用。未知字段、重复字段和显式 null 同样报错。

## 当前自动路径与尚缺内容

| 路径 | 当前支持 |
| --- | --- |
| 商人作用域内 `materialTable` | Material |
| 商人作用域内 `allModuleTable`、`t1moduleTable`、`t2moduleTable` | Module |
| 商人作用域内 `householdTable`、`packedFoodTable`、`medicalTable` | Household、Food、Medical |
| 原生矿工生成货物的对话回调 | Ore，使用 `MinerWeight` |
| Machine 与原版发明家/小偷固定物品货位 | 0.1.64已按官方实例类型接入，含预制机器；特殊未分类物品保留原路径 |

命名表只在精确的发明家补货或小偷货物生成调用域中替换抽取。普通拾荒、作用域外抽取、无相关声明的调用保持原路径。`RuntimeCapabilities.Current.NpcTradeStock` 表示这些适配器所需钩子就绪，不能据此推断表中尚缺的路径已实现。

本版矿工没有使用命名掉落表：原生先决定槽数，再逐槽创建固定矿石。EZapi 在每槽创建之前，以原矿石权重 1 加上合格自定义矿石的 `MinerWeight` 进行一次抽取；例如唯一自定义矿石权重 2 时，它占 2/3，原矿石占 1/3。保留原版槽数、天数规则和后续接纳逻辑。不合格或没有矿工候选时，不额外推进 RNG 或触发事件。

## 作者自建商人和离线规划

`NpcStockApi.BuildPool(tableId, category, supplier, nightShop, miner)` 读取真实命名表与当前玩家状态。无命名表时可传入 `IReadOnlyList<NpcStockEntry>` 基础权重。两种重载都只返回托管快照，不生成、放置或交付物品。

`NpcStockApi.TryRoll(...)` 使用游戏 RNG 抽取 ID，发布一次官方 `OnLootRolled`，保留 tableId/groupId 并接受回调改写的 itemId。空池不推进 RNG；事件重入会报错，周目改变会使结果失效。它仍不创建或购买物品。矿工合成的事件 tableId 为 `ezapi.npc.miner`。

离线使用 `NpcStockPlanner.Build(context, baseline, candidates)` 与 `pool.Pick(sample)`；sample 在 `[0,1)`，由作者提供，不偷偷改变游戏 RNG。快照不可修改，不持有游戏对象。输入最多 4096 个基础条目与 4096 个候选；当前持有扫描最多 16384 件。原表重复 ID 合并权重、保留首个来源，自定义候选 ID 必须唯一并带作者前缀。原生单精度总权重溢出会在 RNG 调用前拒绝。

## 验证与生命周期

94 项规划/严格 JSON 检查及 42 项真实适配器加原生边界替身检查覆盖筛选、权重、不可变快照、事件次数、回调异常、重入、换周目、钩子失效及矿工原路径不推进 RNG。

原生调试夹具检查实际命名表抽取、官方事件、组 ID、改写和重入，并直接执行本版矿工的货物生成委托；临时截获原生交付入口，核对真实创建的矿石后销毁。它没有选择 NPC 对话，没有把物品放上真实货架，也没有购买或保存重载。自动验收由 `Invoke-EZapiTestBatch.ps1` 执行，正常过程不截图。

调用域以周目身份和嵌套令牌隔离，finalizer 负责正常/异常退出清理。只在已核对的游戏构建安装精确签名钩子；版本不匹配时停用对应能力。实现不写全局 LootRegistry 表或 journal，不缓存跨读档的拥有量，不修改拖拽或店铺界面。

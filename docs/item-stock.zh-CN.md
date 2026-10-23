> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

> 0.1.65：已修正官方回调列表语义并验证真实上架、购买、磁盘保存与新进程恢复。见阶段证据（历史标识 `verification/night-placement-native-0.1.65.json`；原件未公开）。

# 物品夜间商店供应

从 EZapi 0.1.50 起，可在物品注册中声明夜间发明家商店供应；请使用0.1.65或更新版本。官方Early/Late事件参数均为当前已拥有物品的快照，不能作为待上架队列。0.1.65按官方LearnerMod示例，在 `OnPlaceInventorInventoryItemLate` 中调用 `PlayerStore.AddDirectSellingItemToTable`，再读回真实销售柜台的物品数量。旧版仅追加快照的实现不能保证上架。

## JSON：只加一个字段

```json
[
  {
    "id": "author.tools.sensor",
    "name": "便携传感器",
    "value": 137,
    "nightShop": "unique"
  }
]
```

内容包的 `pack.json` 请声明 `minEzapiVersion: "0.1.65"`。`nightShop` 可选 `none`、`repeatable`、`unique`，省略为 `none`。CLI 与运行时共用严格校验规则，未知值直接报错。

`repeatable` 表示每次补货可加入一份；真实销售柜台已存在未拥有的该ID时不再创建，同一原生补货调用每个ID最多尝试一次。`unique` 还要求玩家当前未持有该ID，包括玩家子容器中的物品。它不是“本周目永远只能购买一次”：出售或丢弃后，下次补货仍可出现。物品获得后正常走游戏保存；EZapi不另建购买计数存档。

## C#：工厂保持原样，增加供应选项

```csharp
var result = ItemApi.RegisterItem(
    "author.tools", "author.tools.sensor", CreateSensor,
    new NativeItemOptions {
        NightShop = NightShopStockPolicy.Unique,
        IsNightShopAvailable = () => MyUnlockState.SensorUnlocked
    });
if (!result.Accepted) LoggerInstance.Error(result.Reason);
```

`CreateSensor` 必须按原工厂契约每次返回一个ID正确、数量为正、未放入库存的新实例。物品目录应用为 `Applied` 后才参与供应。`IsNightShopAvailable` 在主线程按需调用一次；没有提供就视为可用。它应只读当前状态，不保存、不切场景、不生成物品、不再调用补货。异常仅排除当前物品，后面的物品继续处理，错误写入 `ItemApi.StockSnapshot()`。

## 隔离与诊断

`RuntimeCapabilities.Current.NightShopStock` 要求官方事件订阅和精确的 `StoreClientList.PlaceInventorInventory(bool)` 调用域钩子均在。普通发明家到访的 `isVisitingPlayerStore=true`、任意外部直接发出的同名事件，均不加入夜间物品。异常结束通过 finalizer 清除调用域，避免下一次顾客或店铺操作沿用旧状态。

每个候选先检查目录、玩家当前持有情况和已有货物，再执行作者条件。创建后将未拥有的游离物品交给原生上架方法；保留原生perk限制、所有权和接纳规则。原生拒绝可能直接销毁该物品，交接后EZapi不再读取或再次销毁实例。只有销售柜台中该ID的未拥有数量增加量与交付数量一致时，`StockSnapshot.Added`才增加；拒绝或异常记录`LastError`。柜台最多4096项、拥有快照/实时玩家扫描最多16384项。每个作者条件前重新读取当前拥有状态，场景、柜台或框架代次变化时停止。方法抛出异常后的交付状态不自动重试，也不宣称成功。

## 验证边界

> 历史验收记录（0.1.50），不作为当前 DLL 通过依据。
> 历史0.1.50 的53项离线检查覆盖策略、去重、当前拥有、JSON路由、条件/工厂/列表写入失败、换周目、停止、重入、数量上限与钩子丢失。原生夹具通过9项检查，使用真实官方事件与独立临时列表，验证夜间/到访/无调用域隔离、去重和失败隔离。它没有运行完整夜间商店补货与购买流程，也没有验证保存重载。完整端到端货架与购买验收仍待完成。

0.1.51 已新增 NPC 分类候选池、供应商筛选、矿工权重、最小天数与已有掉落权重保留，见 [NPC 货源指南](npc-stock.zh-CN.md)。自动路径目前包括商人作用域命名表与原生矿工生成委托；0.1.64补充固定发明家/小偷分类货位（含预制机器）；具体类型优先级、来源去重与尚缺的购买验证见[npc-fixed-stock.zh-CN.md](npc-fixed-stock.zh-CN.md)。

## 0.1.65契约修正

`Added`现在表示完整原生上架读回通过的次数，不表示快照追加次数，也不表示购买次数。没有改变公开C#方法、JSON字段或枚举。`IsAvailable`依赖同一个Late委托仍订阅和精确补货调用域钩子就绪。普通到访、无调用域事件、重入事件不供货；finalizer按令牌退出调用域，作者停止框架时不会误弹外层范围。

离线检查直接链接产品运行源码，通过原生边界替身覆盖拒绝销毁、抛错前后、当前拥有状态、目录失败、去重、换周目/柜台、重入、停止和上限。游戏内夹具必须实际读回销售柜台父库存和未拥有状态，结束清除自己创建的物品。历史 0.1.65 端到端夹具曾执行完整原生PlaceInventorInventory(false)、BuyItem、当前日SaveGame、磁盘读回与新进程恢复：两件物品各扣17，现金525→491，数量和实例ID保存恢复一致。柜台满时原生拒绝部分候选，清理临时原版货物后的下一独立补货才重新尝试，不绕过容量。测试并未实际睡觉，也未执行第三次机器加工；既有复杂机器状态保持不变。这段只描述历史 0.1.65 DLL；当前 DLL 的上架、购买、保存重载、特定 NPC 对话选择购买和多 Mod 组合仍需独立验收。

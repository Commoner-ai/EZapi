> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi原生食品与液体容器

这两个入口把声明映射到当前游戏构建公开的 `FoodItemHelper` 和 `WaterHelper`。官方指南的通用注册流程继续适用，但官方 LearnerMod 的治疗示例不是食品/液体实现依据；这两个 helper 的证据来自当前 `Assembly-CSharp.dll` 签名和编译结果，详见随包 verification/current-food-contract.txt。它们没有新增 Harmony 补丁。

## 食品

```json
{
  "id":"author.food.ration", "modId":"author.food", "name":"口粮",
  "value":20, "width":1, "height":1,
  "food":{"calories":400,"canned":false,"freshness":10}
}
```

将对象放到 items.json 数组中。`calories` 必填，整数 1..100000，原样传给 `FoodItemHelper.InitFoodItem`；`canned` 默认 false，是原生罐装标记。`meat` 默认 false，从 EZapi 0.1.16 起转交当前官方第四参数 `isMeat`；使用此字段的包应要求 `minEzapiVersion:"0.1.16"`。`freshness` 可选，整数 0..100，存在时调用 `InitFreshFood`，省略就不调用。这个范围是 EZapi 当前声明范围；该值为原生初值，不在框架中解释成天数或百分比。

分类初始化与进食交互、对象、实际效果、消耗和保鲜推进交给原生 helper 与游戏规则；需要显式追加分类时使用 types 并核对当前游戏分类表。不要把热量写成恢复玩家血量/饥饿值；框架没有另造玩家饥渴系统，也不会在初始化时自动食用。原生 helper 是否绑定激活按钮，以及是否需要队伍/探索场景，都需玩家运行确认，不能从声明字段推断。

## 液体容器

```json
{
  "id":"author.food.bottle", "modId":"author.food", "name":"水瓶",
  "value":15, "width":1, "height":2,
  "liquidContainer":{"capacity":500,"startFull":true}
}
```

0.1.18 起使用 `WaterHelper.InitLiquidContainerItem(item,capacity,value,false,false,false,false,3)`，满瓶另调用 `WaterHelper.FillWithBaseWater(item)`。旧 `LiquidContainerHelper` 在当前构建虽保留签名，却未建立新版容量与成分，不能继续用于创建。`capacity` 是传给原生 helper 的容量，单位毫升，范围 1..100000，默认500；`startFull` 默认 true。`{}` 等于使用这两个默认值。装满时的具体液体组成仍由原生 helper 决定，当前不提供 `liquidId`、混合比例或自定义液体注册。饮用、倒液、拆分和自动饮用仍交游戏处理。

C# 使用同一份定义：

```csharp
ItemRegistry.Register(new ItemDefinition {
    Id="author.food.ration", Name="口粮", Width=1, Height=1, Value=20,
    Food=new FoodDefinition {Calories=400, Canned=false, Freshness=10}
});
ItemRegistry.Register(new ItemDefinition {
    Id="author.food.bottle", Name="水瓶", Width=1, Height=2, Value=15,
    LiquidContainer=new LiquidContainerDefinition {Capacity=500, StartFull=true}
});
```

## 行为与状态边界

food、liquidContainer、JSON activation 和 C# ItemActivation 是不同的行为初始化入口。同一件物品只选一个；原生食品/液体也不能叠加通用 useCount。原因是这些 helper 可能安装自己的原生委托和状态，重复绑定会覆盖行为。组合食品饮料、可多次使用食品等扩展需明确实现完整组合逻辑后开放，不能悄悄按顺序覆盖。

原有普通物品默认值不变。新食品/液体在 helper 初始化后，只有显式提供的 `forceDisableUse/forceDisableActivate/activateDefault/bonusAccuracy/triggerOverwatch/canUseOutsideCombat` 才覆盖 helper 的默认值；显式 false 和 0 也生效。声明的价格、音效、自定义 tags 继续应用。对这些物品不要用 tags 重写原生 helper 的内部状态。

工厂每次创建独立实例；读档时沿用游戏原生恢复流程。框架不定时重置新鲜度或液体，不在每帧重建物品，不请求自动保存。未运行游戏前不能声称读档恢复、容量或原生效果已经验证。

## 内容包和 AI 阅读

当前构建新包建议 `minEzapiVersion:"0.1.18"`，并按需声明 `requires:["items.food","items.liquid-container"]`。只用其中一种就只要求对应能力；旧包不填写这两个对象保持原行为。先运行 `--pack`，再用 `--packs` 检查依赖。Schema 见 schemas/items.schema.json。

调试列表新增“食品 热量”和“液体容器 ml”摘要。这是声明值，不是假装读取了当前剩余量。`校验原生属性` 只创建临时实例、读取属性/tooltip/委托并销毁，不执行进食、饮用、倒液或保存。诊断 nativeConsumable 会记录所用声明和原生激活委托是否绑定；这些字段不能替代实际行为证据。

## 玩家验证

1. 重启确认0.1.7，八件示例应为 Applied；原六件ID和定义不变。新物品使用占位图，不依赖新图片。
2. 点击生成“EZapi 原生食品”和“EZapi 500ml 液体容器”，检查悬停、占格、旋转和拖动；不要先给旧存档已有物品更换类型。
3. 按游戏提供的食品、饮用或倒液交互测试。记录操作所在场景、角色/队伍状态，以及实际物品变化；没有入口时先导出诊断，不用手动加另一套激活委托。
4. 通过正常游戏流程推进一天并保存重载，核对食品状态、液体剩余量与物品位置，确认未被重置或复制。
5. 同时回归敷料治疗和原四件属性示例；出错后给出最后一步，我读取新日志和 diagnostics.json。

0.1.18 原生容量/基础水成分已读取确认；真实食用和饮用仍需合法角色/场景。店铺内双击进入目标选择，不能将进入选择模式算作消耗成功。水瓶调试页提供新空瓶，可独立验证限量倒液。

本轮正常跨天保存、同进程载入及冷启动回读均保留三瓶499ml/500ml/1ml；食品和敷料位置保留。此结论不证明真实进食/饮用消耗或所有食品状态迁移。

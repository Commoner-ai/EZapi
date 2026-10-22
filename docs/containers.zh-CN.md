> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi原生储物容器

目标：用一段 `container` 声明，把普通物品变成具有独立内部网格的储物容器。EZapi 创建结构，玩家通过游戏原生入口打开、关闭和拖放。接口最初版本为 0.1.9；当前构建建议最低版本为 0.1.18，能力名为 `items.container`。

0.1.18 已自行在游戏验证打开/关闭、ID过滤、禁止格拒绝、关闭后拖放、嵌套敷料的跨天及冷启动恢复。标签过滤实战和其它组合仍待验证，详见 native-compatibility-0.1.18.zh-CN.md。

## JSON 最小示例

`pack.json`：

```json
{"id":"author.storage","name":"我的储物包","ezapiVersion":"0.1","version":"1.0.0","minEzapiVersion":"0.1.18","requires":["items.json","items.container"]}
```

`items.json` 是物品数组：

```json
[
  {
    "id":"author.storage.box",
    "modId":"author.storage",
    "name":"随身储物箱",
    "width":2,
    "height":2,
    "value":80,
    "shortDescription":"内部 4×3 格储物空间。",
    "container":{"width":4,"height":3,"title":"随身储物箱"}
  }
]
```

物品外形 `width/height/shapeRows` 决定它在背包中占多少格；`container` 中同名字段决定内部可存多少格。两套尺寸独立，内部宽高均为 1..12 的整数。省略内部 `shapeRows` 即为实心矩形；填写时行数等于内部高度、每行长度等于内部宽度，字符只有 0/1，且至少有一个 1。

例如 `"container":{"width":3,"height":2,"shapeRows":["111","110"]}` 表示内部五格，右下角不可放置。

## C# 最小示例

在 MelonMod 的初始化阶段提交；此处只登记声明，不提前创建窗口或访问玩家。

```csharp
var result = ItemRegistry.Register(new ItemDefinition {
    Id = "author.storage.box", ModId = "author.storage",
    Name = "随身储物箱", Width = 2, Height = 2, Value = 80,
    Container = new ContainerDefinition {
        Width = 4, Height = 3, Title = "随身储物箱"
    }
});
// Accepted 表示已排队；目录事件后查询 Applied，再通过正常游戏流程取得物品。
```

## 字段与规则

| JSON / C# | 默认与限制 | 原生用途 |
| --- | --- | --- |
| width / Width | 必填，1..12 | 内部网格宽度 |
| height / Height | 必填，1..12 | 内部网格高度 |
| shapeRows / ShapeRows | 可选，严格匹配内部尺寸 | 声明1=可用格；适配层反转为原生1=禁止格遮罩 |
| title / Title | 省略使用物品名称；提供时非空、最多200字符、无控制字符 | PixelWindow 标题 |
| draggable / Draggable | true | PixelWindow 构造参数 canMove；控制窗口移动 |
| allowedTags / AllowedTags | 默认空，最多64个唯一 ID | 原样传入 ContainerHelper 的 allowedTags |
| allowedIds / AllowedIds | 默认空，最多64个唯一 ID | 原样传入 ContainerHelper 的 allowedId |

过滤 ID 区分大小写，每项1..160字符，允许字母、数字、下划线、短横线和以点号连接的多段名称。`allowedTags` 指原生 helper 识别的标签，不能把 tooltip 显示标签或分类名称直接当成已确认的准入标签。`allowedIds` 是真实物品 ID，可引用原版或已存在的跨包物品；作者负责目标存在，跨包清单按需写 dependencies。

两个过滤列表不能同时非空；均为空时两个参数均传 null。有过滤时两个参数均为非 null，未配置的一类传空集合。当前原生 helper 的两类条件按 OR 组合，null 会开放对应分支；0.1.18 已在原生实例谓词和拖放中验证 ID 过滤。框架保留原生准入委托。示例“EZapi 敷料收纳盒”只传两种示例敷料 ID，便于实测过滤。

`container` 与 `food`、`liquidContainer`、`activation`、`useCount` 互斥，也不能对该物品登记 `ItemActivation.Register`，避免覆盖原生使用回调。初版不开放预装物品、自动搬运、机器配方或自定义嵌套策略。声音可用既有 `sounds.containerOpen/containerClose` 字段。行为标记省略时保留 helper 默认；显式填写 false/0 会覆盖默认，初次开发请省略无关标记。

## 官方依据与对象寿命

官方 LearnerMod 的 `PurpleBloodSynthesizer.cs` 演示 `PixelWindow`、`GameGridInventory`、Attach 和 SetContentWindow 结构。储物 helper 的完整签名另由当前游戏包装程序集核对：

```text
GameGridInventory(int width, int height)
GameGridInventory(string shape, int width)
PixelWindow(bool canMove, string title)
PixelWindow.Attach(PixelElement) -> bool
GameItem.SetContentWindow(PixelWindow) -> GameItem
ContainerHelper.InitContainerItem(GameInventory, GameItem,
    Il2CppSystem.Collections.Generic.List<string> allowedTags,
    Il2CppSystem.Collections.Generic.List<string> allowedId)
```

实现集中在 `src/EZapi/NativeContainerAdapter.cs`。工厂每次创建新的网格、窗口和过滤列表；子库存标识稳定为 `物品ID.storage`。同类物品使用相同声明标识，但对象各自独立，不使用静态窗口、玩家或库存缓存。结构完成后调用原生 helper，不改全局窗口、拖拽、交易或保存流程。原生窗口关闭和销毁的实际效果仍需玩家操作确认。

读档时仍通过同一物品工厂重建空结构，已有内容恢复交给原生存档流程。本版未实现容器数据迁移；已有装物品的容器不要随意改变 ID、内部尺寸、mask 或准入规则，先在测试档验证迁移方案。

## 玩家验证

1. 重启游戏，确认 EZapi 0.1.9、十件 Applied；调试面板生成两个“EZapi 储物箱”。
2. 用游戏原生容器操作分别打开，给两个箱子放入不同物品；开关、移动窗口和拖出物品，确认两者内容独立。容器入口无法打开时停止后续测试并导出诊断。
3. 生成“EZapi 敷料收纳盒”，尝试放入一次性/三次伤口敷料和其他普通物品，记录原生接受或拒绝结果。
4. 检查原生是否阻止非法自包含/嵌套；EZapi 不额外放开这些规则。
5. 关闭窗口、外出回店、正常过夜保存再重载，核对物品数量、位置、内容和窗口恢复；不应复制、丢失、串箱或残留拖拽框。
6. 异常时记录最后一步，导出 `UserData/EZapi/diagnostics.json`，结合 MelonLoader/Latest.log 和 Player.log 定位。调试“校验原生属性”只创建并销毁临时空容器，检查图结构、指针独立、尺寸和 helper 委托，不执行拖放或保存。

使用 CLI `--pack/--packs` 检查声明；声明错误看 `EZAPI_ITEM_CONTAINER`，图结构创建失败看 `EZAPI_CONTAINER_GRAPH`。新增储物能力没有增加 Harmony 补丁。

0.1.18 修复了内部遮罩反转与过滤空值适配；新内容包建议最低版本0.1.18。旧版创建且已保存的异形库存是否迁移尚未自动处理；不能在载入后无条件覆盖玩家现有形状。实际证据见 native-compatibility-0.1.18.zh-CN.md。

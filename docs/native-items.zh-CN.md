> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

> 0.1.58：重复实例保护保留每个工厂最近一次成功返回对象的原生包装引用，阻止回收后的地址复用造成误拒绝。下一次成功创建替换引用，框架停止时清空；最多每工厂一个，不保留全部创建历史。仍须返回全新游离实例，不可缓存返回同一物品。

# DLL 工厂、JSON 模块与节点

EZapi 是独立前置。此实现复用官方 `ModHook.OnModItemDirectoryInit` 和 `ModItemDirectory.Add`，不加载 Forge。初始化时提交声明，游戏目录事件中逐项应用，游戏实际需要物品时才调用工厂。所有种类统一进入官方 Mod 目录；`Amenity` 表示生活用品的逻辑分类，不是生产机器，也不自动获得原生生活用品商人的进货资格。

## 先选入口

| 需求 | 入口 | 文件 |
| --- | --- | --- |
| JSON 生成普通物品、食品、储物/液体容器、模块、节点 | `JsonItems.Register` / 内容包 items.json | items.schema.json |
| 用 C# 构建完整自定义物品 | `ItemApi.RegisterItem` | NativeItemExamples.cs |
| 用 C# 构建节点 | `ItemApi.RegisterNode` | 工厂调用 `ModuleEffectHelper.InitNode` |
| 用 C# 构建生活用品 | `ItemApi.RegisterAmenity` | 工厂自己实现使用行为 |
| 用 C# 构建一个/一批模块 | `RegisterModule` / `RegisterModules` | 工厂调用 `ModuleHelper.InitModuleItem` |
| 检查声明应用与工厂执行 | `Snapshot` / `FactorySnapshot` | 两种快照只含托管数据 |
| 运行中生成原版或 Mod 物品 | `Exists` / `Create` / `GetDisplayName` | 必须已载入当前周目 |

ID 使用小写命名空间，例如 `author.tools.sensor`，并属于 owner 的 `owner + "."` 前缀。JSON 与 DLL 共用 ID 空间，双向冲突均拒绝。不得使用 EZapi 内部保留的 `ezapi.debug` 命名空间。

## C# 工厂的责任

在内容 Mod 的初始化回调中提交；EZapi 的加载优先级为 -10000。每个工厂返回独立、未放进库存、有效正件数且 identifier 等于注册 ID 的 `GameItem`。不要缓存实例，不要返回玩家现有物品，不要在工厂里保存游戏、发放物品或递归生成自身。工厂可以在读档重建时运行，不能要求此时店铺 UI 或玩家库存已经存在。

`RegisterModules` 每批最多 500 项，自定义工厂总计最多 4096 项。整批声明预检后才入队；任一重复或无效条目都拒绝整批。调用方之后修改提交数组，不改变已接收条目。工厂委托自己的闭包仍由作者负责。

`NativeItemOptions` 是不可变 DTO：Name、ShortDescription、FlavorText、SourceName、ShowSource 提供固定文本；LocalizedName、LocalizedShortDescription、LocalizedFlavorText、LocalizedSourceName 可覆盖对应文本并提供中英两份。null 保留工厂原值，说明的空串明确清空。来源行通过既有官方 tooltip 回调显示，不安装第二套 tooltip 补丁。没有提供名称时，调试列表使用 ID，实际物品名称仍取工厂结果。

```csharp
var options = new NativeItemOptions(
    LocalizedShortDescription: new("", ""), // 中英文简介均明确清空
    LocalizedFlavorText: new("中文风味", "")); // 中文显示，英文明确清空
```

这条空串约定适用于 DLL 的可选简介和风味，单侧 null 回退另一侧，两侧均 null 表示未提供覆盖。0.1.86 修复了“注册允许空说明，已有物品刷新却报 `readable text <=8192 required`”的不一致：工厂创建和刷新沿用相同内联语言规则，不把空串变成回退文字。名称、来源名仍须非空白，说明每侧仍最多 4000 字符。通用 `LocalizedItemText.Resolve` / `GameLanguage.Resolve` 的可读文本要求，以及 JSON/CSV 声明规则不因此放宽。当前 DLL 的真实悬停、切换语言和保存重载仍待实机验收。

`ShowSource` 默认 true；希望贴近原版时显式传 `new NativeItemOptions(ShowSource: false, ...)`，不手工复写游戏已有分类、价格或属性。原生短说明用于操作，风味用于叙述；完整的可选规则见[Tooltip 风格规范](tooltip-style.zh-CN.md)。官方 Late 之后仍有原生描述，来源并不保证在最终末尾。

双语文本按游戏当前 locale 选择：`zh` 开头（例如 `zh-CN`、`zh-TW`）选中文，其它 locale 选英文；某一侧缺失时用另一侧。0.1.87 起通过 `GameLanguage.Current` 核对主线程、官方 `public static string LocHelper.GetCurrentLocaleCode()` 签名及返回值，不再由整份 `Assembly-CSharp.dll` 的 SHA 精确门控；接口不可用或读取失败时本次回退英文，下次展示可以恢复。`ItemApi` 工厂的 Options 名称、简介与风味在创建时写入，查询显示名和来源时重新解析。已 Applied 的 DLL 工厂声明及 `ItemDefinition`/JSON 物品均可按显示/Tooltip/重开窗口触发本地化刷新，保护玩家改名，不扫描全部库存。刷新钩子保留准确签名、原生入口和补丁所有者检查，见[游戏语言指南](game-language.zh-CN.md)。

```csharp
new NativeItemOptions(
    LocalizedName: new("检测模块", "Sensor module"),
    LocalizedShortDescription: new("通用模块", "Universal module"),
    LocalizedFlavorText: new("扫描附近目标。", "Scans nearby targets."),
    LocalizedSourceName: new("工具示例", "Tools example"));
```

工厂异常、null、错误 ID、无效对象、已入库存对象、同工厂紧接着复用同一实例，以及缺少 NODE/MODULE 原生类型，均被隔离并返回 null，不让托管异常跨进 IL2CPP。当前游戏可能把失败结果替换为沿用请求 ID 的 UNKNOWN 占位物，因此 `Items.Create` 还核对本注册表的工厂执行错误，拒绝并销毁游离占位实例。此前一次实例检查不是完整的实例池检测，作者仍必须遵守“每次独立创建”的契约。

作者抛异常之前应自行销毁尚未返回的新实例。框架只有在核对当前 ID、未附着且不是上次返回实例之后，才接管后续包装失败的销毁；不会销毁错误 ID、玩家物品或复用对象。此类错误返回的清理由作者负责。外部原生调用方收到 null 后的处理取决于游戏本身；发布前必须检查工厂。

已注册工厂贯穿进程与读档重建，不能单独撤销，因为存档中的 ID 仍需要它。EZapi 停止时清空托管声明和计数、使旧委托惰性；仍被原生目录引用的委托接收器保留到进程退出，不强删其它 Mod 的目录条目。修改定义需要重启游戏。

## JSON 模块和节点

内容包建议设置 `minEzapiVersion: "0.1.20"`，并声明所需能力 `items.module` / `items.node`。旧运行时会拒绝未知字段，避免静默生成没有行为的物品。

可复制的完整数据包在 [examples/author.tools](examples/author.tools/pack.json)，物品定义见 [items.json](examples/author.tools/items.json)。复制整个 author.tools 文件夹到 Mods/EZapiPacks 后重启；此包不会自动发放，进入测试档从调试列表生成。没有自定义 PNG 时使用框架占位图。C# 低层工厂须自行提供形状、图像与使用行为；常规 PNG 物品可直接使用 ItemRegistry.Register 的声明式入口。

```json
[
  {
    "id": "author.tools.sensor",
    "name": "检测模块",
    "width": 1,
    "height": 1,
    "value": 100,
    "module": {
      "type": "MODULE_TYPE_UNIVERSAL",
      "performance": 10,
      "efficiency": 5,
      "quality": 3,
      "isShipModule": false,
      "additionalEffectDisplay": "",
      "tier": 2
    }
  },
  { "id": "author.tools.node", "name": "效果节点", "node": true }
]
```

module 的八个参数逐项传给当前构建的官方 `InitModuleItem`，三个属性的单位为百分比加值。EZapi 的配置边界为 -100000..100000 整数、tier 1..100、说明最多 4000 字符；这是框架输入边界，不宣称官方支持任意极端数值。原生类型使用大写键，自定义类型使用小写命名空间；自定义模块类型的机器准入由 `ModuleApi.RegisterAdmission` 另外配置。`node: true` 调用官方 `InitNode`，本身不创建自定义效果或效果随机池。

module 与 node 二选一；不能与食品、液体/储物容器、激活行为、使用次数混合。帮助函数管理的 MODULE_*/NODE_* 标签禁止在 tags 中覆盖。原生 helper 的默认使用/激活标记保持原值，只有显式配置的原生字段才覆盖。显示说明与来源仍走已有物品属性规则，不重复追加原生效果说明。

## 状态和验证

Accepted 表示声明已接收，Snapshot 中 Applied 才表示目录 Add 成功；原生 Add 失败逐项报告，不伪称整批目录写入能回滚。FactorySnapshot 的 Created/Failures/LastError/Creating 单独报告工厂结果。某次创建失败不改变已经存在的目录注册状态；下一次仍可重试工厂，成功后清除 LastError 并保留累计失败数。

调试面板“物品工厂检查”集中创建六条路径（四种 DLL 工厂、JSON 模块、JSON 节点），检查后销毁。新增声明也可从列表手动生成到测试档主库存；此操作会改变玩家物品，通常验收应先使用临时检查按钮。六条临时路径的通过不证明跨夜保存、机器生产或商人售卖；实机范围见本版验收文档。

当前物品字段、夜间供应策略和 NPC 货源选项已有独立实现，分别见[物品供应](item-stock.zh-CN.md)、[NPC 货位](npc-stock.zh-CN.md)与[参考功能覆盖](FORGE-PARITY.zh-CN.md)。不要把早期版本的 partial 状态沿用到当前功能清单；当前 DLL 的物品创建、已有实例语言刷新和商店行为仍须实机验收。

0.1.44 的正式项目构建通过，39 个离线测试项目全部通过；物品注册/双语文本专项为 90/90。专项范围与边界见 结构化结果（历史标识 `verification/native-item-localization-0.1.44.json`；原件未公开）。这不能代替游戏进程内的语言切换、物品渲染和 tooltip 验收。

> 当前开发指南。见[统一版本与验收状态](current-status.zh-CN.md)。本次更新文档与消费方示例；运行 DLL 未变。示例离线校验与编译通过不等于游戏内视觉验收。

# 物品 Tooltip：可选的原版风格规范

开发者先选择下面一种风格。希望贴近 Probably Stolen 原版时，遵守“原版风格”；需要显示所属 Mod、自定义状态时，选择“扩展信息风格”。两者使用同一套现有 API，**不是新增 `tooltipStyle` JSON 字段**，也不强制改变已有 Mod。

| 选择 | 原版风格 | 扩展信息风格 |
| --- | --- | --- |
| 说明位置 | `descriptionPlacement: "native"` | 通常也用 `native`；确有需要才用官方 early/late 追加 |
| Mod 来源 | 显式 `tooltip.showSource: false` | 可用 `true`，显示本地化来源名 |
| 自定义行/标签 | 默认不添加；必要的独有玩法状态只显示一次 | 可用 `tooltip.lines`、`tooltip.labels` 或模块文本回调 |
| 短说明与叙述 | 功能说明用 `shortDescription`，气氛/背景用 `flavorText` | 同样区分字段，避免一件事反复显示 |
| 分类、价格、真实属性 | 由游戏按分类、数值、helper 状态生成 | 同样保留游戏生成的内容 |

当前已发布 API 的 `ShowSource` 默认是 **true**；仅省略 `tooltip` 不会得到无来源的原版风格。为兼容已有 Mod，本规范不改变这个默认值。

开发包的起步工具提供直接选项（源码仓库中工具在 `distribution/`，SDK 中在 `developer-tools/`）：

```powershell
./developer-tools/New-EZapiMod.ps1 -Kind Json -ModId author.example -OutputDir ./MyNewMod -TooltipStyle Native
```

`-Kind Dll` 生成独立 C# 工程；`-TooltipStyle Extended` 开启来源行。生成器默认 Native，仅创建新目录，不安装、不发物品、不保存。默认分类是示例材料，开发者仍须按实际用途调整。

## 原版层级与字段

按用户提供的原版果汁截图及当前构建的 Tooltip 路径核对：名称在顶部，青蓝色方括号分类在其下，随后是估值/基础价值和适用的属性，叙述为留有间隔的灰绿斜体。不同类别还会有专用内容；并非每件物品都必须显示热量、等级或短说明。

| 层级 | 写什么 | 通过什么提供 |
| --- | --- | --- |
| 名称 | 一次准确的物品名称 | `name` / `localizedName` |
| 分类 | 游戏中真实适用的类别；可多项 | `types`，自定义类别另配 `typeNames`；由游戏绘制方括号和颜色 |
| 价值 | 真实基础价值、当前估值 | `value` 和游戏估值逻辑；不要在说明里手写另一份价格 |
| 玩法属性 | 食品热量、模块属性等适用状态 | 对应 `food`、`module` 等声明或官方 helper 初始化；不要只写一行文字冒充功能 |
| 短操作说明（可选） | 原生属性未表达的必要操作或效果 | `shortDescription` / `localizedShortDescription`；不重复名称和已有属性 |
| 风味叙述（可选） | 气氛、背景、物品来历的叙述 | `flavorText` / `localizedFlavorText`；原生路径负责斜体、色彩与间隔 |

字体、行宽、换行、边框、配色和适用项的最终顺序由游戏负责。不要手拼 HTML/字号/色码，不要为外观替换整个 Tooltip 窗口。`types` 会影响游戏语义，应按物品玩法选择；处理器型号、品牌或图片本身不能决定它是材料、模块还是其它类别。原版大写类型键由游戏翻译，自定义类型按[属性索引](item-properties.index.json)提供显示名。

`longDescription` 不是普通悬停 Tooltip 必然显示的正文，不能把必要操作只藏在这里。`customText` 在 native 模式是原生自定义文字字段，可能改变库存物品图标上的文字表现，不是普通简介；不要拿它填叙述。

## 图中处理器应如何调整

图中的“来源：深空当铺”是扩展信息，不是原版必需行；“等级 1 · Intel Core i3-370M”再次重复型号，也让正文的功能不明确。选原版风格时：关闭来源，按真实玩法补上适用分类；如果等级确实影响玩法并且原生没有显示，只保留一次有意义的等级说明。若是普通材料，不需要凭空加等级。背景句写入风味字段，不能把必要操作说明变成装饰性叙述。

下面示例把处理器**假定为普通材料**，仅演示文本结构，不代表用户现有处理器的真实玩法；没有使用效果、图像或发放逻辑。

```json
[
  {
    "id": "author.tooltip.processor",
    "modId": "author.tooltip",
    "name": "Intel Core i3-370M Processor",
    "localizedName": {"chinese": "Intel Core i3-370M 处理器", "english": "Intel Core i3-370M Processor"},
    "types": ["MATERIAL"],
    "width": 1,
    "height": 1,
    "value": 20,
    "descriptionPlacement": "native",
    "flavorText": "A trace of warmth lingers in the old silicon.",
    "localizedFlavorText": {"chinese": "旧硅片仍有余温。", "english": "A trace of warmth lingers in the old silicon."},
    "tooltip": {"showSource": false}
  }
]
```

可直接复制[完整 JSON 包](examples/tooltip-native/pack.json)与[物品文件](examples/tooltip-native/items.json)。在 SDK 根目录校验：

```powershell
dotnet tools/ezapi-validate/ezapi-validate.dll --pack docs/examples/tooltip-native
```

## DLL 调用方选项

[完整独立工程](examples/tooltip-dll/TooltipMod.csproj)与[Mod.cs](examples/tooltip-dll/Mod.cs)使用当前 DLL 的真实契约。声明式注册的关键配置为：

```csharp
new ItemDefinition {
    Id = "author.tooltip_dll.processor", Name = "Example processor",
    LocalizedName = new("示例处理器", "Example processor"),
    Types = new[] { "MATERIAL" }, Width = 1, Height = 1, Value = 20,
    DescriptionPlacement = "native",
    FlavorText = "A trace of warmth lingers in the old silicon.",
    LocalizedFlavorText = new("旧硅片仍有余温。", "A trace of warmth lingers in the old silicon."),
    Tooltip = new() { ShowSource = false }
};
```

使用 `ItemApi.RegisterItem/RegisterModule` 等原生工厂入口时，传入 `new NativeItemOptions(ShowSource: false, ...)`；短说明、风味由工厂原生字段或对应 Options 文本提供。不要给 `NativeItemOptions` 传入它没有的 `DescriptionPlacement`。

## 为什么来源可能在描述上面

官方文档允许 `OnCreateTooltipEarly/Late` 追加文字，但 **Late 不等于最终末尾**。当前构建在 Late 回调之后仍会绘制原生短说明、风味文本以及部分特殊内容。因此选择扩展信息风格时，来源行、labels 或 late lines 可能位于这些文字上方；不能承诺来源总在最后。

要贴近原版，优先 `native` 加关闭来源。`early/late` 会把 EZapi 声明的原生说明字段清空，再在相应事件追加这些文字，位置与原生间距可能不同。不要为了搬动来源而清空共享 builder、调用其 `Build`，或全局替换游戏 Tooltip；追加只处理自己的物品并保留其它 Mod 内容。详见[Presentation API](presentation.zh-CN.md)。

## 给 AI 的可复制要求

> 本 Mod 的物品选择 EZapi“原版 Tooltip 风格”。先阅读 docs/tooltip-style.zh-CN.md 与 tooltip-style.index.json，再按真实 schema/API 实现。使用 descriptionPlacement="native"、tooltip.showSource=false；DLL 对应 ItemDefinition.Tooltip.ShowSource=false，原生工厂用 NativeItemOptions(ShowSource:false)。名称只写一次；types 按真实用途选择，由游戏绘制分类。价格和已由原生 helper 展示的属性不在说明重复。必要操作放 shortDescription，背景叙述放 flavorText；无内容就省略，不拿 longDescription/customText 代替普通 Tooltip。不手写字体、HTML、色码、价格行或分类行。需要独有动态状态时只补一次真实信息。文本跟随游戏语言，中英分别提供或使用 CSV，稳定 ID 不翻译。提交时注明离线检查和游戏内视觉验收的不同范围。

## 验收与参考

离线：校验 JSON 包；将 DLL 示例引用同一份 EZapi 编译。游戏内：与同类原版物品并排悬停，检查分类、价格、属性、短说明与风味的层级，无来源/重复型号/重复数值；必要操作仍清楚可见，再检查中英、窄窗口、动态状态及读档后物品。当前新增示例未进行游戏内视觉或保存重载验收。

依据：[官方 ModdingGuide](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md)的物品字段与 Tooltip 事件、用户截图，以及当前框架的原生字段映射与当前游戏构建的只读路径核对。具体颜色和特殊类别顺序由游戏版本决定。美术规范另见[物品美术](item-art.zh-CN.md)；机器可读配方见[Tooltip 风格索引](tooltip-style.index.json)。

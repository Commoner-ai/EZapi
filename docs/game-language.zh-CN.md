> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi 当前游戏语言与中英互通

## 本期语言要求

本期优先提供并验收中文（`zh`）和英文（`en`），不将框架语言能力限制为中英。EZapi 玩家界面跟随游戏设置的当前语言，不新增独立语言设置，也不使用 Windows 语言或存档创建时的语言决定显示。游戏切换语言后，相关界面在下一次刷新或重新打开时重新解析文字；已有物品和读档物品也属于验收范围。示例和本期交付文案提供中英文本，框架不自动翻译作者的固定字符串。

CSV 自动解析保留对全部官方语言代码的支持：按游戏当前语言查询，有对应翻译就显示，缺译回退英文，再回退作者提供的可读文案。其它语言的完整文案及原生验收暂不作为本期交付要求。

> 历史验收记录（0.1.72），不作为当前 DLL 通过依据。
> 历史 0.1.72 DLL 曾记录415项中英原生检查，含已有及真实冷加载物品、Tooltip、容器/机器标题、阅读、工坊、对话、电话和供应商页面；本次供应商到达/离场由夹具主动触发，不代表自然到访或真实支付交付。证据见 0.1.72 保存/供应商原生检查（历史标识 `verification/disk-supplier-native-0.1.72.json`；原件未公开）。

DLL Mod 可以用 `GameLanguage.Current` 读取当前游戏语言快照，再用 `GameLanguage.Resolve` 解析双语文字或内容包 CSV。EZapi 通过当前已核对构建中的原生 locale getter 读取语言。EZapi 0.1.72 起，普通 JSON 物品的显示字段也可直接绑定所属包的 CSV 键；校验器会在整包入队前要求每个键都有英语文本。

```csharp
var language = GameLanguage.Current;
if (language.IsFallback)
    log.Warn(language.Reason ?? "Game locale unavailable; using English fallback.");

// 同一段 UI 使用同一个快照；下次打开/绘制再取 Current，才能跟随游戏切换。
var title = language.Resolve(new LocalizedItemText("维修顾问", "Repair adviser"));
var body = language.Resolve(new LocalizedItemText("需要了解什么？", "What would you like to know?"));
```

`Current` 只能在 EZapi 初始化后的游戏主线程读取。未知游戏构建、未初始化、其它线程、locale 缺失/非法或原生 getter 失败时，返回 `LocaleCode="en"`、`IsFallback=true` 和诊断 `Reason`。它不改变游戏语言，也不保留 Unity `Locale` 对象。

`GameLanguageSnapshot` 是当时读取的纯托管快照，不会自动刷新。图形绘制、菜单打开、电话联系人名和动态对话回调应在展示时重新取 `Current`；同一个页面内先取一次快照，再解析全部文字，避免切换语言时一页中途混用。不要在 Mod 静态初始化时缓存翻译结果。

`LocalizedItemText` 对 `zh*` 使用中文，其它 locale 使用英文；缺少选中一侧时回退到另一侧。需要法语、德语等多语种时使用 CSV：

```csharp
var catalog = Localization.GetPack("author.example");
if (catalog is not null) {
    var text = GameLanguage.Resolve(catalog, "repair_help", "Help unavailable", 3);
    Render(text.Text); // Text: 请求语言 → en → 自己提供的可读回退
    if (text.MissingKey || text.FormatError is not null)
        log.Warn($"repair_help: {text.FormatError ?? "translation missing"}");
}
```

CSV 使用游戏当前的精确 locale 代码，例如 `en`, `zh`, `fr`, `ptBR`。项目自己的 `Localization.Resolve(locale,...)` 仍是纯托管显式语言入口，可用于离线工具和无原生上下文的逻辑；需要跟随玩家设置时用 `GameLanguage.Resolve`。缺键回退必须是玩家可读文案，不要回退成内部 ID。

| 文本场景 | 接口 |
| --- | --- |
| 原生物品名称、简述、风味、来源 | `NativeItemOptions.LocalizedName`、`LocalizedShortDescription`、`LocalizedFlavorText`、`LocalizedSourceName` |
| 配方图鉴 | `RecipeGuideEntry` / `RecipeGuideFigure` 上的 `Localized*` 字段 |
| 升级/服务资格说明 | `RequirementDefinition.Message` |
| 对话、电话、供应商或自定义窗口中的动态文本 | `GameLanguage.Resolve(LocalizedItemText)`，或 `GameLanguage.Current.Resolve(...)` |
| 多于中英文的文本 | `LocalizationCatalog` + `GameLanguage.Resolve(catalog, key, fallback, args)` |
| 普通 JSON 物品名称、简介、风味、自定义文本 | `localizedName`、`localizedDescription` / `localizedShortDescription`、`localizedLongDescription`、`localizedFlavorText`、`localizedCustomText` |
| 分类、模块附加说明、来源、Tooltip、容器标题 | `typeNames[].localizedName`、`module.localizedAdditionalEffectDisplay`、`tooltip.localizedSourceName` / `labels[].localizedText` / `lines[].localizedText`、`container.localizedTitle` |
| DLL `NativeItemOptions` 等声明 | 使用内联 `LocalizedItemText(chinese, english)`；动态对话/电话/自定义 UI 在显示时调用 `GameLanguage.Resolve` |
| 普通 JSON 内容包的多语言资源 | `Localization/<locale>.csv`，由 `--pack`/`--packs` 整包校验；英文字段缺键会拒绝整个包 |
| 独立 JSON 机器布局的窗口和槽标签 | `localizedTitle`、`liquidInputs[].localizedLabel`、`output.localizedLabel`、`localizedBatteryLabel`、`localizedManualLabel`，见[机器布局中英指南](machine-layout-localization.zh-CN.md) |

JSON 的每个 `localized*` 对象可以提供 `chinese`、`english`，也可以提供所属包的 `key`。CSV 绑定可以同时带内联文本作为最终回退：`请求语言 → en → 内联文本 → 原字段`。例如：

```json
{
  "id": "author.example.storage_box",
  "modId": "author.example",
  "name": "储物箱",
  "localizedName": { "key": "storage_name" },
  "container": {
    "width": 4,
    "height": 3,
    "title": "箱内物品",
    "localizedTitle": { "key": "storage_title" }
  },
  "tooltip": {
    "lines": [{ "text": "容量 4×3", "localizedText": { "key": "storage_capacity" } }]
  }
}
```

`Localization/en.csv` 必须包含 `storage_name`、`storage_title`、`storage_capacity`。`zh.csv` 可以翻译这些键；其它已支持 locale 的 CSV 可以补充翻译，缺项时使用英语。普通文本键不接受格式占位符，因为物品字段没有对应的参数绑定。已有已应用物品在显示名称或重新打开容器时按当前语言刷新；EZapi 只刷新自己的已登记物品和框架写入的名称，不覆盖玩家自行改过的名称或外部物品。原生热钩子与真实语言切换仍须在游戏中验收，离线检查不证明 UI 或读档显示。

游戏内置 ModLoader 文档示例使用 `ModHelper.GetCurrentLocale().Identifier.Code` 与 `ModHelper.GetLocalized`。EZapi 的 MelonLoader DLL 接口保持独立，不伪造 ModHelper 或加载器 ABI；它在当前确切游戏构建中适配 locale getter，并保留英文失败回退。Mod 内容 CSV 的无表头格式和转义约束见 [localization.zh-CN.md](localization.zh-CN.md) 与 [官方 API 对照](official-api-map.zh-CN.md)。

已编译样例：[LanguageExamples.cs](../src/EZapi.Sample/LanguageExamples.cs) 与 [中英物品内容包](../src/EZapi.Sample/Content/items.json)。整包 CSV/字段映射及物品刷新有离线测试证据。上述415项中英原生检查、18项保存、27项新进程重载及37项磁盘回读均属于历史0.1.72 DLL，不能作为当前DLL验收。当前DLL的语言界面与保存重载仍待实机验收。

## 0.1.72 展示 API

对话、电话通话和供应商用 `Localized*` 属性；作者的动态回复用 `context.LocaleCode`，使结果与当前页使用相同语言。阅读面板的 `Open` 可直接传三个双语对象。工坊使用 `WorkshopSnapshot.ForGameLanguage(...)` 与各字段的 `Localized*` 属性，打开/刷新时解析。旧工坊固定 `English` 标志保留兼容；新 Mod 默认使用 `ForGameLanguage` 入口跟随游戏。完整字段与边界见 [对话/展示本地化索引](dialogue-localization.index.json)、[reading.zh-CN.md](reading.zh-CN.md) 和 [workshop.zh-CN.md](workshop.zh-CN.md)。阅读、工坊、对话、电话和供应商页面均包含在上述证据中；供应商验收范围见报告。

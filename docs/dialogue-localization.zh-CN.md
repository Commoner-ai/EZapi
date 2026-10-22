> 0.1.72-preview.1 封装变更：开发工具默认关闭，见[安装与调试](install-and-debug.zh-CN.md)。下述415项及保存重载报告属于记录哈希4238A189…的封装前DLL；新DLL不继承其实机通过状态。

# 对话、电话与供应商的中英文字

0.1.72 开发版提供可选的 `Localized*` 属性。脚本可在初始化时创建；每次真正打开原生对话图时，框架读取一次游戏语言并解析整张图。切换语言不会重建正在运行的业务回调；关闭后重新打开原脚本，或供应商选择继续交易后，新图使用新语言。字段清单见 [机器可读索引](dialogue-localization.index.json)，完整签名见 [DLL 元数据](public-api.index.json)。

```csharp
var script = DialogueApi.Create("author.example", new DialogueDefinition(
    "author.example.hello", "访客", "你好", new[] {
        new DialogueChoice("about", "了解", "查看介绍", context =>
            new LocalizedItemText("欢迎！", "Welcome!").ForLocale(context.LocaleCode)!) {
            LocalizedLabel = new("了解", "About"),
            LocalizedPreview = new("查看介绍", "Read the introduction")
        }
    }) {
    LocalizedSpeaker = new("访客", "Visitor"),
    LocalizedBody = new("你好", "Hello")
});
```

`zh*` 选择中文，其它游戏语言选择英文；仅提供一侧时使用已有的一侧。已提供文字必须可读且不能空白。说话人、选项和按钮最多 512 个 UTF-16 字符，正文、预览和失败提示最多 131072；双语字段额外拒绝除换行、回车、制表符以外的控制字符。`Localized*` 有效时对应旧字面量可为 `""`，仍不能是 null。每个双语对象创建后不可变，脚本复制只读选项集合。

`DialogueDefinition` 支持说话人、正文、确认、返回、离开、稍后、失败提示、前后翻页。`DialogueChoice` 支持选项、预览和独立确认按钮。原来默认的中文导航与失败提示在英文环境自动显示英文；自定义字面量保留。因为旧构造函数无法区分“省略默认值”和“显式传相同文字”，字面量恰好等于内置中文默认值也按内置默认处理；要固定该文字，可显式写 `LocalizedConfirmLabel = new("确认", "确认")`。作者正文不会被自动翻译。

动态确认回复使用 `context.LocaleCode`，与当前页语言一致。需要 CSV 时用 `catalog.Resolve(context.LocaleCode, key, readableFallback, args).Text`。这些 DLL 声明没有所属内容包目录，因此不接受 `LocalizedItemText.Key`；请显式解析目录。不要在初始化时调用 `GameLanguage.Resolve` 把整张脚本永久固定成当时语言。

电话通话共用该对话图；联系人名称仍由 `PhoneContact.DisplayName` 回调提供，可在其中调用 `GameLanguage.Resolve(new LocalizedItemText(...))`。电话面板重开和现有刷新流程会重新查询名称。可编译双语示例见 [DialogueExamples.cs](../src/EZapi.Sample/DialogueExamples.cs)。

供应商的 `SupplierDefinition` 提供说话人、确认、返回、继续、离开、失败、前后翻页；`SupplierPage.LocalizedBody` 提供每次捕获的正文；`SupplierOffer` 提供选项、预览与独立确认。`Capture` 上下文带捕获时语言，`Purchase` 上下文带实际图的语言；入队和到访可能不在同一语言。使用 `Localized*` 声明可在到访时正确解析。继续交易会重新捕获数据并打开新图。购买抛异常时，框架记录 `EZAPI_SUPPLIER_PURCHASE`，沿用本图失败提示，避免切换语言后混排。可编译示例见 [SupplierExamples.cs](../src/EZapi.Sample/SupplierExamples.cs)。

本地化不更改 ID、当前周目许可、确认至多一次、自动报价规则或资源回收边界；不会执行支付、交付、保存，也不会自动翻译作者的返回文字。当前有链接真实生产源码的离线测试；当前 DLL 已通过415项中英原生检查，含已有及真实冷加载物品、Tooltip、容器/机器标题、阅读、工坊、对话、电话和供应商页面；本次供应商到达/离场由夹具主动触发，不代表自然到访或真实支付交付。见[当前DLL证据](verification/disk-supplier-native-0.1.72.json)。

## 阅读与工坊入口

阅读面板可传三个 `LocalizedItemText` 作为标题、正文和署名；调用时一次读取语言，创建原生纸条之前解析。工坊使用 `WorkshopSnapshot.ForGameLanguage(...)` 和各文本的 `Localized*` 属性，在打开/刷新时按当前语言更新。旧工坊 `English` 标志保留固定语言兼容。详见 [reading.zh-CN.md](reading.zh-CN.md) 和 [workshop.zh-CN.md](workshop.zh-CN.md)。

# 原生阅读面板

`ReadingApi` 使用当前游戏的 `HandnoteUIManager`，通过 `DirectoryMaster.Item("handnote", true)` 创建游离纸条，并设置官方 `HANDNOTE_TITLE`、`HANDNOTE_BODY`、`HANDNOTE_SIGN` 标签。它不把纸条交付到背包，不保存纸条，也不更改游戏读档内容。标题、正文、署名沿用游戏字体、布局及富文本行为；长文本不会自动分页，应由作者分段。

从当前主线程 `LifecycleApi` 回调的 `context.Run` 取得 `RunId` 和 `SlotId`，再传入当前槽位和周目。可编译示例见 `../src/EZapi.Sample/ReadingExamples.cs`；真实完整签名、默认值和 DTO 见 `API.index.zh-CN.json`。`IsAvailable` 只表示框架和五个必需钩子就绪；当前没有有效店铺/周目或面板时，`Open` 仍返回 `Unavailable`。不要把运行时能力探测当成展示成功。

```csharp
var result = ReadingApi.Open("author.example", "author.example.welcome",
    context.Run.RunId, context.Run.SlotId,
    new LocalizedItemText("标题", "Title"),
    new LocalizedItemText("第一行\n第二行", "First line\nSecond line"),
    new LocalizedItemText("署名", "Author"));
if (result.Status == PresentationStatus.Opened) {
    // 本次同步调用已核对原生可见面板、纸条指针和三个实际 TMP 文本。
}
```

owner/document ID 必须是小写 ASCII 命名空间，使用 `[a-z0-9_-]` 和点分段，各至多 160 个字符；文档 ID 必须以 `ownerId + "."` 开头。标题、正文、署名分别最多 512、131072、4096 个 UTF-16 字符，字符串重载允许空串，拒绝 null/NUL。当前周目 ID 不可空，槽位不可为负。ID 和参数错误返回 `Invalid`，不分配原生纸条。

双语重载在调用时读取一次游戏语言，并在创建原生纸条前解析标题、正文和署名；`zh*` 选择中文，其它语言选择英文，缺失一侧回退到已有一侧。双语字段必须非空白。`LocalizedItemText.Key` 不适用于独立阅读声明；需要 CSV 时请显式调用 `GameLanguage.Resolve` 后使用字符串重载。语言更改后再次打开即可使用新文本。

已显示的原版或其他作者纸条优先；任何作者都不能通过 EZapi 抢占或关闭另一个作者的阅读。只有同作者、同文档、同载入身份的可见纸条可以刷新。刷新先创建独立实例，确认原生窗口切换后才释放旧纸条；原生拒绝刷新时保留仍显示的旧纸条。原版直接调用 `OpenPanel` 接管后，下一维护阶段仅释放 EZapi 自己的临时对象。

`Close(ownerId, documentId)` 只关闭匹配的自有面板；已无自有实例时返回 `Closed`，此时其他窗口可能仍可见。`Closed` 不代表全局所有阅读都已关闭。错误作者、拖拽或外部窗口优先返回 `Conflict`；原生异常、拒绝操作或写后状态无法确认返回 `Indeterminate`。不要遇到 `Indeterminate` 就自动重复打开。

用户关闭、周目/槽位/载入 Epoch 变化、场景卸载或返回菜单时清理自有实例。清理可以在周目失效后执行，不再调用要求有效周目的库存查询。面板拒绝关闭时继续持有仍展示的纸条；检查临时失败时最多保留新旧两份，阻止继续分配，后续成功检查才协调释放。销毁出现异常时记录 `releaseFailures` 并停止后续分配，等待重启；不自动重试可能已部分完成的 Destroy。日志和诊断如实保留故障，不宣称释放成功。

`PresentationStatus` 是展示家族共用状态：本入口使用 `Unavailable/Invalid/Conflict/Indeterminate/Opened/Closed`；`Queued/Arrived/Dismissed` 为其它展示能力保留，不代表那些功能已经实现。`PresentationResult` 的 `Reason` 给出具体错误代码。当前阅读为 C# 运行时入口，尚无阅读 JSON 声明或异步线程入口。

调试面板提供“原生阅读检查”“看纸条”“关纸条”。检查按钮执行真实公共 API、直接读取三个原生 TMP 字段并核对外部接管、刷新和释放；“看纸条”用于人工观察和返回菜单/重载验证。诊断中的 `reading`、`readingDebug`、`readingLifecycleDebug` 记录五个钩子、资源数、错误和生命周期证据。只加载 EZapi 的测试覆盖直接原生接管；尚未验证真实第三方 Mod 的补丁顺序组合。

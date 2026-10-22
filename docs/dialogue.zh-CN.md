> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 原生对话

`DialogueApi.Create(ownerId, definition)` 校验并复制只读脚本，不持有游戏对象，可在初始化阶段创建。`Open(ownerId, requestId, runId, slotId, script)` 在当前已初始化的游戏主线程展示游戏自己的对话；`Close(ownerId, requestId)` 只关闭匹配请求。可编译示例：`../src/EZapi.Sample/DialogueExamples.cs`。精确公共签名、默认参数和 DTO 以 `API.index.zh-CN.json` 为准。

## 最小用法

```csharp
var script = DialogueApi.Create("author.example", new(
    "author.example.greeting", "示例访客", "需要了解什么？",
    new[] { new DialogueChoice("about", "了解 Mod", "确认后显示介绍。",
        context => "欢迎使用我的 Mod。") }));
var result = DialogueApi.Open("author.example", "author.example.request",
    context.Run.RunId, context.Run.SlotId, script);
```

只在真实用户动作或当前生命周期回调中展示，不在每帧反复调用。`IsAvailable` 表示线程/框架/必需钩子完整，不代表没有其它对话或当前周目已就绪。`Opened` 核对 `StartDialogue` 返回值和当前原生图身份，正文打字和选项出现仍由原生异步流程负责；业务成功以作者自己的结果为准。`Conflict` 尊重原版/其它作者对话、拖拽及同栈重入，`Unavailable` 等待当前周目/界面/钩子就绪，`Invalid` 修正参数；`Indeterminate` 查看日志和资源状态，不能自动重放扣款、发放或保存。

## 定义与回调

owner/definition/request 使用小写 ASCII 点分命名空间；每个 ID 最多 160 字符，definition/request 必须以 owner 加点开头。1..128 个选项，选项 ID 唯一、合法且最多160字符。Speaker 和六个按钮标签非空、最多512个UTF-16字符，Body/Preview最多131072字符（可空），FailureText非空且最多131072字符；全部拒绝 null/NUL。回调必须非空；返回非 null、无 NUL、最多131072字符的回复。

默认先预览，再确认；返回、翻页、稍后再说均不执行业务。`RequireConfirmation=false` 时直接点击选项确认；`AllowLater=false` 隐藏根菜单的稍后选项。根据当前原生按钮数分页，不扩容或克隆共享按钮；预览返回到原选项所在页。每次选择先显示 `PLAYER` 过渡行，再进入目标页面，沿用游戏原生流程。

确认仅在精确原生 `SelectChoice(int)` 调用栈内、来源节点和索引匹配、当前作者/请求/周目/槽位/载入 Epoch 有效时放行一次。直接 Invoke 委托、旧委托、递归选择和重复确认不执行业务。进入作者回调前先消费执行权；异常也不自动重试，显示 FailureText 并记录 `EZAPI_DIALOGUE_CALLBACK`。作者必须自行处理支付、交付、回滚与持久业务幂等；Generation 只标识本次展示，不能作为跨保存的业务键。作者代码不能在确认回调中强制读档或切换场景；游戏自己的调用栈仍在运行。

脚本可跨读档复用；原生展示实例与回调不可跨周目复用。回调后再次核对周目与所有权，失效时不再写旧结果页。原生 SelectChoice 在回调返回后仍会读取当前节点的选项；自有选择调用栈内直接请求外部 StartDialogue 会返回 false，避免原生读错节点。需要展示后续外部对话时，在下一帧显式请求；框架不自动重放该请求。

## 生命周期与兼容边界

同一时间一个自有原生图。原版或其它 Mod 已占用时返回 Conflict；外部直接接管后旧回调失效，只释放自有节点和游离 choiceItem，不关闭外部对话或清空共享列表。当前图、未完成正文、延迟结束列表以及它们的 next/choice 链仍引用自有节点时保留资源；遍历超过4096个节点时保守保留并报错。原生待推进/待结束标记有效期间也保留资源，阻止新图分配；activeCoroutines 列表保留的已完成句柄不当作仍在运行。释放异常停止新分配，不自动重试可能已部分完成的 Destroy。

临时 handnote 只作选项令牌，不进入库存，不保存。`Dialogue` 为原生普通对象，拆除自有引用后交由原生 GC；只有游离 GameItem 调用 Destroy。读档、新局、返回菜单、场景卸载先撤销业务许可，再中断自己的图。若仍有原生引用，诊断如实保留数量，不假称已释放。

当前游戏普通对话的 ContinueConversation 会检查实体顾客；原生协程包含内联推进逻辑，独立对话在本次展示期间租用 isSecMonologuePlaying 通道；结束、关闭、失效或外部 StartDialogue 进入原方法之前归还本次设置的模式，不创建实体顾客。该模式仅为独立对话暂用，测试必须核对归还，不能把同步方法补丁当成整个异步流程已隔离。DisplayClientText 的窄范围 Postfix 只为当前自有图同步说话人 TMP；不全局替换原版姓名逻辑。

C# 接口目前无 JSON 对话导入器。Phone、ClientVisit 与 Supplier 共用托管对话脚本；供应商指南见 [suppliers.zh-CN.md](suppliers.zh-CN.md)。0.1.72 的可选 `Localized*` 字段和 `DialogueContext.LocaleCode` 用法见[对话中英指南](dialogue-localization.zh-CN.md)。只加载 EZapi 的测试可验证直接原生接管；真实第三方补丁顺序组合仍需对应安装环境验证。

调试按钮：原生对话检查（跨帧原生流程）、看对话（人工鼠标预览/返回/翻页/确认）、关对话。`dialogue` 记录钩子、拥有者、临时资源、许可及释放故障；`dialogueDebug` 和 `dialogueInteractive` 分别记录自动与鼠标验证状态。测试不写游戏存档。

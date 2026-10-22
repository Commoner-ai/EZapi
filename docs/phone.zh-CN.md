> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 电话联系人

使用 `PhoneApi.Register(ownerId, contact)` 登记联系人；EZapi 负责将当前可见的联系人放入游戏自己的通讯录、接入原生拨号，并用 `DialogueScript` 展示通话。可编译示例见 `../src/EZapi.Sample/PhoneExamples.cs`；精确签名及 DTO 见 `API.index.zh-CN.json`。本接口不需要依赖 Forge。

```csharp
var script = DialogueApi.Create("author.phone", new(
    "author.phone.help", "维修顾问", "需要帮助吗？",
    new[] { new DialogueChoice("help", "查看帮助", "确认后显示说明。", c => "欢迎咨询。") }));
var result = PhoneApi.Register("author.phone", new(
    "author.phone.contact", 9000000012,
    context => true,
    context => "维修顾问",
    context => script));
```

登记一次即可，可在已初始化框架的游戏主线程、原生目录建立前登记。`Queued` 表示声明通过校验；`Snapshot()` 中 `Applied=true` 才表示最近一次同步已核对当前原生目录，`Visible` 表示最近一次查询结果。`IsAvailable` 表示线程、框架和十四个必需钩子完整，不代表当前有周目、电话已空闲或所有联系人已应用。

owner/contact ID 使用小写 ASCII 点分命名空间，contact ID 必须以 owner 加点开头，每个最多160字符；号码为 100000..9999999999，不能重复号码或联系人 ID，同一进程最多128个声明。号码被原版/其它 Mod 占用时，当前条目标记 `EZAPI_PHONE_NUMBER_CONFLICT`，保留对方对象及拨号行为，不按号码强行接管。

`PhoneContext.Generation` 是当前载入 Epoch，另带 RunId/SlotId；不能当作跨保存业务键。Visible、DisplayName 都是只读查询：后台每秒最多一次，另外在打开电话/通讯录和准备实际呼叫时同步。DisplayName 必须非空、无 NUL、最多512个UTF-16字符；隐藏联系人不会求名称。查询异常时隐藏本联系人并移除自身目录槽，不影响其它条目。不要在这些查询中登记/释放、切场景、读档、扣款或展示 UI。

Call 只在游戏真实 `StartPhoneDialog(long)` 调用栈内，对已绑定的确切联系人放行一次。它必须返回相同 owner 创建的不可变 DialogueScript；可以随当前进度选择不同脚本。直接查询原生 GetCallDialog 不执行作者 Call，也不分配对话图。Call 是准备展示的工厂，不是业务确认入口；不要在这里修改资源。作者代码结束后再次核对周目、目录、原生管理器和界面占用，发生变化就拒绝创建旧图。

通话使用游戏独立的 `DialoguePhoneUIManager`，店铺对话使用 `DialogUIManager`；共用脚本校验、分页规划和原生图构建，分别持有选择许可、原生实例与释放状态。预览、返回、翻页不执行业务；Confirm 只在真实原生选择栈内至多执行一次。异常显示脚本 FailureText，不自动重放。直接 Invoke 旧委托、嵌套 Invoke、关闭后或旧载入的确认无效。业务支付、交付、回滚和持久幂等由作者实现，见 [对话指南](dialogue.zh-CN.md)。

“离开”和“稍后再说”使用原生 `OnPhoneDialogEnd` 同步归还确切通话身份；原生文字结束本身不保证挂断电话。选择栈返回后才中断、释放自有图，不在仍读取选项的调用栈里清空节点，不排程可能误挂下一通电话的延时回调。外部通话/外部图接管优先；自有选择栈内直接替换原生电话对话会被拒绝，调用方可在下一帧显式请求。

`Unregister(ownerId, contactId)` 撤销本作者声明和正在使用它的确认权，只删除当前周目/字典中指针完全匹配的目录对象。对象被外部替换时不删除对方；原生拒绝中断或外部链仍引用图时返回 Indeterminate，保留图，后续检查解除引用后再释放。重复释放已不存在的声明返回 Closed。创建/查询/选择调用栈内不允许登记或释放。

读档、新局、返回菜单和卸场景使旧图许可失效；下一有效周目重新同步同一组进程声明。游戏目录中已经保存的相同稳定 `ezapi.phone.<contactId>` 标记可以在当前周目重新绑定，不按旧指针跨载入复用。本版不自行保存、不提供联系人存档迁移或模组卸载清理历史存档；实机保存重载的序列化场景须另有证据，不能依据重新登记成功推断已验收。

释放规则与独立对话一致：仅 Destroy 自有、游离的选择令牌，不放入玩家库存；普通 Dialogue/StorePhoneClient 对象交给原生 GC。当前节点、未完成正文、next/choice 外部链和原生待推进标记仍引用图时继续持有，阻止下一图分配；遍历超过4096节点保守保留。Destroy 部分失败后停用新分配，不自动重试。没有清空共享字典、共享按钮池、全局对话工厂或原版联系人。

调试按钮为“原生电话检查”“看通讯录”“移除联系人”“生命周期拨号”；最后一个直接走原生拨号入口，用于暂停菜单下检查活动图返回菜单的释放，不恢复时间、不改变菜单。诊断字段 phone、phoneDebug、phoneInteractive。仅在可丢弃测试档使用；调试入口不会自动保存。当前为 C# 联系人 API，没有电话 JSON 导入器；第三方补丁顺序与联机组合需要对应环境的实际验证。

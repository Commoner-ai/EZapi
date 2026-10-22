> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

> 0.1.62：复杂材料图、液体物品/容器产出、普通电池及外电已补充两晚真实保存和冷启动验证；下方历史边界以本条限定的矩阵更新，完整范围见[跨夜指南](machine-persistence.zh-CN.md)。不包含原版熔炉所有材料策略或崩溃恢复。

> 0.1.60：独立处理器已支持 `Process(machine,inventory)`；Runtime入口复制并绑定Template，自定义窗口可借用真实子库存。原生交互委托仍由窗口作者设置，见[machine-inventory-binding.zh-CN.md](machine-inventory-binding.zh-CN.md)。

# 机器自动化与夜间加工

统一机器现在默认接入游戏的 `BeforeNight` 生命周期，每台每晚默认尝试一批。框架复用已核对的 `PlayerStore.EndNight` 生命周期入口，不添加第二套每帧调度，也不请求保存。基本注册见 [机器注册](machine-registration.zh-CN.md)，材料消费见 [标准事务](machine-materials.zh-CN.md)，可编译示例为 `src/EZapi.Sample/MachineAutomationExamples.cs`。

## AI 开发顺序

1. 初始化主线程声明材料、产物和 `MachineDefinition`，检查注册的 `Accepted/Reason`。物品工厂仍必须在原生目录出现前提交。
2. 夜间自动加工保留 `EnableNightly=true`；纯手动机器明确设置 `false`。不要再为统一机器手动订阅 EndNight，否则可能产生额外批次。
3. 每晚一批无需填写解析器。自定义 `ResolveNightlyBatchCount` 只能只读机器状态，返回 1..64。成功后重新读取，允许缩减额度，不能超过本次最初额度；第一失败立即停止。无效额度不加工，不自动改成 1。
4. 需要选择配方或阻止时，调用 `MachineAutomationApi.Subscribe(ownerId,machineId,phase,callback,priority)`。持有 `IDisposable`，在 Mod 关闭时释放。不要保存同步上下文或其中原生对象供下一帧使用。
5. 查 `MachineRuntimeApi.RuntimeInstalled`、注册快照及诊断中的 `machineRuntime.history`；目录 Applied 不能单独证明机器已加工。
6. `MachineRuntimeApi.ProcessBatch(machine)` 显式执行一批；忽略夜间启用开关和额度，但仍经过自动化、资格检查和真实资源事务。不要把失败结果自动循环重试。

## 订阅与同步上下文

| 成员 | 契约 |
| --- | --- |
| `Recipes / RegisteredRecipes` | 转发统一合并目录，保留真实配方作者；查询不执行动态回调 |
| `BeforeBatch` | 完整资源快照之后、配方匹配之前；可 `SelectRecipe(id)` 或单向 `Block()`，不准修改材料/液体/电量/价格 |
| `AfterBatch` | 只对已进入真实事务执行器的结果分发；含预检拒绝、成功、补偿或恢复失败。缺材料、无匹配、Before 阻止时不分发 |
| `Machine / Inventory / RecipeId / Blocked` | 仅当前同步调用有效；过期访问抛 `EZAPI_MACHINE_AUTOMATION_STALE` |
| `Result / BatchResult` | Before 为 null；After 为结果文本及完整收据。必须检查 `BatchResult.Committed` 后再执行成功通知 |
| `IsCurrent` | 当前线程、代际、周目、机器身份及资源范围仍有效；过期返回 false |

每个 owner/机器/phase 最多一条订阅，全局最多 256 条。先按 priority 升序，再按 owner 的 Ordinal 顺序。同一次 phase 分发使用快照，新订阅不会插入该次快照，已 Dispose 的订阅即时跳过。不同订阅选择不同配方会阻止本批，同一配方重复选择允许；Block 不可撤销。After 调用选择/阻止会抛阶段错误。

回调异常禁用对应订阅并记录 WARNING。Before 异常阻止本批；After 异常保留原有事务收据，不撤销产物，不触发自动补发。资源变动会使 Before 上下文失效并停止加工；框架不能通用撤销作者回调自行造成的任意写入。

## 夜次身份与持久化

只有完整物品快照中、玩家拥有、附着于库存、数量为 1、统一注册且启用夜间的机器才进入调度。按实例 ID 排序，每台执行前重查当前周目与机器身份。

框架先写入并读回物品标签 `EZAPI_MACHINE_NIGHT_ATTEMPT_V1`，值为 `存档槽/RunID/日计数`，再尝试有限加工。标签和加工结果随游戏本次正常保存持久化；新进程读档仍能拒绝相同夜次。失败也占用本夜尝试，避免重复生命周期回调反复试产。下一晚使用新日计数。此标签是框架内部状态，作者不要清除或改写。

事务 `RecoveryFailed` 后写 `EZAPI_MACHINE_FAULT_V1` 并隔离该实例。标签写入失败时保留当前周目的内存隔离并记录错误。不会自动清故障、退款或重产；需要人工检查遗留材料/产物后明确修复。同步事务没有进程崩溃恢复日志，不能承诺游戏保存和外部副作用为同一事务。

## Runtime 入口与 JSON

`TryGetInventory(machine,out inventory)` 复用布局 API；`CreateBatchProcessor(owner,definition)` 创建显式手动处理器，不注册目录、工厂或夜间任务。RuntimeInstalled 核对配方、生命周期、准入和冻结的真实就绪状态。

诊断 `machineAutomation` 输出订阅与故障；`machineRuntime` 输出 run/day/attempted/history/scanning；`machineNightDebug` 是显式测试夹具结果。输出可用 JSON 阅读，不是可执行配方/委托的 JSON 导入格式。示例见 [machine-automation-view.json](examples/machine-automation-view.json)。

共享制造终端及其加价设置、同步外电、指定成分默认扣液已接入。已有容器液体输出已在0.1.55接入；含内装物材料、外电跨夜及持久恢复仍有缺口。新增液体输出的真实跨夜保存生命周期未在本轮验收。普通机器布局和投入/产物的正常游戏保存重载有单独实机证据；不等于 SaveGraph.Decode 所有特殊类型已支持。完整 Forge API 目标仍未完成。

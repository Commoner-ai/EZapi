# EZapi 0.1.69 机器公共契约审查

机器部分已逐项审阅 **34 个类型、114 个属性、20 个方法**。清单在 `machine-public-contract.audit.json`，每个属性/方法记录 EZapi 的实际语义、源码和验证证据。`machine-contract.index.json` 给 AI 提供入口选择与生命周期规则。

这份审查覆盖独立功能实现，不提供 Forge 的二进制兼容层。物品/容器输出使用 `"items"`/`"container"`；加工返回结构化 `MachineBatchResult`。已有 Forge Mod 需要按 EZapi 接口改写调用。

| 开发目的 | 首选入口 | 生命周期与边界 |
| --- | --- | --- |
| 声明新机器及配方 | `MachineRegistrationApi.RegisterMachine` | 初始化、物品目录应用之前；一次校验模板/配方/准入/图鉴 |
| 给共享机器追加配方 | `RegisterAdditionalRecipes` | 主线程空闲时整批追加；保留实际作者和声明顺序 |
| 手动加工一批 | `MachineRuntimeApi.ProcessBatch` | 当前已载入周目、主线程；检查 `Committed` 和事务错误 |
| 复用已有自定义窗口 | `CreateBatchProcessor` 后 `Process(machine, inventory)` | 每次传入当前机器真实子库存；模板只绑定结构，原生交互由窗口作者安装 |
| 默认每晚加工 | `MachineDefinition` | 默认一批；`EnableNightly=false` 停用；解析器只读、范围 1..64 |
| 加工前选择或阻止 | `MachineAutomationApi.Subscribe` | 保存并释放返回的 `IDisposable`；Before 可选择/阻止，After 只读结果 |
| 自管特殊资源 | `MachineRecipe.Transaction` | `Validate→Apply→Verify`；部分 Apply 失败也必须能 `Restore` |

`MachineInventory`、`MachineBatchContext`、配方计划和自动化上下文中的原生对象只在当前同步调用中借用。读档、换场景、换窗口后重新解析；工厂或处理器停止后不能复用旧代际句柄。登记快照中的模板和集合有独立副本，但委托仍是作者声明的委托，不会被序列化到 JSON。

夜间标记按存档槽、周目、日期和机器实例隔离，在作者代码执行前记录。成功后重新解析批次数；动态增加不会突破初始上限。失败不会在同夜重复生产。框架不主动保存，也不提供异步、网络或崩溃恢复事务。

只读回调的图状态审计能发现非法写入并阻止本批提交；不能撤销作者任意写入。标准事务另有实际材料、液体、电源、输出补偿。`RecoveryFailed` 需要人工检查；成功后的通知失败保留提交结果，不能再次扣费或补发。

本阶段沿用已安装的 0.1.69 DLL：11 组实机 **1557/0**，旧机器 **19/0**，提交数仍为 2；退出后 **536 份 ES3 字节不变**。此前两晚实际保存、完整进程重载，以及中英配方图鉴渲染证据按源码连续性复用。当前批次没有再次过夜或写存档。

扩大到全部 149 个公开类型后还发现辅助能力缺口：分级日志、窗口指针捕获工具和制造终端 ID 规范化。它们列入 `public-contract-coverage.index.json`，所以完整功能覆盖仍不能宣布 100%。39 个主要 API 组之外的辅助能力同样属于交付范围。

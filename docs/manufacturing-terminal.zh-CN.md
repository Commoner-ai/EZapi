> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi 共享制造终端

EZapi 0.1.28 提供自己的共享机器，物品 ID 是 `ezapi.machines.universal_manufacturing_terminal`。内容 Mod 调用 `MachineRegistrationApi.RegisterAdditionalRecipes(ownerId, ManufacturingTerminal.ItemId, recipes)` 追加自己的配方，即可复用官方目录工厂、独立窗口、投料准入、图鉴、标准材料事务和夜间调度。没有内置生产配方；正常运行不自动发放、不请求保存。

先读可编译的 [ManufacturingTerminalExamples.cs](../src/EZapi.Sample/ManufacturingTerminalExamples.cs)，再读 [机器注册](machine-registration.zh-CN.md)、[材料事务](machine-materials.zh-CN.md) 和 [夜间调度](machine-automation.zh-CN.md)。本框架不加载 Forge；不占用、重定向或迁移其他框架的物品 ID 和存档。

## 最短开发路径

1. 在内容 Mod 初始化时声明自己的投入和产物物品；不要在注册回调创建原生实例。
2. 以自己的 owner 为前缀声明唯一配方 ID，调用 `RegisterAdditionalRecipes`，检查 `Accepted`，拒绝时记录 `Reason`。追加按提交顺序匹配，每台合计最多64条，不能替换其他作者配方。空追加不表示清空。
3. 若产物价格应按材料计算，在 `Output.PrepareItem` 中调用 `ProductionValueApi.SetBasis(item, ProductionValueApi.Calculate(plan.Context, plan.Context.ProductionMarkupPercent))`。投入价值是本次合计，Calculate已经按产量分摊和向上取整，不要再乘投入数量或重复加价。固定价格配方可以保持自己的策略。
4. 选择自己的获取途径；使用现有掉落/物品创建接口，不把调试夹具当正式发放。当前未给这台机器自动加入原版掉落或商店。
5. 运行时逐项看 `ItemApi.FactorySnapshot()` 和 `MachineRegistrationApi.Snapshot()` 的 Applied/ProcessingAvailable/NightlyInstalled；`ManufacturingTerminal.Snapshot().Registered` 只证明声明提交。

## 机器和状态

| 参数 | 值 |
| --- | --- |
| 外部占格 / 基础价值 | 3×3 / 400 |
| 材料 / 产物 | 各9×6物品网格 |
| 电池 | 2×2单件槽，沿用官方电池准入 |
| 模块 | 7×5，原版熔炉和通用模块类型 |
| 默认批次电耗 | 25，按当前原生机器电耗读取，效率模块由官方计算 |
| 默认加价 | 15% |
| 夜间 | 每台每夜至多尝试一批，持久夜次去重；失败也占本夜 |

终端使用自有内嵌PNG；不会从 Forge DLL 提取资源。每次官方工厂创建新实例；窗口、材料、产物、电池、模块与夜次标签均按实例隔离。存档恢复使用已有EZapi机器布局，不额外安装终端专用的解码补丁。实例图不能跨场景缓存，每次重新 `TryGetInventory`。

共享终端只有物品投入和物品输出；需要液体专用槽时声明自己的机器。标准材料消费的既有边界继续适用：指定成分扣液已在0.1.54接入；含内部物品材料、外电跨夜持久恢复仍未完成，不要用空事务绕过拒绝。

## 加价配置的完整规则

`MachineRegistrationApi.ConfigureManufacturingTerminalMarkup(string ownerId, int markupPercent)` 返回 `ContentSubmissionResult`。合法范围0..1000（百分比整数）。owner沿用配方注册的严格小写校验，不能带空白或尾换行。

首个有效提交取得配置权，即使提交的恰好是默认15%；该作者之后可以更新，其他作者提交相同值也会收到 `EZAPI_TERMINAL_OWNER`。无效参数、终端未声明和加工中请求不取得配置权、不修改目录。重复相同值不重写目录。停止框架后配置权清除。

更新只作用于托管定义和可执行目录；保留追加作者身份、配方顺序、准入句柄与图鉴。后续 `MachineRuntimeApi.ProcessBatch` 及夜间调度读取新加价。`CreateBatchProcessor` 是独立定义快照，不会被全局配置追改。加价本身不重定价已经生产的物品，也不会覆盖作者自定的产物价格。

该配置不是存档内容；内容 Mod 应在每次初始化重新提交。它在进程内保留，换存档不会释放首作者配置权。所有配置操作仅允许主线程空闲时运行，禁止在动态配方/自动化/生产回调中修改当前目录。

## JSON 与 AI 使用约束

`docs/examples/manufacturing-terminal-output.json` 是诊断结果样例，不是可导入的配方或机器声明。动态条件、配置回调和原生物品操作使用C#。JSON物品声明仍使用既有pack schema，不能自行添加 `recipes` 或 `terminalMarkup` 字段。

机器常量和托管状态见 `ManufacturingTerminal.cs`；配置及原子目录更新见 `MachineRegistration.cs`；原生物品和模块初始化见 `ManufacturingTerminalRuntime.cs`。错误码与API索引同时记录。不要复制 Debug 类作为正式生产逻辑；测试按钮会临时追加测试配方，配置权直到进程重启才清除。

## 批量验证

“共享终端检查”按钮创建临时实例并回收，检查默认/0/25/1000加价、两作者、独立窗口、原生模块、电量不足、材料不足和满产物槽。“终端保存夹具”只在槽531及以后的独立新档放置两台终端；首次2份/1份投入，各100电，之后只读取。该按钮在新进程中重新提交测试配方，模拟正式内容Mod的初始化贡献；终端的保存数据通过游戏正常过夜和重启载入验证。报告在 `UserData/EZapi/diagnostics.json`，不要操作历史玩家档保存。

> 0.1.60：独立处理器已支持 `Process(machine,inventory)`；Runtime入口复制并绑定Template，自定义窗口可借用真实子库存。原生交互委托仍由窗口作者设置，见[machine-inventory-binding.zh-CN.md](machine-inventory-binding.zh-CN.md)。

> 当前0.1.57：Transaction省略即可用标准同步事务；MaterialContents默认Reject，可显式ConsumeGraph处理整个材料图。先读[machine-material-graph.zh-CN.md](machine-material-graph.zh-CN.md)。下文版本化历史说明不能作为当前缺口清单。

> 当前0.1.55：省略Transaction使用标准真实消费事务；统一机器注册、夜间调度、同步外电和已有容器液体输出均已有独立API。先读[machine-materials.zh-CN.md](machine-materials.zh-CN.md)和[machine-liquid-output.zh-CN.md](machine-liquid-output.zh-CN.md)。下文0.1.24为历史行为，不能据此判断当前默认事务或自动化缺失。常规测试直接使用native-test-automation.zh-CN.md中的脚本。

> 0.1.25更新：请先读[machine-materials.zh-CN.md](machine-materials.zh-CN.md)。省略Transaction现使用标准消费与冻结事务；自定义分支保持下文契约。以下0.1.24内容为历史行为。

# EZapi 0.1.24：配方选择与同步批次

这是独立EZapi的C#接口，能力ID为`machines.recipes`。先有已注册的机器物品及`MachineLayoutApi`布局，再登记配方、提供真实资源事务、按需调用一次处理器。现有物品JSON、布局JSON、图鉴JSON各有独立用途；本版没有可执行配方JSON导入器。`machine-batch-result.schema.json`只描述结果输出。

本版实现材料分配、液体预扣算、动态产物/电费、可执行配方目录、输入过滤、落点规划和自定义事务调度。默认材料消费器、整台机器冻结提交、外部电网、自动跨夜与正常机器保存重载继续开发；没有把这些能力标为完成。与Forge公共契约逐成员差距见`forge-parity.index.json`。

## AI先按这条路径实现

1. 用既有物品/工厂API登记机器，再在独立工厂内创建`MachineLayoutApi`布局。不要引用Forge，不改原版机器窗口。
2. 创建`MachineRecipe`及其`MachineIngredient`、`MachineLiquidIngredient`、`MachineItemOutput`。`Transaction`必须返回真实`IMachineBatchTransaction`；缺失在登记时拒绝，不会以空事务生产。
3. 调用`MachineRecipeApi.RegisterRecipes(owner,machineId,recipes,power,productionMarkupPercent)`。同一机器目录只属于一个owner，整组替换，空组移除；拒绝整组时旧目录及修订号不变。不会顺带登记机器物品或图鉴。
4. 可选调用`InstallInputAdmission(owner,machineId)`并持有返回句柄，停止时Dispose。它通过现有原生库存AND准入限制精确子库存`<machineId>.ezapi.machine.input`；保留原生和其他Mod的拒绝。规则读取当前配方的ID/Tag/Condition及`AuxiliaryItemIds`，目录替换立即生效，移除后释放本规则限制。辅助物品只是允许存放，不自动消耗、不要求存在。数量不足仍可逐件投料。准入回调异常沿用库存API的故障停用策略；修复后释放旧句柄再安装。
5. 在当前周目主线程调用`ProcessBatch(machine)`。或者用`CreateBatchProcessor(owner,machineId,recipes,...)`创建独立处理器，再`Process(machine)`。处理器只保存定义；Stop/Start后旧处理器失效，不能跨生命周期复用。处理器本身不注册目录或过滤器。
6. 只认`result.Committed`为成功。选中配方后不会因为提交失败再尝试另一配方。没有夜间自动调用、自动保存或自动重试。

可编译示例见`../src/EZapi.Sample/MachineRecipeExamples.cs`，真实原生调试事务见`../src/EZapi/DebugMachineRecipes.cs`。后者只把临时单件输入Expel暂存并在测试结束销毁，不是可复制到玩家材料上的通用消费策略。不能直接改`unitCount`假装已消费，也不能在恢复中Destroy框架的新产物。

## 数量、条件与动态解析顺序

材料只从输入网格的直接子项选择，按实例号排序，最多64堆、16条需求。`ItemId`与`ItemTag`同时填写时取交集；至少填一项，`Condition`再附加真实状态限制。`Count`为正整数单位数；`WholeStack=true`时选中的完整堆总数必须恰好为Count，绝不为凑数量多扣。同一堆可满足多条普通需求，但其总分配不超过实际数量；整堆分配不再给其他需求重复使用。宽条件与严条件用整数最大流共同分配；整堆先有界组合搜索。最多8192搜索/增广步、262144边检查；超限整批拒绝，无部分分配。

`MachineItemTake.Value`为原生`unitValue × 本次分配数量`的合计基础价值，不再重复乘Count，不声称包括所有原生成交修正。`ProductionMarkupPercent`只进入批次上下文，只有作者显式调用计价API才应用到产物。

同槽液体需求按声明顺序从剩余组成计算，不能让两条300ml需求共用同一瓶500ml。整数ml范围0..2147483，1000parts=1ml；`LiquidId=null`按既有比例算法扣混合液，填写ID仅扣该成分。这里只计算计划；事务仍须执行实际原生液体操作及元数据同步。副作用/反应超出已验证恢复范围时不得假报成功。

动态回调顺序如下，均只调用一次，不重新求值电费或产量：

| 回调 | 所见上下文 |
| --- | --- |
| MachineCondition、材料Condition | 真实当前机器/材料，只读 |
| 各液体ResolveMillilitres | 已选材料、全部当前液体容器、前面已计算液体需求；OutputCount暂为1 |
| 输出ResolveCount | 全部材料/液体投入；OutputCount暂为1 |
| 输出ResolveItemId | 最终OutputCount |
| 电费ResolveCost | 最终材料/液体/数量 |
| 输出Contents各解析器 | 最终上下文；每件产物的成分总量/价值/品质 |
| Transaction(plan) | 完整计划；产品尚未创建，OutputPlacements为空 |
| PrepareItem(plan,newProduct) | 只配置本次新产物；不得扣资源或交付 |
| 事务Validate/Apply/Verify/Restore | 见下一节；第二次Validate前落点已计算 |
| AfterCommitted(plan,products) | 全部提交与框架写后核验通过后一次 |

`power=null`时读取`Power.GetMachineCost(machine)`，不再次乘原生效率。`MachinePowerRule`是显式替代费用，`PerOutput=true`只乘最终产物数量。正费用需要布局电池槽中的单件电源并通过官方CanDraw检查；本版不查询外部电网。零费用允许没有电池。

## 写入、补偿与结果

处理器保存各精确子库存快照、输入基础价值、液体实际组成/元数据和电量。只读解析器、条件、事务工厂、Validate都不允许写游戏状态。框架可以发现已观察资源的变化，但不能恢复违规只读回调对任意其他状态的改动；不要依赖这种检查来替代纯回调。

选择第一条资源满足的配方后，执行器预检→创建/配置最多64独立新单件→整组网格落点规划→二次预检→一次Apply→Verify。`OutputPlacements`使用既有有界规划器，未预留空间；Apply仍须使用真实`TryInventorySlot/TryAcceptOnce`并逐项读回。满输出可能创建随后清理的临时产品，但绝不开始Apply/扣资源。

事务Validate负责额外业务资格；Apply负责实际材料消费、液体、电量和产物接纳；Verify必须审计实际材料消费。本处理器另外检查产物确实位于本机输出库存、液体精确剩余组成及电量差。**框架不能安全解引用已销毁材料来通用审计消费，不能省略作者Verify。** 输出Contents声明非空时，PrepareItem负责填充实际内容，框架核验每件产物体积及显式价值/品质；不自动加液，不多生成内容物。

失败时事务Restore恢复部分写入并把框架新产品Expel回游离态。处理器随后独立核对原库存快照、输入数量/坐标/价值、液体和电量；Restore返回true但仍丢资源会被报告为RecoveryFailed。框架最后清理本次新游离产品。附着/身份不明产品保留并报告故障。

| 结果 | 调用方行为 |
| --- | --- |
| `Transaction=null`及顶层Error | 未进入执行器：没匹配、目录缺失、回调异常、重入或旧处理器；不得视为生产 |
| Rejected | Apply未开始；产品收据仅代表临时创建，不代表交付 |
| Restored | Apply尝试后已恢复；只有明确重新操作时才再处理 |
| RecoveryFailed | 补偿/产品清理不确定；禁止自动重试，先处理故障 |
| Committed | 已确认提交；`CompletionError`非空只表示成功通知失败，不退款、不再次加工 |

`AfterCommitted`可显式调用已验证的模块工作通知；本版不自动加成长。通知抛错也仍然Committed。所有原生对象、上下文、计划与落点只在当前同步调用内使用，不可写入全局缓存；Snapshot仅提供纯托管目录描述。结果默认System.Text.Json为PascalCase/数字枚举。

## 自行验证入口

调试面板新增“配方批次检查”（1920×1080下中心114,408），一次合并多种临时场景，不发给玩家、不保存。操作脚本`verification/Invoke-EZapiNativeTest.ps1`沿用PID/窗口焦点/分辨率门控、截图与输入日志；先查看截图再重放计划。当前证据与限制见`machine-recipes-0.1.24.zh-CN.md`。

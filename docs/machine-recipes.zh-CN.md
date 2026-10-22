# 机器配方选择与同步批次

当前程序集 0.1.73，实机验收状态见[统一状态](current-status.zh-CN.md)。`MachineRecipeApi` 接收 C# 配方、选择材料、执行有界落点规划，并在同步事务中提交一次批次。布局 JSON 和图鉴 JSON 不等于可执行配方导入器。

## 推荐接入顺序

1. 注册机器物品并建立 `MachineLayoutApi` 布局。普通 Mod 优先使用 [MachineRegistrationApi](machine-registration.zh-CN.md) 统一登记，避免重复维护目录和过滤器。
2. 用 `MachineRecipe` 声明 `ItemInputs` 和 `Output`；液体输入、条件、辅助物品、电费及图鉴按需添加。**省略 `Transaction` 使用框架标准消费、冻结与同步补偿事务**。设置自定义事务时，作者负责真实写入、差额核验和恢复。
3. 低层入口 `RegisterRecipes(ownerId,machineId,recipes,...)` 对独立目录整组替换；统一登记的目录用 `RegisterAdditionalRecipes` 追加，不可通过低层替换。检查提交结果，登记接受不表示机器已在游戏目录中生效。
4. 低层可用 `InstallInputAdmission` 为精确输入子库存安装 AND 准入，保存并释放句柄；统一注册管理相应准入。原生与其他 Mod 的拒绝优先。
5. 手动执行 `ProcessBatch(machine)`，或创建独立处理器后调用 `Process(machine)` / `Process(machine,inventory)`。独立处理器只保存定义，不自动登记目录、过滤器或跨夜钩子。自动加工由 [MachineRuntimeApi](machine-automation.zh-CN.md) 管理。
6. 仅 `result.Committed` 表示已确认提交。拒绝、恢复失败、旧会话和不明确结果均不应自动重试；`AfterCommitted` 异常作为完成通知错误，不回滚已完成加工。

可编译示例：[MachineRecipeExamples.cs](../src/EZapi.Sample/MachineRecipeExamples.cs)。精确类型、重载和参数见[公开签名](public-api.index.json)。

## 标准事务与自定义事务

标准事务处理材料预留与消费、冻结、液体差额、电源扣款和产物交付，并核对失败恢复。普通电池和已注册外电连接器分别使用对应电源路径。材料默认 `MaterialContents=Reject`，含子图材料需显式 `ConsumeGraph`，消费根材料及后代；不要把默认拒绝当成未实现子图消费。详见[材料](machine-materials.zh-CN.md)、[材料图](machine-material-graph.zh-CN.md)、[外电](external-power.zh-CN.md)。

`MachineItemOutput` 创建新单件；`MachineContainerOutput` 向输出槽已有容器添加液体，不创建新物品。标准事务填充声明内容物；自定义事务需实现其 `PrepareItem`、Validate/Apply/Verify/Restore 责任，不能用恒真校验代替实际消费。见[液体输出](machine-liquid-output.zh-CN.md)、[自定义事务](machine-transactions.zh-CN.md)。

条件、动态解析器及事务工厂必须只读；`PrepareItem` 只配置本批次新产物。不得跨帧缓存原生对象、在回调中切换场景/读档或私自保存。处理器不把一系列游戏保存视为原子事务，也不提供崩溃后的自动重放。

## 数量与结果边界

材料只观察精确输入网格的直接子项，ID 和 Tag 同时声明时取交集，Condition 进一步限制。WholeStack 要求完整堆数量恰好匹配。液体数量按整数 ml 声明，1000 parts=1 ml；同槽需求依次从剩余组成扣算，不复用同一份资源。原生副作用超出恢复范围时须明确报告失败。

材料匹配、落点和解析器均有预算；无匹配、预算超限或输出无法安置不能被视为成功。正电费预检当前可用电源；零电费不强制存在电池。框架不自动保存、不提供网络事务，也不将临时调试事务作为推荐消费实现。

当前 DLL 的原生生产、跨天、Mod 组合与保存重载仍待验收。[历史配方沿革](history/legacy-machine-recipes-notes.md)保留旧阶段描述，不能用于判断当前能力缺失。

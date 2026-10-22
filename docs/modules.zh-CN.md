# 模块 API（0.1.19）

用途：追加机器模块仓准入、查询已安装模块、按官方顺序重算、订阅五个属性阶段、模块使用通知与工作成长。独立实现，不需要Forge。入口在 `src/EZapi/Modules.cs`；中文调用示例在 `src/EZapi.Sample/ModuleExamples.cs`。

初始化主线程先注册声明/订阅；目录就绪后检查 `ModuleApi.GetRuntimeStatus()`。四组分别为 admission、phases、actions、learning，每组必须全部目标安装成功才运行。未使用的组不安装；未安装不等于报错。`Accepted`只表示准入声明已接收，不证明原生窗口或某台已存在机器被重新创建。

| 入口 | 行为与边界 |
| --- | --- |
| RegisterAdmission(ownerId, admissions) | 整批校验后替换本owner声明；机器ID可为原版。追加类型不会删除其它owner或原生类型。每owner最多256台、每台64类型、全局4096条。 |
| RemoveAdmission(ownerId) | 移除本owner未来的追加规则；已创建仓不会被强行覆盖。 |
| GetInventory(machine) | 当前官方 `MachineHelper.GetModuleInv`；无模块仓返回null。支持目录就绪后的临时工厂实例，不要求已交付给玩家。 |
| FindInstalled(machine, moduleId, moduleType=null) | ID精确匹配，可再过滤原生 `MODULE_TYPE.valueString`；返回列表快照，物品仍是原生对象。 |
| Recompute(machine) | `ComputeAllModuleTempStat`→`ComputeModuleEffect(inv,null,machine)`→`ApplyBasicModuleEffect(inv,null,machine)`；每步复查身份/场景/模块仓，拒绝同机器重入。失败不保证原生属性回滚，不自动重试。 |
| Subscribe(ownerId, callbackId, phase, callback, order=0) | int升序、owner/ID字典序；同owner的callbackId跨种类唯一。Dispose幂等。 |
| SubscribeAction(ownerId, callbackId, moduleId, callback) | moduleId须以ownerId加点开头；原生ActivateOnUseAction之前通知，不替换原生方法，不证明生产成功。 |
| SubscribeLearning&lt;T&gt;(ownerId, callbackId, capture, completed) | 每次调用先capture得T，原方法正常返回后completed(item,T)。跳过或原生异常不完成；finalizer原样传递异常并清理。 |

五阶段映射：

| ModulePhase | 精确原生入口 | 时机 |
| --- | --- | --- |
| BeforeAggregate | ModuleHelper.ComputeModuleEffect(List&lt;GameItem&gt;, GameItem) | Prefix |
| AfterAggregate | 同上 | 原方法执行并正常返回的Postfix |
| AfterTemp | ModuleEffectHelper.ComputeAllModuleTempStat(List&lt;GameItem&gt;) | 原方法执行并正常返回的Postfix |
| BeforeAdded | ModuleEffectHelper.OnAddedModuleToGrid(GameItem,GameItem,List&lt;GameItem&gt;) | Prefix |
| AfterRemoved | ModuleEffectHelper.OnRemovedModuleFromGrid(GameItem,GameItem,List&lt;GameItem&gt;) | 原方法执行并正常返回的Postfix |

AfterTemp只有模块列表，Machine和Module可为null；Aggregate提供Machine；安装/移除提供Machine和Module。Context只在同步派发期间有效，调用结束后原生属性getter拒绝访问。所有回调都在主线程执行；异常订阅停用，其它订阅继续。不要在邻接/临时属性阶段累积上一次的临时结果。

成长帧按当次调用保存，支持不同物品嵌套。同一物品递归调用原方法仍由游戏处理，EZapi抑制重复capture；最多64层待完成帧。Dispose、场景变化、周目revision变化、物品身份变化、停止框架都会阻止过期completed；场景卸载立即释放待完成引用。仅收到动作通知不能据此发放奖励或认定成功生产，内容逻辑须另核对实际材料、电量和输出。

准入的null与空集合语义：无本方追加时原样保留原参数。存在追加且原参数为null时保留原生默认 `MODULE_TYPE_UNIVERSAL`；原参数显式空集合时不会额外添加UNIVERSAL。规则只在SetupModuleBay构造时参与，不重建玩家已有窗口。

调试面板新增“模块检查”：手动创建临时未交付实例、安装实际原生钩子、执行原生准入/接纳/重算/动作/成长，结束销毁；不写玩家库存、不保存。当前Demo可能没有生产机器工厂，报告会明确标记syntheticMachine测试壳，不能把它计为原版机器生产/跨夜/保存验证。完整测试结果以 `modulesDebug` 和阶段报告为准。

目前不把ModuleApi当作模块物品注册器。低层模块/节点/设施声明与机器配方注册属于后续范围；此API可对现有原生物品或内容侧工厂创建的模块使用。多语言扩展暂缓。

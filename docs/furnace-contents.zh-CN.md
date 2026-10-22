> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 原版熔炉的内装物与外电（EZapi）

外电提供方仍通过 `ExternalPowerApi.Register` 登记，旧四参数重载保留。连接器工厂调用 `InitializeConnector`，登记成功后等待 `IsAvailable`；停用时释放登记句柄。详见[完整外电指南](external-power.zh-CN.md)。

## 材料和旧产物

四参数重载默认 `MachineMaterialContentsPolicy.Reject`：带子窗口的输入材料在创建电力计划之前拒绝，防止不知情地销毁内装物。作者明确希望连内装物一起消耗时，调用五参数重载：

```csharp
// QueryEnergy/PlanDebit 是作者自己的只读查询和同步电力事务工厂。
// 这项策略只影响原版熔炉；EZapi配方仍使用各自的MaterialContents策略。
IDisposable lease = ExternalPowerApi.Register(
    "author.power", "author.power.connector", QueryEnergy, PlanDebit,
    MachineMaterialContentsPolicy.ConsumeGraph);
// 内容Mod停用时 lease.Dispose(); 不在回调内重复登记。
```

已有产物的子图始终保留，**不需要**允许消耗材料子图。加工前记录所有后代的身份、状态/字典、标签、价值、形状、父库存、库存边界/锁和图连接；原材料根的原槽、handler及位置由槽位日记独立记录。加工期间只冻结内装物，原版继续处理根材料。受保护的后代不能被移走；提前Destroy后代会拒绝本批，原实例仍可恢复。

失败先恢复原提供方的真实电量，再移除新产物、恢复原材料/旧产物和子图，逐项读回。恢复不能确认时继续尝试其余步骤并隔离机器。提供方释放后仍调用原事务恢复，不能重新找另一个提供方退款。

确认产出与能源成功后，才从最深层开始逐件Expel/Destroy已消耗的材料子图，最后销毁数量可能已为0的根。清理一旦开始不能退款重做；变化或清理失败只隔离。纯托管销毁收据不会再次解引用已销毁对象。Unity帧末销毁仍在两帧后确认，临时队列不承担资源事务。

上限：128个原输入/旧输出根、4096个合计图物品、每图8192节点/边和16384标签、128模块、128待销毁确认批次。目录未知或原生钩子不齐时关闭能力。作者的查询/plan/Validate/Verify必须只读；不承诺修复任意第三方直接改字段造成的身份或结构损坏。

## 已验证范围

> 历史验收记录（0.1.67），不作为当前 DLL 通过依据。
> 最终DLL三项目构建0警告0错误、49组离线检查；图日记42/0、外电登记/事务66/0。原版熔炉28场景233/0，八组原生合计1126/0；旧复杂机器状态19/0、committed仍为2。所有536份ES3文件不变。

场景覆盖普通电池与外电、零耗电/缺料/缺电/断线、Validate拒绝、扣电异常、前后Verify拒绝、提供方释放、满输出、旧产物销毁请求、恢复确认false隔离，以及三层材料子图拒绝/消耗/回滚/标签价值/液体容量成分恢复/后代销毁/既有冻结、旧产物子图成功/回滚/销毁请求、空子窗口、子图零耗电。

本轮仅临时图调用真实原版委托，不保存、不推进夜晚。已有EZapi外电两晚保存/冷启动证据见[0.1.62](machine-persistence.zh-CN.md)，不能把它说成原版熔炉所有组合的跨夜实测。不提供异步、网络或崩溃恢复。

见[AI索引](furnace-contents.index.json)、公共契约审计（历史标识 `external-power-public-contract.audit.json`；原件未公开）、本轮证据（历史标识 `verification/furnace-contents-native-0.1.67.json`；原件未公开）及[English](furnace-contents.en.md)。

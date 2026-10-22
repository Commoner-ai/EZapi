> 历史记录 / Historical record：只适用于标题所列旧版本；不代表当前 DLL 验收。[当前状态](../current-status.zh-CN.md)。

> 历史阶段：0.1.22已冻结但未通知交付；补审发现离线owner校验替身比真实公共规则严格，已由0.1.23执行器独立全文校验修正。当前验收见machine-transactions-0.1.23.zh-CN.md。

# 0.1.22 加工事务与液体恢复验收

日期2026-10-20；游戏 Probably Stolen Demo 050rev5-L，Assembly-CSharp SHA256 `E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF`。DLL SHA256 `55F3073462276F4841077D0CF8E349B5069FC1B5101B0F31960441A1AA1E8715`。

本版公开 `IMachineBatchTransaction` 五方法契约、`MachineProductDefinition`、纯托管收据与 `MachineTransactionApi.Run`。框架管理产品创建、准备、重复预检、一次Apply、实际Verify、失败Restore/清理与完成通知异常隔离；具体资源与投料规则由自定义事务负责。详细用法见 [开发指南](../machine-transactions.zh-CN.md)。没有 Forge 程序集引用。

## 本次实际实机验证

最终证据为包内 `verification/native/final.json`、`final.log`、截图和自动点击计划。游戏仅启用 EZapi.dll；未安装示例C# DLL。

| 检查 | 通过 | 失败 |
| --- | ---: | ---: |
| 自定义加工事务/限量倒液恢复 | 52 | 0 |
| 机器布局/原生准入/编码 | 46 | 0 |
| 物品工厂 | 48 | 0 |
| 模块 | 14 | 0 |
| 物品原生属性 | 12 | 0 |

共172项，0失败。加工测试只对临时自定义目录物品进行，使用真实官方物品接纳/Expel、`WaterHelper.Remove`、`PowerHelper.DrawPowerSource`。五种自定义事务结果：

- 预检拒绝：Apply 0次，原物品、水量、电量不变。
- 成功：输入移出材料槽，输出槽增加1件新产品，水量500000→499000 parts，电量100→93，完成通知1次。
- 部分写入异常：Restore 1次，输入返回原坐标、水量恢复500000、电量恢复100，输出0件，完成通知0次。
- Verify拒绝：已交付产品被Expel再由框架清理，所有资源恢复，完成通知0次。
- 完成通知异常：仍为Committed，资源保持成功结果，CompletionError记录异常，不回滚、不允许直接重试。

最终 `machineTransactions` 为committed2/restored2/recoveryFailed0/running=false；无遗留冻结租约。额外两组限量倒液故障分别发生在源扣液之后、目标加液之后，均返回Restored且Indeterminate=false，两个容器的精确成分、价值恢复且冻结释放。

初版失败证据保留在 `verification/native-investigation`：整体 `TagSystem.Clone()` 备份不能隔离原生后续液体标签修改；恢复电力与输入成功，但水量仍499000，执行器正确返回RecoveryFailed。改为逐TagState.Clone后，同样两条路径恢复为500000。`deep-tags`是42项自定义事务对照；最终`verified`再加入双容器10项恢复测试，共52项。失败调查不是最终验收证据。

日志无ERROR。两条WARNING分别是加载器Class::Init替代提示，以及物品工厂检查主动返回null的预期EZAPI_FACTORY_RESULT，后续恢复正常。

## 离线与静态检查

本轮实际执行7套542项唯一案例，全部通过：加工事务50、液体转移97、机器/图鉴101、服务60、生产/效果93、库存守卫78、内容包63。加工测试链接真实执行器与已有批次DTO；显式替身隔离原生分配/周目，不把离线替身算实机。

371个直接游戏方法完整签名匹配。Unity静态检查88直接引用/93可达方法，无已知unstripping空壳。不调用自动保存，不安装自动HarmonyPatch属性，不手动调用tooltip Build。三个项目最终构建零警告零错误；默认JSON包、独立布局与图鉴JSON、示例内容包CLI检查通过。

## 存档与范围

测试结束正常退出游戏。524个历史存档文件及527测试档SHA256不变；玩家ownedCounts与0.1.20基线相同，没有ezapi.debug物品；原有三瓶499000/500000/1000parts与测试计数1不变。本次未交付测试机器给玩家，未触发保存。

自定义事务例中的输入先Expel，再由测试清理销毁；该例没有标准配方匹配、完整冻结加工策略、一般堆叠消耗或跨夜调度。资源事务不得被描述为标准机器运行时已全部完成。正常机器保存重载、图解码、外部电网、原版生产、夜间去重仍待后续开发与验证。Forge39组覆盖保持15实现/11部分/13缺失，完整目标仍active。

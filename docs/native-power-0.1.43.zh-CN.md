# EZapi 0.1.43 原生电力验收

**结果：通过。** 2026-10-21 在 Probably Stolen 0.46D、Unity 2021.3.45f2、MelonLoader 0.7.3 中运行。Mods 目录仅加载 EZapi 0.1.43，DLL SHA-256 为 `3E2E70EB542B89D4077D9799889C5E496A23B94A67B6B5120421A4DD77020491`。测试档为可丢弃槽 533。

游戏内“EZapi 调试 → 电力接口检查”创建临时机器，并分别测试单件与 5 件堆叠电源，每组 10 项，共 20 项通过、0 项失败。两种夹具都通过官方 `PowerHelper` 扣除 7 电，实际读回由 100 变为 93；恢复到 100 后再销毁。检查覆盖机器电耗、原生能量读回、电池槽接纳、扣款差额、不拆堆、实例与父库存身份保持、不足电量不写入、零扣款和清理失败检测。

测试前为全部 533 个 `save_*.es3` 文件创建了本地备份和 SHA-256 清单。退出游戏后再次逐项比较，文件数量和哈希均未变化；槽 533 的 `save_533.es3` 哈希为 `BD484FA149CDCAAA0FDF01071BDB7948FB29E8774333C90A3D0B6C06880A18F2`。Steam 显示云同步不可用，因此测试前备份本地存档，并选择继续离线运行。游戏进程启动时另行重写了 `SaveFile.es3` 设置元数据；测试前没有为该设置文件建立副本，EZapi 测试夹具没有调用存档 API。

`PowerBatchMath.Plan` 的 24 项纯托管回归全部通过。该规划器只验证多源分摊，不提交原生扣款。真实多源扣款事务、生产调度、外部连接器、跨日、保存/读档和其他 Mod 组合均未在本次范围内验收。

逐项结果和边界见 [`verification/native-power-0.1.43/native-test-result.json`](verification/native-power-0.1.43/native-test-result.json)；离线规划结果见 [`verification/native-power-0.1.43/power-batch-result.json`](verification/native-power-0.1.43/power-batch-result.json)。游戏内截图：[检查结果](verification/native-power-0.1.43/power-check-result.png)、[调试面板与测试槽](verification/native-power-0.1.43/debug-panel-open.png)。

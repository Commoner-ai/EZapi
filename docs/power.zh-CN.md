# 原生电力与只读规划

`Power` 提供当前物品的原生电量、机器电耗、可用性、扣款和恢复相关入口。`PowerBatchMath` 是纯托管分摊规划器，规划成功不会提交原生扣款。精确重载与参数见[公开签名索引](public-api.index.json)，调用示例见 [PowerExamples](../src/EZapi.Sample/PowerExamples.cs)。

在已初始化的游戏主线程调用；检查当前会话、物品拥有关系和能力状态，并逐项处理返回结果。不要拆堆、手改库存父图或把多次操作视为保存事务。外部电源、连接器和可补偿交易见[外电指南](external-power.zh-CN.md)。

当前 DLL 的实机电量读回、跨天与保存重载仍待验收。0.1.43 的原生扣款记录仅作为[历史报告](history/native-power-0.1.43.zh-CN.md)保留，不构成当前版本通过依据。

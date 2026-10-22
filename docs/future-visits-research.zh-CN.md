# 未来访客排程研究（0.1.72，未公开为 API）

这是一份实现前契约记录，不是可调用的 FutureVisitApi。现有 `ClientVisitApi.Queue` 是当前进程下一次顾客生成时排队；`SupplierApi.Queue` 提供独立对话型供应商，两者均不承诺未来日期的磁盘持久化。

## 来源与证据边界

官方文档提供 `OnGenerateCustomerVeryLate`，但没有定义未来队列日期的意义。当前 050REV5-L 包装程序集另公开 `PlayerStore.QueueFuturClient(string,int)`、`HandleFutureClientQueue()`、两份配对的 ID/天数列表。公开签名和当前游戏原生实现的只读研究是两种证据；均不等于游戏内跨天验收，也不保证其他构建行为相同。

本次包装程序集 SHA256：`E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF`。
GameAssembly.dll SHA256：`A2CAA0CECBF197CCD6B12DDEC7BB9E6FA0BC2CD2A5C52ABEF627AA919E2B9BFD`。
研究仅对磁盘二进制进行只读分析；运行时不得采用这些分析中的裸地址或字段偏移。

## 已观察到的行为

| 入口 | 当前构建的静态观察 | 对封装的影响 |
| --- | --- | --- |
| `QueueFuturClient(id, day)` | ID 与整数分别追加至配对列表；CLOSED/OPEN 写原值，MORNING 写 `day+1` | 参数不是绝对游戏日；必须验证开门时序后才能承诺“明天” |
| `HandleFutureClientQueue()` | 先将有效项倒计数减一；到零或负数时创建并加入顾客，再删除两份列表中对应位置 | 取消/去重不能只看 ID；队列变更与记录更新必须处理部分完成 |
| `OnShutterOpened()` | 先生成普通顾客，再消费未来队列 | “开门次数”与日历日期的关系需要原生跨天确认 |
| `BeginDay()` / `StoreStation.StartDay()` | 新一天增加日计数 | 不能仅凭方法名决定何时将剩余次数转换为绝对日 |
| `CreateStoreClient(id)` | ID 未注册时回退默认 scav | 创建非空不代表角色存在，排队前必须检查角色注册目录 |
| `StoreCalendar.GetDayOfWeek()` | 使用日计数余数映射 | 尚未验证零值对应星期，不提供猜测的星期编号 |

## 实现前必须解决

1. 在同一可丢弃周目分别从 MORNING、OPEN、CLOSED 排队，经真实开门、关门、睡觉，记录日数、剩余倒计数与实际到达。测试必须包括保存与新进程载入。
2. 定义作者命名空间、稳定请求 ID、槽/周目归属与配额；保留其他 Mod 的未来队列，不把全局清空作为取消实现。
3. 原生两份队列与 RunData 记录不是一个事务。写前/写后校验、损坏记录保留、不确定状态禁止自动重放，均须有实际失败路径。
4. 区分“已登记”“已加入当天顾客队列”“已到达”“已离场”，不能把倒计数归零报告为到达。读档不能重复发放访客或消耗两次请求。
5. 日历标记是单独显示能力；真实到访和显示标记分别验收。方法缺失、未知构建或无法核对日期时，返回明确不可用原因。

这项候选仍未实现。当前公开实用扩展是升级/服务快照、共享前置条件与物品版本化数据，详见 [知识库扩展索引](knowledge-expansion.index.json)。

开发区研究原始记录：`work/future-visits-native-research-0.1.72.txt`、`work/future-visits-lifecycle-research-0.1.72.txt`、`work/future-visits-open-research-0.1.72.txt`。这些工作文件不是发行 API；发布文档保留上表的可审查结论及其边界。

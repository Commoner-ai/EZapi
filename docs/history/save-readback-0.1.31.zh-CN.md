> 历史记录 / Historical record：只适用于标题所列旧版本；不代表当前 DLL 验收。[当前状态](../current-status.zh-CN.md)。

# EZapi 0.1.31 存档回读验收

本版新增 `SaveReadbackApi`，提供有界、只读的 ES3 文本快照。它不调用游戏保存、不写存档、不恢复原生物品；`Readable` 只表示文本可以解析。

离线测试链接真实源码，net6.0.36 与 net8.0.31 各通过 1,737 项（失败 0），读取本机 534 个 `.es3` 文件，逐个核对槽位/周目身份和 SHA 前后不变。游戏内使用安装 DLL 的“存档回读”按钮，通过 9 项检查：ES3 扩展规范化、坏转义、重复/缺失属性区分、Clone 与 Dispose 生命周期、当前存档读取、槽位和周目匹配、只读 SHA 不变。最终实机报告位于 `work/native-0.1.31-save-readback-20261020/verified/diagnostics.json`，日志 0 条 ERROR。

文件读取在打开期间以 `FileShare.Read` 共享模式读取，并核对长度、写入时间、创建时间和流长度；保存正在写入或文件发生替换时返回 `ChangedDuringRead`/`Unavailable`。这只能保证本次快照，不保证读取结束后的未来写入。`ReadBytes` 会复制输入；`Parse` 是无大小上限的低层入口，不可信输入应先用有界 `ReadBytes`。

解析保留 JSON 重复属性。`TryGetUniqueProperty` 对缺失返回 `true` 与 `present=false`，对重复或非对象返回 `false`；`RequireProperty` 对缺失、重复和非对象抛出异常。跨 `SaveReadResult` 生命周期保存节点时必须显式 `Clone()`。

本版覆盖状态：`Readable`、`Missing`、`TooLarge`、`ChangedDuringRead`、`Malformed`、`Unavailable`、`Invalid`。尚未声称 SaveGraph 恢复、保存事务、异步/崩溃恢复或联机大厅组合行为。

## 本版集中回归

| 游戏内检查组 | 通过 |
| --- | ---: |
| 存档回读 | 9 |
| 原版熔炉外电与故障恢复 | 115 |
| 外部电力事务 | 82 |
| 共享制造终端 | 84 |
| 机器自动化 | 119 |
| 统一机器注册 | 42 |
| 材料策略 | 109 |
| 配方解析与执行 | 118 |
| 加工事务 | 52 |
| 机器布局 | 46 |
| 原生物品工厂 | 48 |
| 模块 | 14 |
| 原生物品属性 | 12 |
| 合计 | 850 |

最终日志 0 条 ERROR。11 条 WARNING 为 1 条 Il2CppInterop 已知兼容提示、7 条熔炉故障注入提示、2 条自动化回调故障注入提示和 1 条非法工厂返回值拒绝提示。末态外电提供者 0、未关闭交易 0、冻结物品/租约 0、待销毁清理 0；熔炉恢复失败 1 次对应故意注入的 Restore=false，隔离符合预期，不作成功恢复统计。

15 套离线检查合计 3,285 项通过，失败 0；存档回读另外在 net8 重跑 1,737 项，不重复计入总数。其它套件的原生替身边界保留在各报告中，不能代替游戏验证。三项目构建结果、真实源码 SHA、完整测试 cases、执行输出、截图/输入记录/日志随发行包提供。413 个直接游戏方法签名匹配，Unity 94 个直接引用、99 个可达方法未发现 unstripping 空壳。没有 Forge 运行时引用、自动 SaveGame 调用、自动 HarmonyPatch 特性或手工 Tooltip.Build 调用。

已复核 524 份历史存档和 527/532 测试档 SHA 不变；发行包不含 `.es3` 存档。仅加载 `Mods/EZapi.dll`，已安装 DLL 与本轮实机、构建和包内 DLL 一致。完整能力目标仍为 17 组实现、13 组部分实现、9 组缺失。

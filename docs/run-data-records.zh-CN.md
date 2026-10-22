> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 周目 JSON 记录与冲突检查

`RunDataApi` 用于任务进度、版本化状态和待交付记录。物品自身状态继续使用物品标签。普通计数仍可使用 `RunData.Open`；该接口的原有格式、限制和行为不变。

## 最少调用

在当前游戏事件内使用事件的 `context.Run`：

```csharp
if (context.Run is not { } run) return;
const string owner = "author.tools", key = "author.tools.quest";
var before = RunDataApi.Read(owner, key);
if (before.Status is not (RunDataStatus.Missing or RunDataStatus.Present)) return;
// Present 时先检查自己的 version 和业务条件；示例见 RunDataRecordExamples.cs。
string next = "{\"version\":1,\"progress\":3}";
var result = RunDataApi.Stage(owner, key, run, before.Json, next);
// 只在 StagedInMemory 时确认内存暂存成功；游戏是否已保存另行验证。
```

两个便捷重载不要求持有原生玩家。另提供显式玩家的 `Read(store, ownerId, key)` 和 `Stage(store, ownerId, key, runId, slotId, expectedJson, nextJson)`，供底层集成使用。它们只接受当前玩家；便捷 Stage 还检查事件快照的 Epoch，同一存档再次载入后旧快照也会被拒绝。

所有操作在已初始化的游戏主线程执行。`key` 是完整键，必须以 `ownerId + "."` 开头，例如 `author.tools.quest`，最多 320 字符，只含 ASCII 字母、数字、下划线、连字符和点。owner 沿用 EZapi 的小写 Mod ID 规则。

## 状态处理

| Status | 含义与后续动作 |
| --- | --- |
| Disabled | 当前线程没有可用的 EZapi 主线程入口；不要从后台线程触碰原生对象 |
| Invalid | 参数、JSON 或已有记录不合法；保留原数据，修复声明或显式迁移 |
| Unavailable | 没有当前已载入玩家、原生字典缺失、旧玩家或读取异常；等下一有效游戏事件 |
| Missing | 字典存在且键不存在；Stage 的 expectedJson 使用 null |
| Present | Json 是原始记录；校验版本与业务后，将该原文用于 expectedJson |
| StagedInMemory | 写后已读回同一玩家、同一载入、同一字典与目标文本；不表示落盘 |
| Conflict | 周目/槽/载入或旧值已改变；本次没有执行写入，重新读取并复核业务 |
| Indeterminate | 已尝试原生写入但无法确认；先核对当前记录和业务结果，不能自动重做扣款或发奖 |

比较使用原始文本，空格与属性顺序也属于比较内容。`expectedJson=null` 只允许新建；它不表示无条件覆盖。JSON 中的字面量 `null` 也不是缺失值。框架不把版本号当作全局事务号，不提供跨存档文件的原子性，不回滚其它 Mod 的更新，也不保证识别“旧值→其它值→旧值”的 ABA 变化。

## JSON 边界

新值和非空旧值必须是合法 JSON 对象，最多 1,048,576 个 .NET 字符、深度 64；不接受数组/标量根、注释和尾逗号。上限用于兼容公共契约，实际内容应遵守官方指南，保持几 KB。JSON 内部的字段、版本、重复属性与未来格式由内容 Mod 校验，框架不静默标准化数据。Read 对坏数据返回 Invalid，绝不覆盖成默认值。

## 格式与迁移

官方指南的 `ModHelper.SetModData/GetModData` 便捷方法在当前包装程序集中不可调用，因此与已有 RunData 一样，接入集中使用公开 `PlayerStore.modData`。不增加保存钩子、Harmony 补丁或旁路文件。

本 API 的完整键直接存入 `modData`。旧简单接口仍使用 `EZapi.RunData/v1/<owner>/<key>`，两者不会互相覆盖。不会自动认领 Forge 或官方 ModHelper 的历史键；只对作者明确认领的完整键读写。迁移时先读取、验证旧格式并生成新 JSON，再以目标键当前原文作为比较值提交；保留旧数据，确认游戏保存及重新载入之后再由作者决定是否删除旧键。不能因为 Read 失败就重置玩家进度。

## 验收范围

源码：[RunDataApi.cs](https://github.com/Commoner-ai/EZapi/blob/main/src/EZapi/RunDataApi.cs)。可编译调用示例：[RunDataRecordExamples.cs](../src/EZapi.Sample/RunDataRecordExamples.cs)。离线测试链接真实 RunDataApi/RunData/GameSession，原生玩家和字典由替身提供；覆盖冲突、载入隔离、边界、异常与写后未知状态。游戏内每晚保存、重新读档和多 Mod 组合仍需单独验收；本版没有启动游戏或改动游戏存档。

> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 物品自有版本化 JSON 数据（当前接口）

`ItemDataApi` 适合记录某件物品的升级等级、制造批次或作者状态。官方指南要求物品数据保存在物品标签中；EZapi 使用 `GetTagReadonly` / `ModifyTag` 和 `SyncModifiedState`，把命名空间、格式版本、比较检查和失败状态集中处理。周目任务、余额和未来奖励应使用周目数据接口。

它只暂存**当前物品的内存状态**，不调用游戏保存。`StagedInMemory` 不代表磁盘保存完成。新增接口的原生与磁盘验收状态见 [机器可读索引](item-data.index.json)，不能套用此前 DLL 的验证。

## 最短调用

在已载入周目的游戏主线程，从当前玩法回调取得活物品后调用：

```csharp
public sealed record UpgradeData(int Level, string Note);

var read = ItemDataApi.Read<UpgradeData>(item, "author.example", "upgrade", 1);
if (read.Status is ItemDataStatus.Missing or ItemDataStatus.Present)
{
    var next = new UpgradeData(2, "第二级 / Level two");
    var write = ItemDataApi.Stage(item, read.Revision!, next, 1);
    // 只有 StagedInMemory 表示本次内存写入已读回确认。
    // 其它状态交由当前玩法处理；不要自动扣款或盲目重试。
}
```

[可编译样例](../src/EZapi.Sample/ItemDataExamples.cs)只演示数据暂存。扣款、材料消耗和实际升级效果由内容 Mod 负责；数据写入与这些动作没有自动事务。需要交易时先设计完整补偿和对账，再接入现有事务 API。

## 地址、版本与迁移

`ownerId` 使用小写 Mod ID，最长160字符；`key` 为1至120字符，仅允许字母、数字、下划线、连字符与点。实际标签键为 `EZAPI_ITEM_DATA_V1|<ownerId>|<key>`。命名空间防止正常作者相互覆盖，不是对恶意 Mod 的权限沙箱。

数据 envelope 为 `{"version":1,"data":{"Level":2,"Note":"..."}}`。版本必须为正整数，data不能为null。JSON由 System.Text.Json 默认选项处理：属性名区分大小写、仅序列化默认可见属性，作者类型必须可序列化/反序列化。回调 getter、构造器和 setter 应保持纯计算，不操作游戏。

`Read<T>(..., expectedVersion)` 遇到不同版本返回 `VersionMismatch`，保留原文、原版本和 `Revision`，不自动迁移。作者先检查是否认识旧版本，再解析 `Json` 中的 `data`，生成新类型后用该令牌 `Stage(..., version: 新版本)`。未知的未来版本应保留，不能降级覆盖。类型错误或损坏数据不会给出可写令牌。

`Revision` 绑定本次载入 Epoch、槽位、周目、物品实例、原生状态、父库存、堆叠数量和读取时的原始JSON。读档、场景上下文变化、物品移动/堆叠或其它代码改值后重新读取；不要保存令牌或跨回调缓存原生物品。比较令牌检查当前值，不承诺检测所有曾经发生后又恢复的变化。

## 返回状态与恢复

| 状态 | 含义与处理 |
| --- | --- |
| Missing | 数据缺失，读取不创建标签；可用提供的令牌初始化 |
| Present | 版本和类型匹配；返回值、原文和令牌 |
| VersionMismatch | 作者显式检查旧数据并决定迁移 |
| Invalid | 参数、格式、类型、配额或损坏标签；保留已有数据 |
| Disabled / Unavailable | 线程、周目、构建、状态或冻结能力未就绪；等待正确生命周期 |
| Frozen | 物品由当前交易/事务冻结；等待其完成 |
| Conflict | 读取令牌过期、活动拖拽或重入；重新观察当前玩法，不能直接重放消耗 |
| StagedInMemory | 官方写入完成，已读回确认；仍未证明游戏文件保存 |
| Indeterminate | 已进入原生写入但回调、同步或读回未确认；先读取当前物品对账，禁止自动重试 |

每份envelope最多16 KiB UTF-8、深度16；每件物品每个owner最多128键/64 KiB（键和值合计）；最多检查4096个物品标签。保护检查在作者序列化后、进入原生写入前及回调内再次执行。标签创建也可能先于回调失败，`Indeterminate` 可能留下部分状态。框架不清空、删除或替换整个物品状态，也不伪造自动回滚成功。

读取 `Read<JsonElement>` 可以查看有效数据，不会为损坏记录提供修复覆盖入口。缺失/禁用/损坏分别处理。接口不会给默认值冒充有效记录。

## 中英文与保存

中文字符串随JSON保留，稳定地址和字段名不要随游戏语言变化。存储等级、枚举和翻译键，展示时用 `GameLanguage` / `LocalizedItemText` 解析当前语言；不要把语言切换当作数据迁移。

物品复制、拆分和合并沿用原游戏标签语义；唯一序列号的业务必须自行设计复制规则。正常保存由游戏执行；保存后需在正确槽位的新加载过程重新读取数据，才可确认磁盘持久化。原生图编解码检查只能证明图恢复，不能替代保存/冷启动验证。

English: [Versioned item data](item-data.en.md)。完整签名见 [公开索引](public-api.index.json)，错误定位见 [错误索引](errors.index.json)。

> 历史验收记录（0.1.72），不作为当前 DLL 通过依据。
> 本轮五节点测试图已通过显式原生保存18项、新进程重载27项和独立磁盘回读37项检查，两套owner JSON及版本均保留。此证据不使Stage自动保存，也不保证所有游戏组合或崩溃恢复。 Evidence（历史标识 `verification/disk-supplier-native-0.1.72.json`；原件未公开）.
> 
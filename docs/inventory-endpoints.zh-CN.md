> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 持久库存端点（）

库存端点把一件由当前 Mod 拥有的根物品放进 EZapi 管理的隐藏 `PlayerStore.saveBags` 槽位，并将整张原生物品图以有版本的 JSON 保存在 `PlayerStore.modData`。它适合机器缓存、任务奖励柜等不需要直接显示在店铺主库存中的状态。物品创建、库存准入、存档落盘和后续加载仍由游戏原生流程负责。

## 最小用法

先在物品目录就绪前注册端点根物品；示例包已注册 `ezapi.sample.storage_box`：

```csharp
var endpoint = InventoryEndpointApi.Open(
    player,
    ownerId: "ezapi.sample",
    endpointId: "ezapi.sample.battery_cache",
    itemId: "ezapi.sample.storage_box",
    createIfMissing: true,
    chargingOnly: true);

if (endpoint is null || !endpoint.IsCurrent) return;
var contents = InventoryEndpointApi.CaptureContents(endpoint);
if (contents.Status != InventorySnapshotStatus.Ready)
    throw new InvalidOperationException(contents.Reason);
```

在当前已载入周目的游戏主线程调用。Mod 重载/初始化时再次调用 `Open`：同一槽位、周目和端点 ID 会取回原根；若尚无端点且 `createIfMissing` 为 `false`，返回 `null`，不会创建空替代品。`ownerId`、`endpointId` 和 `itemId` 都必须使用完整的小写命名空间，端点 ID 和物品 ID 必须以 owner 前缀开头。

`itemId` 必须创建出带原生 `GameGridInventory` 的容器根；普通物品、列表库存或缺少可用网格的容器会在端点创建/恢复时拒绝，避免开放不能存取物品的空端点。

`InventoryEndpoint.Root` 只在本次当前周目身份内有效。载入存档或切换周目后，旧句柄会失效；再次 `Open` 获取新句柄。不要把 `GameItem`、库存网格或 endpoint 句柄保存到静态字段、下一帧队列或跨场景缓存中。`Snapshot()` 返回当前端点句柄的托管只读列表；若存档状态尚未恢复，它会先尝试恢复。

## 快照、存入与取出

```csharp
var contents = InventoryEndpointApi.CaptureContents(endpoint);
if (contents.Status != InventorySnapshotStatus.Ready)
    throw new InvalidOperationException(contents.Reason);

var stored = InventoryEndpointApi.Store(endpoint, currentItem, maximum: 1);
if (stored.Indeterminate) {
    Log($"停止操作并人工核对：{stored.Error}");
    return;
}

var taken = InventoryEndpointApi.Take(endpoint, directChild, playerGrid, maximum: 1);
```

`CaptureContents` 只读端点网格的直接子项；`Store` 从物品当前所在库存存入端点；`Take` 只接受该端点网格的直接子项作为来源。两种搬运都复用 `ItemTransferApi.Move` 的单次原生接纳、前后快照和守恒核对，结果字段及失败处理规则相同。`Indeterminate=true` 时必须停止本批，不能自动重试或补发。EZapi 不替调用者创建待存入物品，也不自动保存。

原生搬运回调重入时，端点搬运返回 `Reentrant`，端点只读和删除操作则以 `EZAPI_ENDPOINT_REENTRANT` 拒绝，避免回调在同一物品移动期间修改端点或读取半成品快照。

## 端点和原生库存语义

- `createIfMissing` 只控制缺失端点时是否调用 `ItemApi.Create`。已有保存记录、原生键冲突或相同 ID 配置不一致时，API 不会生成第二个根。
- `chargingOnly` 不限制箱内物品类型，也不改变游戏原生准入规则。此项只控制原生电力枚举：设为 `true` 时，端点物品不作为家具供电源枚举；带 `power_source_item` 标签的拥有物品仍加入原生充电列表。
- `PlayerStore.FindAllItem` 的返回值会合并端点根及其子图，原有结果和其它 Mod 的列表不会被就地修改。游戏退出、加载和新建周目之前，EZapi 只解绑本组精确登记的根，不销毁正在显示或被其它系统使用的旧对象。
- 不要手动改写 `saveBags` 的 `EZapi.Endpoint/v1/` 键，也不要直接编辑 `EZapi.Endpoints/v1/state`。业务数据仍应放在你的 Mod 自有状态中；端点图负责保存该原生根及其库存子图。

## 保存与恢复

EZapi 不会调用 `SaveGame`，也不会主动触发保存。游戏调用原生 `PlayerStore.SaveGame` 时，EZapi 的前置钩子先编码所有端点图并写入专属 `modData` 键，再让原生保存继续。编码、身份或读回核对失败时，EZapi 会取消这次原生保存，设置 `InventoryEndpointApi.SaveError` 并导出诊断，避免把过期端点状态写进存档。先处理错误再退出游戏；不要盲目重试恢复。

`HookApi.GetRuntimeStatus()` 会列出 `ezapi.inventory.endpoints` 独立钩子组的完整目标及安装错误。端点 API 的 `IsAvailable` 还要求 `SaveGraphApi.CanDecode` 为 `true`，这样只有确认当前构建能恢复完整物品图后才允许创建或打开端点。不可用时查看 `InventoryEndpointApi.UnavailableReason` 和钩子状态；不要把未就绪解释成端点不存在。

> 历史验收记录（0.1.53），不作为当前 DLL 通过依据。
> 加载后，第一次 `Open`、`Snapshot` 或保存会读取当前槽位和周目对应的 JSON，使用 `SaveGraphApi.Decode` 重建完整图，核对实例 ID、根物品、所有子图别名，并在全部记录准备完成后才发布到原生 `saveBags`。任一记录失败时会封锁端点操作和保存，不会只发布一部分端点，也不会静默创建空库存。0.1.53已按当前构建真实加载序列恢复CanDecode，临时端点创建/查询/编码/删除验证14项通过；同版本补充验收已通过原生保存/新进程恢复/删除后冷启动，详细范围见下方新验收记录。原生加载回调失败时应查看 `UserData/EZapi/diagnostics.json` 的 `inventoryEndpoints` 和 `inventoryEndpointDebug`。

有界契约：每个存档最多 128 个端点；端点 JSON 总计最多 8 MiB；原生 `saveBags` 最多 4096 个键；每张原生图遵守 `SaveGraphApi` 的 4096 个物品、8192 个节点/边限制。超出时整次操作失败，不返回截断快照。

## 删除与释放

`Delete(ownerId, endpointId)` 只会删除属于调用 owner 的空端点。任何物品仍在图中、物品正在拖动、端点窗口仍可见、原生键被替换或句柄过期时返回 `false`。只有精确解绑原生键后才销毁空根；删除过程出现不确定结果会设置故障并阻止后续保存。调用者应先通过游戏原生库存 API 取回需要保留的内容，再关闭窗口并删除。

## 故障码

恢复前置缺失时，`InventoryEndpointApi.UnavailableReason` 会返回 `SaveGraphApi.DecodeUnavailableReason`，创建端点会在调用物品工厂前拒绝。它与已经执行过的恢复故障 `InventoryEndpointApi.Error` 分开报告。`EZAPI_SAVEGRAPH_UNSUPPORTED` 表示游戏构建未经核对或当前加载序列缺少精确签名；不要跳过检查继续创建端点。

`EZAPI_ENDPOINT_NOT_READY` 表示主线程/游戏目录/必需钩子尚未就绪；`EZAPI_ENDPOINT_ID` 和 `EZAPI_ENDPOINT_ITEM` 表示命名空间无效；`EZAPI_ENDPOINT_CONTAINER` 表示根物品没有有效原生网格；`EZAPI_ENDPOINT_STORE` 表示传入的不是当前精确 `PlayerStore`；`EZAPI_ENDPOINT_SOURCE` / `EZAPI_ENDPOINT_TARGET` 表示取出物品不是端点直接子项或目标网格无效；`EZAPI_ENDPOINT_CONFLICT` 表示 ID、配置或原生槽位冲突；`EZAPI_ENDPOINT_LIMIT` 表示超出有界资源限制；`EZAPI_ENDPOINT_REENTRANT` 表示同一原生操作重入；`EZAPI_ENDPOINT_ALIAS` 表示实例或物品图别名；`EZAPI_ENDPOINT_STALE` / `EZAPI_ENDPOINT_IDENTITY` 表示周目或句柄身份已变化；`EZAPI_ENDPOINT_RESTORE`、`EZAPI_ENDPOINT_RUNTIME`、`EZAPI_ENDPOINT_OPEN_INDETERMINATE` 和 `EZAPI_ENDPOINT_DELETE` 表示不确定状态，停止修改并检查诊断；`EZAPI_ENDPOINT_SAVE_BLOCKED` 表示原生保存被安全门控取消。

## 游戏内检查

调试面板的“库存端点检查”只创建并删除一个空临时端点，验证原生 `FindAllItem` 合并、普通物品准入和空端点删除。它不保存游戏。

“端点保存夹具”仅在 `SaveGraphApi.CanDecode=true` 且存档槽号大于等于 533 时使用：第一次点击会在专属调试命名空间创建一件物品并保存实例 ID 基线；通过游戏原生 UI 保存后，完整退出游戏并重启，在同一存档再次点击，诊断需显示 `coldRestartVerified=true`、`sameInstances=true`。这才表示跨进程读档核对通过。验证后点击“移除端点夹具”，并通过原生保存清理测试端点。历史槽位不会写入持久夹具。

## 本轮验收（0.1.36）

- Release 构建 EZapi、示例 Mod、内容校验器均为 0 警告、0 错误；10 个示例物品、机器布局、配方图鉴、掉落表和整包内容校验通过。
- SaveGraph 离线回归 44/44；端点 JSON 协议在 .NET 6 与 .NET 8 分别 27/27。
- 游戏 Version 050REV5-L（日志显示 0.46D），MelonLoader 0.7.3 / .NET 6；主菜单正常启动，确认只加载 EZapi 与示例内容包。当前安装 DLL SHA-256：`6581DA452450BA7DDD4A72EFE5B8E2B88A82E2AC42E368F2FBA01C54D548C65B`。
- `UserData/EZapi/diagnostics.json` 显示 `restoreAvailable=false`、`ready=false`、端点为空；缺少水培恢复入口的原因已由游戏进程确认。没有加载存档、创建端点或写入保存夹具。

因此本轮只验收了构建、离线协议和游戏主菜单加载。端点钩子就绪、实际存入/取出、原生保存、读档与冷启动恢复仍未验收；恢复入口补齐前，端点功能保持关闭。

0.1.53更新：旧版水培签名限制已由当前游戏真实加载序列替代。下述旧版不可用日志仅为历史记录，当前能力须读取本进程diagnostics与CanDecode。

## 0.1.53持久生命周期补充验收（2026-10-21）

使用用户准备的无教学测试槽534。原生每晚结算保存后退出并启动新进程：根110、子项111的ID、数量、父子关系一致；删除后再次原生保存并重启，只读查询确认缺失，磁盘端点数组为空。

实际执行开门→等待→F8调用原生EndAndClose→左侧出口→不带背包二次确认→配给站→返回；前后端点身份/数量/父子关系一致。返回后原生物品拖动及放下状态已核对。公开Store/Take通过12项；原生充电、去重、所有权过滤、枚举及隔离家具过滤器通过14项。家具函数的bool是isFromShip，不能在店铺当作ownedOnly调用；实际突袭和联机组合仍未验证。

536个ES3中，可丢弃槽534及原生存档索引变化，其他534个哈希不变；索引只有槽534预览和总游玩时间变化，其他槽预览保持一致。测试结束已退出游戏并移除独立测试DLL，只保留EZapi.dll。证据：`verification/endpoint-lifecycle-0.1.53.json`。本次属于同一DLL的补充验收，旧版发布ZIP与其证据保留。

> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 库存观察、规划与单次搬运（）

这些是独立 EZapi 实现，不依赖 Forge。适用于当前 Demo 049-REV5-L / MelonLoader IL2CPP .NET 6。接口签名由本机游戏程序集核对；原生接纳、机器槽位和保存重载仍待游戏验证。

## 先选择入口

| 需求 | 调用 | 是否写游戏状态 |
| --- | --- | --- |
| 当前周目物品实例列表 | `InventoryApi.CaptureRunItems(PlayerStore)` | 否；返回原生对象，只能本次同步使用 |
| 所有权 | `InventoryApi.IsPlayerOwned(GameItem)` | 否 |
| 拥有物品的总单位数 | `InventoryApi.CaptureOwnedItemCounts(PlayerStore, relevantIds)` | 否；返回只读托管字典 |
| 一个库存的直接内容 | `InventoryApi.CaptureDirect(GameInventory, includeGeometry:false)` | 否；返回纯托管快照 |
| 整件或整堆能否进入独立空位 | `InventoryApi.PreviewWholeGridTransfer(item, source, destination)` | 否；不把合堆或部分接纳判作整件落位 |
| 拖动是否进行中 | `InventoryApi.IsItemDragActive` | 否；含待开始/正在进行的群组拖动 |
| 为一组新产品规划落点 | `InventoryPlacementApi.PlanWholeGrid(target, products)` | 否；不预留空间，不提交接纳 |
| 真正搬运，最多N单位 | `ItemTransferApi.Move(item, destination, maximum)` | 是；恰好一次原生 `SlotMarker.TryAcceptOnce` |

所有原生调用在 EZapi 已初始化的游戏主线程、已载入周目执行。在 `GameEvents` 当前有效回调或自己的游戏内按钮中获取当时的原生对象；不要在初始化、后台线程、OnGUI绘制或跨场景持有对象后调用。原生调用拒绝过期周目，不能拿旧对象跨读档继续操作。

JSON 内容包继续用于物品/容器/掉落声明。这些运行时接口由 DLL 调用；本次没有“自动搬空背包”的 JSON 字段。所有示例仍不自动生成、搬运或保存。

## 只读快照

`CaptureRunItems` 合并原生 FindAllItem、saveBags 和主库存，遍历 GraphNodeStorage 的子图，按本进程指针去重。支持共享节点和循环；重复实例编号（包括负数）拒绝，编号不要求为正。最多4096物品、8192节点/边，超限抛异常，不返回截断列表。原生 FindAllItem 本身的成本不由这些托管上限限制。非图连接的自定义外部存储不在此范围。

`CaptureOwnedItemCounts` 按原生所有权累计单位数；过滤列表最多4096个ID，重复ID合并，缺席的请求ID返回0；不传过滤器则只列实际拥有的种类。整数溢出抛错，结果不随后续库存变化而改变。

`CaptureDirect` 返回 `NativeInventorySnapshot`，包含 Status、Run、NativeIdentity、ParentIdentity、InsertLocked、RemoveLocked、Grid、Items、Reason。Items是直接子项，不递归展开；每项有 NativeIdentity、InstanceId、ItemId、Units、PlayerOwned、Shape。默认只读身份/数量，Grid和Shape为null，支持普通库存和列表形式的库存。`includeGeometry:true` 加读真实modifiedShape及网格，不满足1..64有限形状约束时返回Invalid。`InventoryShape.Cells`是全局包围框逐行的字节十六进制；X/Y是全局网格坐标，方向/翻转另行记录。

0.1.18增加`InventoryShape.OutsideBounds`，保留原七参数构造器。物品Shape的非零格=占用、外部=0；库存Grid的非零格=禁止、0=可用、外部=1。两种遮罩不能直接当作同一种占格图。当前24×10店铺主库存实测内部全0、外部1，正确返回Ready。容器声明的`shapeRows`另以1表示可用，由适配层转换；内容作者不要自行反转。

快照可序列化为 JSON。NativeIdentity仅用于同一进程和Run.Epoch内的比较，不能作为持久化身份，也不能转回原生指针。读档/切场景后重新读取。Items只在Status=Ready时可用；读取中出现重复身份、错误父库存或无效数量，整份返回Invalid和空Items。线程/周目门控失败直接抛异常。

## 整件预检与布局规划

整件预检核对真实来源、所有权、MayRemove/MaxNumRemove、原生库存锁、玩家插入资格、所有权准入、拖拽与非法自嵌套，再找原生独立空位。候选后重读两边快照，避免准入回调改变数据后仍报Ready。Ready只说明此刻预检通过，不代表已交付。

`PlanWholeGrid` 接受0..64个不同的、未放入任何库存的单件原生实例。原有库存固定，规划只在内存中占位；按产品包围框面积从大到小、四个方向依次寻找落点，保留原来的翻转状态。每个候选都经过原生TryInventorySlot，并检查实际返回形状与候选一致，不绕过其它Mod的否决。支持网格洞、异形物品、已有占用、负坐标起点和旋转；输出顺序与输入一致，Shape均为独立克隆。

限制是有限网格每轴1..64、输入形状合计65536格；搜索阶段最多8192候选/65536格检查/40ms。40ms不是整个调用的硬时限，快照与原生单次调用仍占用主线程。贪心搜索不保证找到每一种数学上可行的布局。无方案、预算耗尽或目标/产品变化时抛出带错误码的异常，整组无结果，EZapi不移动任何产品。产品始终归调用者负责：之后重新核对并交付，或安全销毁未放置实例。禁止反复每帧创建产品来测试空间。

## 搬运结果与停止规则

`Move` 接受已在库存内、玩家拥有、可移除的当前物品和原生网格目标。maximum必须为正；实际请求限制为原数量、最大可移除量、原生槽位可接受量的最小值。支持整件、拆堆、合堆、部分容量；目标不能是来源或物品自己的子库存。

调用前后核对当前周目、库存身份/父节点/网格/锁，以及直接内容。源物品减少数必须等于目标同ID增加数；其余源项不变，目标只能有一个新项或一个堆叠数量增加，原有目标项不能消失/移动/变归属。原物品可能在合堆后被原生销毁，审计只比较新快照，不再读取旧包装对象。

`ItemTransferResult`：

| 字段/状态 | 调用者处理 |
| --- | --- |
| Moved | 经源/目标差额核对的移动单位数，不能只看NativeReported |
| Indeterminate=false，Status=Moved | 请求的最多数量已确认移动 |
| Indeterminate=false，Status=Partial | 原生容量/移除规则只允许部分数量；按Moved扣减待交付量 |
| Indeterminate=false，Status=Rejected/NoSlot/Stale/Reentrant | 未由EZapi提交接纳或已验证无变化；处理Error，不循环重试 |
| Indeterminate=true，Status=Indeterminate | 立即停止整批，保留诊断和待处理记录，重新人工核对实际库存 |
| NativeReported / Error | 原生返回值与异常，供诊断；不是交付事实 |

写后异常即使能确认差额，仍返回Indeterminate=true，同时保留已确认Moved。无法证明守恒时Moved=0不等于“实际未移动”。EZapi不直接改parent/数量、不先Expel、不盲目补发或回滚；这些操作可能在合堆/回调后复制或丢失物品。本API不是保存事务，不保证所有原生副作用可逆，也不证明物品状态字段在恶意回调下完全不变。批量调用遇到Indeterminate必须停，不能整批回滚/重试。

## AI最小调用示例

```csharp
// 在自己的主线程按钮回调中获取当前对象；不要缓存到下次读档。
var player = Il2Cpp.PlayerStore.instance;
var snapshot = EZapi.InventoryApi.CaptureDirect(player.gridInv);
if (snapshot.Status != EZapi.InventorySnapshotStatus.Ready)
    throw new InvalidOperationException(snapshot.Reason);
var counts = EZapi.InventoryApi.CaptureOwnedItemCounts(player,
    new[] { "ezapi.sample.wound_dressing" });

// sourceItem和targetGrid必须来自本次界面/场景；不在绘制回调里写。
var result = EZapi.ItemTransferApi.Move(sourceItem, targetGrid, maximum: 1);
if (result.Indeterminate) {
    // 停止本批次，记录所有字段并提示核对；这里不能自动重试/补发。
    Log($"停止搬运：{result.Status}, 已确认={result.Moved}, 原生={result.NativeReported}, {result.Error}");
    return;
}
Log($"本次已确认移动 {result.Moved} 单位：{result.Status}");
```

`sourceItem`、`targetGrid`、`Log`由你的Mod提供。可编译的函数示例在 `src/EZapi.Sample/InventoryExamples.cs`，没有自动执行的搬运回调。CLI不运行这些原生接口。

## 玩家验证

游戏中点击“EZapi调试 → 读取库存”，检查 `UserData/EZapi/diagnostics.json` 的 `inventoryDebug`：实例总数、拥有数量、最多16份直接库存快照。它只读，不创建、不搬运、不保存；没有其它存储时只显示主库存正常。

换容器、正常拖放、外出回店和重载后再次点击，核对数量与Run.Epoch，旧快照不能被当成新周目数据。这个按钮不证明写入API可用。真正搬运需在测试档由调用Mod按上述函数测试：整件、拆堆、合堆、满库存、锁定、正在拖动、部分接纳；每次核对两边数量、位置和归属，然后用游戏自身保存重载验收。

离线检查链接真实库存实现，替身模拟原生槽位；它们不能证明IL2CPP接纳/保存效果。冻结、通用准入注册、列表创建、持久端点与物品图恢复尚未开放，仍在Forge全量覆盖清单内。

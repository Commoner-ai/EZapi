> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 库存准入、短期冻结和列表库存

这组接口补齐 Forge 对应的库存扩展能力，独立运行于 EZapi。以本机 Demo 049-REV5-L 为准，不加载 Forge。原生钩子和公开接口已编译核对；离线模拟不能证明游戏 detour、UI 或保存恢复已经通过。

## API速查

| API | 用途 | 调用时机 |
| --- | --- | --- |
| `InventoryAdmissionApi.Register(ownerId, itemId, inventoryId, allows)` | 为指定容器定义/子库存追加准入规则，返回可释放句柄 | EZapi初始化后主线程，可在目录就绪前排队 |
| `InventoryAdmissionApi.IsAvailable` | 当前主线程原生准入钩子是否完整 | 初始化后；目录前为false |
| `InventoryFreezeApi.TryAcquire(items, group:null)` | 一次认领整组实例及已有子图，冲突返回null | 当前载入周目主线程，拖动结束后 |
| `InventoryFreezeApi.IsFrozen(item)` | 当前实例是否有有效冻结 | 当前周目主线程 |
| `InventoryFreezeApi.IsAvailable` | 原生移除/接纳守卫是否完整 | 目录就绪后 |
| `ListInventoryApi.Create(owner, inventoryId)` | 创建独立可扩展原生库存图 | 原生工厂重建未放置单件时 |
| `ListInventoryApi.Find(owner, inventoryId)` | 按稳定键和真实父物品找回库存 | 当前主线程，不缓存跨读档 |
| `ListInventoryApi.IsList(grid)` | 检查EZapi尺寸标签 | 当前主线程 |
| `ListInventoryApi.Move(item, destination, maximum, accepts:null)` | 按需扩容，资格复核后执行一次原生接纳和守恒审计 | 当前周目主线程，由内容Mod显式调用 |
| `HookApi.TryValidateNativeTarget(method, out reason)` | 检查生成方法元数据及可执行入口 | 官方目录就绪后主线程 |
| `HookApi.GetRuntimeStatus()` | 读取三组钩子的就绪状态/完整目标/失败原因 | 运行时诊断 |

JSON格式继续为0.1；本轮没有JSON冻结、自动搬运或自定义工厂字段。DLL调用示例随源码提供于 `src/EZapi.Sample/InventoryGuardExamples.cs`，示例函数不会自动执行。

## 准入规则

`itemId`是**目标容器本体的物品定义ID**，不是要拖进去的物品ID。`inventoryId`是该物品实际子库存的稳定identifier。回调参数incoming是准备放入的物品，target是当前原生网格。例：

```csharp
// 在内容Mod初始化保存这个IDisposable；卸载/关闭功能时Dispose。
var lease = EZapi.InventoryAdmissionApi.Register(
    "author.medical", "ezapi.sample.storage_box", "ezapi.sample.storage_box.storage",
    (incoming, target) => incoming.identifier is
        "ezapi.sample.wound_dressing" or "ezapi.sample.reusable_dressing");
```

按(ownerId,itemId,inventoryId)去重，最多256条规则；不同owner规则按AND组合。原生已经拒绝时不调用回调，回调不能放宽原生所有权/空间/过滤限制。只作用于匹配的、玩家拥有的容器；不匹配的普通库存沿用原生行为。Dispose幂等，可从清理路径调用；旧句柄不能删掉后来重新注册的规则。

回调必须同步、只读、快速，不修改数量/父图/标签/窗口，不递归查询相同准入。重入会拒绝；异常会把该条规则锁为拒绝直到Dispose并重新注册，且记录 `EZAPI_ADMISSION_CALLBACK`，避免每帧重复异常。身份/单位数突变也拒绝，但框架不保证捕获回调的所有隐蔽副作用。读档重建阶段没有有效周目时保持惰性，不阻止原生恢复物品。

准入使用 `MayHaveValidInventorySlot`、`CheckInventorySlotStillValid`、实际 `TryInventorySlot` 返回值及 `SlotMarker.TryAcceptOnce` 四个明确入口。后者在真正接纳前重查，旧槽位不能绕过后注册的规则。它不阻止恶意Mod调用UncheckedAccept或直接改父图。

## 冻结与释放

```csharp
using var held = EZapi.InventoryFreezeApi.TryAcquire(currentItems, "author.mod.operation");
if (held is null) return; // 原生钩子不可用、正在拖动、其它group占用或库存类型不支持。
// 这里只读取/确认，不修改库存；using作用域结束即释放。
```

整组先检查再提交，不会只冻结半组。输入按指针去重，并包含容器现有后代，最多4096物品/8192图节点边/256活动句柄。默认group每次独立；相同显式group可嵌套引用计数，不同group冲突返回null。使用有归属的稳定命名，只有同一操作才能共享group。

原生MayRemove返回false，MaxNumRemove归零；网格/滚动网格/单槽库存的Expel拒绝；槽位接纳同时检查源物品、目标堆叠物品和目标容器。冻结保护的是实际实例和当前Epoch；Dispose只处理托管计数，不改游戏存档，也可在异常清理中从后台释放。场景切换清空，读档的Epoch变化使旧冻结失效；每次清空还会更新独立代数，因此旧句柄不能解除新Epoch或同Epoch新场景/重启后的冻结。

冻结不提供“持有者越权移动”入口：持有期间Move也拒绝。需要移动时释放并重新预检。它不覆盖Destroy、战斗消耗、低层直接写数量或其它Mod故意绕过的Unchecked操作，也不预留保存文件。不要把长时间冻结当作交易提交协议。

## 列表库存与恢复

列表能力使用独立原生网格和窗口，从16×16开始；空间不足时扩展，最大64×64。名称表示可追加库存能力，界面仍使用游戏原生网格。`Create`只接受未放置单件、无已有窗口的owner，不能覆盖其它Mod或原有容器的存储图；每次工厂创建/重建必须使用同一inventoryId。若owner已带有尺寸标签，Create保留并恢复尺寸，不用默认值覆盖。

尺寸写入owner的 `EZAPI_LIST_WIDTH` / `EZAPI_LIST_HEIGHT` 原生状态标签，使用游戏自己的物品状态保存。恢复钩子在原生网格接纳前恢复已有标签尺寸；不改SaveManager或自动保存。缺一标签、越界、缩小都会拒绝，避免将已有物品排除网格。不是通用存档迁移：已有固定容器改成列表、缩小容量或更换库存ID均需专门迁移，不能只改声明。

`Move`支持可选 `(item, amount) => bool` 只读资格回调。先捕获源/目标、执行回调并复核，再检查原生准入。空间不足时扩容并读回尺寸/标签，最后复用 `ItemTransferApi.Move`。回调改变状态、扩容失败或写后异常返回Indeterminate，停止整批；即使Moved=0也不能断言没有尺寸/回调副作用。资格回调异常不会自动补偿。拒绝后已经扩大的空余网格保留，不自动缩回。

这项能力的存档恢复仍需真实保存重载验收。原生工厂接入是内容Mod职责；EZapi普通JSON container仍使用原有固定独立网格，不会自动升级成列表。

## 原生钩子能力与兼容

三组独立owner：`ezapi.inventory.freeze`、`ezapi.inventory.admission`、`ezapi.inventory.lists`。官方目录事件后安装，精确匹配声明类型、返回类型和参数；通过Interop公开版本适配器读取生成方法元数据和入口，不手算偏移，不输出地址。组内必需目标共享原生地址、无入口、安装失败或缺少所有者时，关闭整组；清理只撤销自己的owner。

TryValidateNativeTarget只做入口检查，不证明该方法适合补丁、没有其它Mod冲突或游戏行为正确。GetRuntimeStatus返回Ready、Targets、Error；首次进游戏查看 `inventoryGuards.groups`，不能从编译时的FrameworkInfo能力声明推断运行钩子成功。已装回调统一受组门控约束，部分安装失败不会留下活跃半功能。初始化和卸载清理均处理本组句柄/补丁。

本轮使用unsafe只为调用Interop的版本化元数据适配器。没有手算对象偏移或执行裸地址；具体目标/阶段/边界见 `patches.index.json`。其它Mod无条件覆盖返回值仍可能改变最终行为，组合兼容需要游戏验证。

## 玩家手动验证

1. 重启测试档，导出诊断；`inventoryGuards.groups`三组应Ready=true，Error为null。如失败先提供日志，不要把不可用能力用于正式存档。
2. 主库存放一个容易辨认的普通物品，结束拖动，点“冻结主库存首件5秒”。面板显示实际被冻结的名称。5秒内尝试拖出应被阻止；到期下一次主线程Update自动解除，也可点“解除测试冻结”。不修改数量、不搬运、不保存。
3. 外出回店或读档后不应残留冻结；导出 `freezeDebug` 和 `inventoryDebug` 对照实际情况。游戏卡住时计时释放也要等主线程恢复，这是帧回调限制。
4. 准入与列表需调用Mod加载示例函数后测试。准入至少测允许/拒绝、锁定、回调释放、旧槽位提交；列表至少测两件owner内容独立、扩容、保存重载与超过初始尺寸的物品位置。调试冻结按钮不能证明准入或保存已验收。

离线检查链接真实安装器、服务和补丁回调，模拟Harmony/Interop和原生对象，验证门控、失败清理、释放、准入和尺寸逻辑。它们不运行真实原生detour；本轮不启动游戏。

# EZapi 0.1.56：液体输入与输出容器条件

`MachineContainerSlot.Condition` 限制某个液体输入槽中的容器；`MachineOutputTemplate.ContainerCondition` 限制已有液体输出容器，要求 `Kind="container"`。两者均为 `Func<GameItem,bool>`，与原生规则、固定 ID/标签和配方条件取交集，不能放宽原生拒绝。

```csharp
var template = new MachineLayoutDefinition {
    Output = new MachineOutputTemplate {
        Kind = "container",
        Size = new MachineGrid(2, 3),
        // 示例：只允许作者自己的容器。不要依赖显示名称。
        ContainerCondition = item => item.identifier == "author.tools.bottle"
    },
    LiquidInputs = new[] {
        new MachineContainerSlot {
            SlotId = "water",
            Size = new MachineGrid(2, 3),
            Condition = item => item.IsTag("author.tools.accepted_vessel")
        }
    }
};
```

使用 `MachineRegistrationApi.RegisterMachine` 时将该模板放入 `MachineDefinition.Template`，框架自动安装和持有规则句柄，声明失败会撤销本次安装。快照复制会保留 C# 委托，调用者修改原列表不会改变登记规则。

低层 `MachineLayoutApi.Create` 用户需先调用：

```csharp
// 初始化主线程声明一次，保存句柄至Mod停止时释放。
IDisposable conditions = MachineContainerConditionApi.Register(
    "author.tools", "author.tools.machine", template);
// 此后工厂对每个新实例调用MachineLayoutApi.Create(item, template)。
// 停止：conditions.Dispose();
```

低层布局含条件却未登记对应规则时，创建会以 `EZAPI_MACHINE_CONDITION_MISSING` 拒绝，避免条件被静默丢弃。同 owner/机器重复登记拒绝，替换前释放旧句柄；不同 owner 对同一机器的相同布局按 AND 组合，最多256个非空规则句柄。布局内容不同会拒绝。条件委托可引用稳定定义和纯托管配置，不可捕获周目物品、库存或窗口实例。

## 准入与加工

原生拖入检查通过既有 IL2CPP 委托接收器路由，只作用于指定机器类型的精确 `output` 或 `liquid.<slotId>` 子库存，不改店铺等其他库存。加工时重新检查实际输入/输出容器，提交前再次检查：容器虽然已在槽内，也不能绕过后来变化的条件。

条件是可重复查询的只读函数，不保证只调用一次。不得移动物品、写标签、扣钱/电、创建产物、读档或切换场景。框架检查当前周目、物品/机器身份及状态字典、标签值、价值、父库存和目标库存；异常或已观察状态被修改会停用该规则并拒绝后续操作。框架不会撤销违规回调对任意状态的副作用。递归检查同一物品/目标会拒绝。

`MachineContainerConditionApi.Snapshot()` 输出纯托管视图：`OwnerId/MachineId/Slots/Faulted/Error`。故障后先修正回调，再释放旧句柄并重新登记。统一注册托管的句柄随框架停止释放；更新内容代码应退出并重新启动游戏。

## JSON与读档

委托仅在 C# API 中可用，不写入布局 JSON 或存档。JSON 中填写 `condition` 或 `containerCondition` 会拒绝；不会把表达式字符串当代码执行。命令行校验器仍只依赖纯托管声明，无需加载游戏程序集。

每次启动由 Mod 初始化重新登记。原生存档重建期间不调用作者条件，保留游戏恢复物品图的流程；进入有效周目后，准入和加工均重新执行条件。无需把原生引用缓存到静态变量。真实磁盘保存/新进程恢复需单独验收，不能以同进程查询代替。

这些接口独立实现对应的公共功能；Forge只作为功能和公开形状参考。原生槽位仍使用当前官方 `GameSlotInventory` 和既有委托适配，不新增生产 Harmony 钩子。

> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi自定义机器窗口借用库存与模板绑定

`MachineBatchProcessor.Process(machine, inventory)` 让自己建立窗口的机器复用配方选择、真实材料消费、液体、电源、产物与同步补偿。无需伪造 EZapi 布局标记，也无需重新注册机器物品。原来的 `Process(machine)` 继续自动解析 EZapi 布局。

## AI 应按这个顺序使用

1. 若要从零声明机器，优先用 `MachineRegistrationApi.RegisterMachine`，它会创建窗口并设置原生槽位准入。
2. 只有已有自定义窗口需要复用加工时，才建立 `MachineInventory`。每个引用必须是目标机器当前窗口的真实子库存，不可拿玩家背包或另一机器补齐槽位。
3. 用 `MachineRuntimeApi.CreateBatchProcessor(owner, definition)` 建立独立处理器。该入口复制并绑定 Template、配方、电源和加价；不会注册机器、窗口、准入规则或夜间任务。
4. 在已载入周目的主线程同步调用一次 `processor.Process(machine, inventory)`，检查 `Committed` 与完整事务收据。停止后旧处理器失效，不能自动循环重试失败批次。
5. 换场景、换窗口、读档后重新取得库存引用；不保存库存或回调上下文到下一帧。

```csharp
// 初始化时只保存定义和处理器，不捕获任何周目物品。
var processor = MachineRuntimeApi.CreateBatchProcessor("author.tools",
    new MachineDefinition("author.tools.press",
        new() { ItemInput = new(4, 4), Output = new() { Size = new(4, 4) } },
        new[] {
            new MachineRecipe("author.tools.press_recipe",
                new[] { new MachineIngredient("author.tools.material", 2) },
                new("author.tools.product"))
        }) { Power = new(0) });

// 玩家按下加工按钮时；input/output来自ownedMachine当前窗口。
var inventory = new MachineInventory(null, null, input, output,
    new Dictionary<string, GameSlotInventory>(), null);
var result = processor.Process(ownedMachine, inventory);
if (!result.Committed) {
    // 显示实际Error/Transaction；RecoveryFailed应交人工恢复，不补发。
}
```

需要的命名空间为 `EZapi`、`Il2Cpp` 和 `System.Collections.Generic`。可编译的同类示例在 `EZapi.Sample/MachineRegistrationExamples.cs`。

## 借用与边界

| 情况 | 行为 |
| --- | --- |
| 自定义窗口无 EZapi 标记 | 显式传入真实子库存即可；单参数 Process 无法猜测角色 |
| EZapi 标记布局 | 传入的每个槽位、角色、液体 ID 和 RequiresBattery 必须与原生解析一致，防止交换或省略 |
| 另一机器库存、重复槽位、失效引用、无窗口 | 在作者回调和产物创建前拒绝 |
| 调用者修改原字典 | 本次使用独立、只读、按 ID 排序的快照；下次重新借用 |
| 回调改父图、换窗口或改变模板尺寸 | 本批失效；框架不替作者撤销任意回调副作用 |
| 借用视图 RequiresBattery=true | 即使零电耗也需要有效电源；绑定模板也可加强该条件，不能降低自有布局条件 |

`EZAPI_RECIPE_INVENTORY` 表示库存身份、角色或模板结构不匹配。`EZAPI_RECIPE_BATTERY_REQUIRED` 表示结构正确，但必需的电源当前不可用。

## Template 绑定的实际范围

绑定器核对输入和输出尺寸、物品/容器输出种类、电池和模块是否存在及尺寸、说明书槽、液体槽 ID 和尺寸。模板的液体 AllowedIds/AllowedTags/Condition、输出 ContainerCondition 与配方条件取交集，在准备和提交边界复查。

绑定器不会给自定义窗口安装模块、拖放、电池或说明书的原生委托。窗口作者仍需按官方接口设置这些交互；模块 AllowedTypes 等原生交互行为由窗口创建方负责。Title、Draggable、标签和显示名称不参与生产结构匹配。委托只存在于当前进程，不序列化成 JSON。

低层 `MachineRecipeApi.CreateBatchProcessor(owner, machineId, recipes, ...)` 保持无 Template 的用途；若希望加工器核对完整声明，使用 Runtime 入口。二者共用标准事务和代际保护，不会添加第二次夜间生产。

离线和实机结果以本阶段验证报告为准。显式同步加工通过不能单独证明跨夜、磁盘保存重载或第三方组合兼容通过。

> 历史验收记录（0.1.60），不作为当前 DLL 通过依据。
> 本阶段：配方离线459/0、自动化217/0，49套离线工程通过；正式3工程零警告零错误。真实六组回归829/0（机器注册124/0，含8种借用模式）。退出后536份ES3未变；见验证报告（历史标识 `verification/inventory-binding-native-0.1.60.json`；原件未公开）。
> 
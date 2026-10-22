# 机器的必需电源与工厂配置边界

适用 EZapi 0.1.59。此阶段补齐 `MachineBatteryTemplate.Required` 的执行行为；此前它只进入布局元数据，加工没有执行该条件。

```csharp
var machine = new MachineDefinition(
    "author.mod.press",
    new MachineLayoutDefinition {
        ItemInput = new(3, 3),
        Output = new() { Size = new(3, 3) },
        Battery = new(new(2, 2), Required: true)
    },
    new[] {
        new MachineRecipe("author.mod.press_recipe",
            new[] { new MachineIngredient("author.mod.material") },
            new("author.mod.product"))
    }) {
    Power = new(0),
    ItemOptions = new("压制机")
};
var result = MachineRegistrationApi.RegisterMachine("author.mod", machine);
if (!result.Accepted) throw new InvalidOperationException(result.Reason);
```

这个示例不扣电，但必须在电池槽放入游戏认可的电源。没有电源时返回 `EZAPI_RECIPE_BATTERY_REQUIRED`，不会调用作者条件、创建产物或消费材料。

| 配置 | 零电耗 | 正电耗 |
| --- | --- | --- |
| 无 Battery 模板 | 可以加工 | 无法满足供电 |
| Battery.Required=false | 允许电池槽为空 | 仍需足够且可扣除的能源 |
| Battery.Required=true（默认） | 必须有有效电源 | 必须有有效电源且能源充足 |

有效电源须属于玩家、没有被其他事务冻结，并通过原生 `PowerHelper.CanDrawPowerSource(source, 0)` 检查。原生认可的空电池可以满足“在场”条件；它仍不能支付正电耗。已登记的外部电源连接器也适用：在线但零能源可满足零电耗，断开则拒绝，零电耗不会创建提供方事务或调用 Debit。

创建布局和 `TryGetInventory` 都从同一模板填入 `MachineInventory.RequiresBattery`。这是本次同步借用视图的说明，不修改已保存布局格式，不需要给 JSON 增加字段。手动 `ProcessBatch`、独立处理器和夜间调度共用同一加工检查；产物 Prepare/事务 Validate 前后会重新核对资格。作者条件/Prepare 必须遵守现有只读约定，框架不会替作者恢复任意违规副作用。

## ConfigureItem 的约束

机器工厂中的 `ConfigureItem` 可设置外观、形状、数值和标签。返回后必须仍然满足注册 ID、单件、游离、尚无窗口；窗口交由 EZapi 建立。改变 ID、堆叠数量、提前建窗口或附着到玩家库存时，在布局建立前拒绝，错误为 `EZAPI_MACHINE_CONFIGURE`。框架清理仍游离的本次实例；已被错误回调附着的对象保留，避免删除玩家库存。作者不可依靠回调失败获得交付。

## 检查路径

离线配方测试新增必需/可选/空能源/冻结/所有权和 Prepare 后重检；注册测试覆盖 ConfigureItem 四种违规结果。调试面板的“机器注册检查”追加八个临时原生场景，包含真实电池及外部连接器。测试不会向玩家交付这些机器、申请保存或证明真实跨夜恢复。

完整机器注册契约仍保留在总目标内。图片、液体、容器材料及外电的已有独立验收不等于全部 DTO、边界与跨夜存档生命周期都已完成。

最终六组实机回归793/0，其中机器注册88/0；三种零能源模式均用SetPowerSourceAt(0)设置并读取原生值验证。49套离线检查、正式3工程零警告零错误。退出后536份ES3未变，不声称真实跨夜或保存重载通过。见[证据](verification/required-power-native-0.1.59.json)。

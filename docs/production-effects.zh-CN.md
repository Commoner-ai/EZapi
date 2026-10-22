# 生产计价、功耗与效果注册（0.1.15）

EZapi 独立实现这些契约，没有引用或打包 Forge。用户要求的最低范围仍是全部 Forge 公共能力；此版本新增两个 API 组，不代表整个目标完成。只依据本机签名编译和离线行为检查，尚无真实 IL2CPP 随机回调、机器生产或保存重载证据。

## 生产计价

`MachineItemTake(Item, Count).Value` 表示本次 **Count 件的总投入价值**。默认零，需要加工调用者填入实际材料价值。`MachineLiquidTake(SlotId, Container, Before, After, Value)` 的 Value 是消耗液体的总价值，不含容器价格；QualityBasis 单独保留。InputValue 默认将所有 Value 相加，不再乘 Count。选择器返回对应记录的总价值；异常原样传出。

`ProductionValueApi.Calculate(context, markupPercent, itemValue=null, liquidValue=null)` 使用明确传入的加成，不再叠加 `context.ProductionMarkupPercent`。该属性留给加工策略自行读取。计算不会检查材料所有权、消耗材料、扣电或生成产物。

`ProductionValueMath.PerOutput(inputValue, markupPercent, outputCount=1)` 与 `PerOutputWithOverhead(inputValue, markupPercent, batchOverhead, outputCount=1)`：

```
单件价值 = ceiling((投入价值 × (100 + 加成百分比) / 100 + 整批额外成本) / 产量)
```

例如投入100、加成20%、整批成本11、产量3：单件44。额外成本在加成后计入一次。投入、加成和成本不能负数，产量1..256。decimal中间值或最终long溢出会抛异常，不能饱和截断。

`MachinePowerMath.CalculateCost(cost, outputCount, perOutput)` 只计算电费。perOutput=false返回一批cost，true返回checked(cost×产量)。cost非负，产量始终检查1..256。原生机器效率已经计入实际电耗时，不得再次套效率。

## 批次数据

`MachineBatchContext(machine, items, liquidContainers, outputCount)` 复制Items、LiquidContainers和init属性Liquids集合，每个最多4096项，避免调用方改List影响已捕获批次。记录含原生GameItem引用，只在当前同步加工回调内使用；不能跨await、场景、读档或下一帧缓存。它不自行证明一批加工成功。

`MachineLiquidPart(LiquidId, Parts)` 提供可选Value/QualityBasis。`MachineLiquidSnapshot(capacityParts, contents)` 复制成分，拒绝重复ID、负数和超容量；1000parts=1ml。可用 `MachineLiquidSnapshot.From(Liquids.Capture(container))` 转换实际观察值。不要将总价值快照误当实际倒液能力。

## 基础价值标签

`ProductionValueApi.SetBasis(item, value)` 仅在主线程、当前已载入周目操作活物品，写 `EZAPI_PRODUCTION_BASIS_V1` 的InvariantCulture字符串并启用标签、SyncModifiedState和unitValue，最后严格读回。标签独立于其它框架；不会自动迁移Forge标签，不自动保存。

`ReadBasis(item, fallback=0)` 只读；没有标签或空字符串返回fallback，非法格式、负数、超long记录抛 `EZAPI_PRODUCTION_BASIS_CORRUPT`，不默默改写旧数据。

写后失败尝试恢复原TagState对象和原unitValue；`WRITE_RESTORED`表示本次内存写入已恢复，`RECOVERY_FAILED`表示身份、周目、字典、标签替换或恢复核对失败，停止本批并检查日志，不盲目重试。正常保存能否保留基础价仍待玩家验收；此标签不会自动重新覆盖其它游戏计价结果。

## 效果注册

在调用者 `OnInitializeMelon`、EZapi初始化后调用：

```csharp
var accepted = EffectApi.RegisterEffect("demo.mod", "demo.mod.fixed-effect", () =>
    new ModuleEffectHelper.ModuleEffect {
        identifier = "demo.mod.fixed-effect",
        displayName = "稳定效果",
        description = "请在工厂内设置自己的原生效果回调。"
    }, randomEligible: false);
if (!accepted.Accepted) throw new InvalidOperationException(accepted.Reason);
```

RegisterEffects(ownerId, IReadOnlyList<NativeEffectRegistration>)整批预检，最多256项/批、4096项/进程，ID使用owner点前缀且ASCII字母/数字/点/横线/下划线。声明不会调用工厂，返回Accepted只表示Staged。工厂在官方物品目录就绪后调用，返回独立ModuleEffect并设置相同identifier。原生回调委托由作者转换和持有；工厂闭包应持有转换后的委托，不得捕获场景对象。直接原生回调并不经过EZapi异常隔离，作者需要在回调内处理异常。

Snapshot()提供Staged/Applied/Conflict/Failed，ContentApi发布实际转换；工厂单项异常不会中断其它项。冲突不覆盖原生目录。相同目录不重复创建，目录替换后重新应用；此过程不迁移已存在模块上的效果。

randomEligible=true直接进入原生随机池；false仍可通过原生AddEffectToModule按ID附加，但原生InitRandomEffect期间临时排除。排除只针对本框架插入的确切对象，独立每次调用作用域，finalizer覆盖正常/异常/嵌套退出；保留原始游戏异常、其它Mod替换和原来的字典。不会重抽已生成的效果槽。

有排除声明时，必须先成功安装 `ezapi.effect-random` 的精确 `ModuleEffectHelper.InitRandomEffect(GameItem,int):void` Prefix/Finalizer，才允许排除效果进入目录。缺钩子将相应项标Failed，允许随机的项仍可应用。owner丢失通过最多每秒一次检查撤下本方排除项；这不是与其他Mod强行卸钩子的原子协作保证。关闭时只撤销本方确切对象，移除失败保留工厂/效果强引用并报告，不释放仍在原生使用的委托。游戏退出和存档迁移是不同事项。

## 调试与玩家验收

左上角“EZapi 调试”→“查看生产/效果”显示纯计算与只读状态，写 `productionDebug`、`effects`。默认十件JSON物品不额外注册测试效果，因此effects.entries可能为空，hook.Ready=false且无目标属正常。验证真实效果需使用示例DLL调用者显式注册，不能把空清单当效果已测试。源码示例见 `../src/EZapi.Sample/ProductionEffectExamples.cs`。

正常开档确认计价示例44/件、电费7/批或21/三件；查看效果Applied与随机钩子。需要验证原生随机/附加效果、实际回调与保存重载时由玩家操作并提供日志。框架不会自动调用随机入口、EndNight或SaveGame。

## 当前构建变化

本机Assembly-CSharp SHA256从此前记录的BCA96232539EB85F3D8C4E2C6EEFC1F68BC623A7D0226BD0FBD9112C3C670C6E变为E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF。原先 `MachineHydroponic.LoadHydroponicModuleInvData(GameItem):void` 已不在公共签名表，也没有找到同名替代。SaveGraph.Decode在任何原生创建前完整检查，缺失抛 `EZAPI_SAVEGRAPH_UNSUPPORTED`；Items/Encode保留。不能省略必要恢复步骤后把解码宣称为成功。后续需依据当前游戏实际恢复链完成适配。按钮记录saveGraphDecodeSignatureAvailable供排查。

> 0.1.62：复杂材料图、液体物品/容器产出、普通电池及外电已补充两晚真实保存和冷启动验证；下方历史边界以本条限定的矩阵更新，完整范围见[跨夜指南](machine-persistence.zh-CN.md)。不包含原版熔炉所有材料策略或崩溃恢复。

# EZapi 0.1.55：向已有容器输出液体

机器模板的 `Output.Kind="container"` 表示玩家放入一个现有液体容器。配方使用 `MachineContainerOutput`，框架直接向该容器加入整批液体，保留原有成分和容器身份，不创建新物品。需要生成一瓶新产品时继续使用 `MachineItemOutput.Contents`。

先在初始化阶段注册材料、机器和液体目录引用，再注册配方；进入周目后取得玩家拥有且具有 EZapi 布局的机器实例。完整可编译调用见 `src/EZapi.Sample/MachineRecipeExamples.cs` 的 `RegisterLiquidOutput`。

```csharp
using EZapi;
using Il2Cpp;

// machineId/materialId/liquidId必须是已注册的真实ID。
// 此模板用于机器工厂建立布局，或交给MachineDefinition.Template。
var template = new MachineLayoutDefinition {
    ItemInput = new MachineGrid(4, 4),
    Output = new MachineOutputTemplate {
        Kind = "container", Size = new MachineGrid(2, 3)
    },
    Battery = new MachineBatteryTemplate(new MachineGrid(2, 2))
};

var submission = MachineRecipeApi.RegisterRecipes("author.tools", machineId,
    new[] {
        new MachineRecipe("author.tools.liquid_product",
            new[] { new MachineIngredient(materialId, 2) },
            new MachineContainerOutput(new[] {
                new MachineLiquidAmount(liquidId, 2, ResolveValue: _ => 10m) {
                    ResolveQualityBasis = _ => 3m
                }
            }))
    }, new MachinePowerRule(7));
if (!submission.Accepted)
    throw new System.InvalidOperationException(submission.Reason);

// 在周目主线程按需调用一次；machine是当前玩家机器。
// var result = MachineRecipeApi.ProcessBatch(machine);
// 只有result.Committed才表示已完成，不能因Created为空判断失败。
```

这里的模板声明不会自行注册或生成机器。统一注册请使用 `MachineRegistrationApi.RegisterMachine`，布局低层入口见 `machine-layouts-guide.zh-CN.md`。不要把每次加工写进逐帧回调。

## 数量与动态输出

固定 `Contents` 的 `MachineLiquidAmount.Millilitres` 是整数毫升，`ResolveValue/ResolveQualityBasis` 返回该成分本批合计的价值和品质基数。`1000 parts = 1 ml`。已有液体的元数据与本批新增元数据按框架组合规则合并。

动态输出可设置 `ResolveContents`，返回 `IReadOnlyList<MachineLiquidPart>`，覆盖固定 `Contents`。其 `Parts` 使用原生数量单位，整份返回值表示整批产出，最多32项；不得返回 null。总液量必须为正，液体 ID、数值及合计容量必须有效。

`Condition(GameItem)` 只读检查实际输出容器；`ResolveContents` 在本次配方求值中执行一次。所有解析器和条件不得移动物品、改标签、扣电、切换场景或读档。框架审计已观察资源，但无法替违规回调恢复任意其他游戏状态。

为保留原有 `MachineRecipe` 构造签名和目标类型推断写法，`MachineContainerOutput` 继承 `MachineItemOutput`。只使用 `Contents/ResolveContents/Condition/Guide`；继承的 `ItemId` 必须为空，`Count` 必须为1，`ResolveCount/ResolveItemId/PrepareItem` 必须为 null，否则声明拒绝。

## 匹配、执行与补偿

- 模板输出种类必须与配方一致。输出槽为空、容器条件不满足或容量不足时，本条配方不匹配；不扣材料或电量，可以继续寻找后续满足条件的配方。
- 标准事务检查容器所有权、父库存、实例、原生包装指针和状态字典；冻结完整机器图后暂存材料、扣输入液体及电量，向原有容器加液，逐成分读回，再确认提交。
- 写入失败时恢复原容器的全部标签及价值/品质元数据、输入液体、电量和材料。补偿失败报告 `RecoveryFailed`，不得自动重试。提交后通知异常记录 `CompletionError`，不能因此退款或补发。
- `Transaction.Created` 和 `AfterCommitted` 的新物品列表均为空；成功由 `Committed` 判断。`AfterCommitted` 仅在确认提交后尝试一次。
- 自定义 `Transaction` 负责真正写入和恢复；框架仍独立核对输入、电量、目标液体及身份。`MachineRecipePlan.OutputContainer/OutputContainerBefore/OutputContainerAfter` 是本次同步调用的借用信息，不可跨帧缓存原生引用。

自动图鉴显示“液体（加入已有容器）”。固定液体可取首个液体 ID 作插图引用；纯动态产出需提供 `Guide.IllustrationItemId` 或关闭该配方自动图鉴。生成图鉴不执行动态解析器。配方级 `Condition` 已支持；0.1.56也支持模板级 `ContainerCondition`，详见[machine-container-conditions.zh-CN.md](machine-container-conditions.zh-CN.md)。

## 依据与验收范围

本地官方指南第554行说明物品数据使用 `GameItem.ModifyTag/GetTagReadonly`，没有专门液体处理章节。官方 `PurpleBloodSynthesizer` 示例可参考布局、电力和模块，但其产物是物品袋。`WaterHelper.AddLiquid(GameItem,string,int):void` 来自当前游戏公开程序集；EZapi 的快照、冻结、身份审计和补偿规则为独立实现。当前标准加液要求已核对构建050REV5-L及完整签名；不匹配时停用，详见 `official-sources.index.json`。

0.1.55 已通过八个原生临时场景：空容器、已有混合液、动态输出、容量不足、无容器、条件拒绝、提交核验失败恢复，以及第一成分加入成功后第二成分失败的完整补偿。五组批量检查合计485通过、0失败，全部536份ES3哈希未变。证据见 `verification/liquid-output-native-0.1.55.json`。

这些证据覆盖同进程同步事务，不代表新输出已通过真实夜间保存/新进程恢复、崩溃恢复或其他 Mod 组合验收。含子物品材料的默认消费仍写前拒绝。可执行配方目前使用 C#，现有 JSON 布局与结果输出不能作为配方导入器。

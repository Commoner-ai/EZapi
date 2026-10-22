# EZapi 0.1.54：标准材料与指定成分消费

先读本页，再读配方指南。本版保留自定义Transaction，并允许省略它：框架会创建真正的同步资源事务。旧版文档中“Transaction必填”仅适用于0.1.24。

```csharp
MachineRecipeApi.RegisterRecipes("author.tools", machineId, new[] {
    new MachineRecipe("author.tools.recipe", new[] {
        new MachineIngredient(materialId, 2)
    }, new MachineItemOutput(productId))
}, new MachinePowerRule(7));
// machine须是已注册、玩家拥有、单件且有EZapi布局的机器。
var result = MachineRecipeApi.ProcessBatch(machine);
// 只有result.Committed表示完成；RecoveryFailed与Committed均不能自动重试。
```

没有自动注册机器、跨夜加工或保存。可编译示例见`../src/EZapi.Sample/MachineRecipeExamples.cs`的RegisterStandard。

默认事务按以下顺序执行：只读预检→创建并配置新产物→规划并复核落点→冻结机器完整图及新产物→通过原生TryAcceptOnce拆堆/移动材料到独立保管库存→按声明比例或指定成分扣液→扣布局电池的电量→逐件原生交付→读回核验→逐件Expel/Destroy已确认消费的材料→释放冻结→AfterCommitted一次。

写入失败先将新产物移回游离态，再恢复液体/电池的逐标签深备份，原生退回材料，并由外层独立核对原库存、数量、坐标、价值、液体和电量。整堆保持原实例；部分堆原生合回，合堆可能销毁拆出的实例，之后不能再读该包装对象。未能退回的材料保留在保管图并报告RecoveryFailed，不能销毁它来冒充恢复。该日记不跨帧，不承诺进程崩溃后的持久恢复。

冻结许可是内部实现，不对内容Mod公开。IsFrozen始终反映真实冻结。许可只允许本次精确源实例、目的库存、槽位和数量各接纳/移出一次；同组其他物品及外部组仍被阻止。回调不能借许可搬走其他材料或电池。Dispose、异常、场景、周目变更清除许可；标准事务最终释放冻结。

默认事务支持普通单件和原生可堆叠材料；不会替材料扩容、改堆叠规则或直接改unitCount消费。含内装物材料默认写前拒绝；0.1.57可显式设置MaterialContents=ConsumeGraph，用标准事务消费整根及全部内部物品，失败恢复原实例。详见[machine-material-graph.zh-CN.md](machine-material-graph.zh-CN.md)。JSON物品注册仍只支持单件，调试堆叠工厂不是JSON堆叠支持。

LiquidInputs省略LiquidId时按原生WaterHelper.Remove比例扣算；填写LiquidId时，标准事务通过官方GameItem.ModifyTag只扣对应成分，再调用当前构建的OnLiquidContainerPourOutOf后处理。两条需求按声明顺序从剩余液体计算，保留精确价值/品质元数据；未知构建或完整签名不符在创建产物前拒绝。原生parts与ml相差1000倍；AddLiquid负数不是扣液入口。无需为指定成分单独编写Transaction。

Output.Contents默认自动填入每件新容器，先检查容量和真实液体ID，再用EmptyContainer/AddLiquid正数填充并读回，随后调用作者PrepareItem。作者修改结果必须仍符合声明。自定义Transaction分支保持原来的PrepareItem填充契约。框架不自动设置产物价格或模块工作成长；在PrepareItem中显式调用ProductionValueApi.SetBasis等公开API。

提交确认之后的材料销毁或成功通知异常记录为CompletionError，仍Committed，禁止退款或重试。AfterCommitted仍只尝试一次。检查result.Error、result.Transaction.Error及CompletionError，不能只看是否生成了新产品。

调试面板“材料保管检查”中心304,408，合并整堆、拆堆、退回、默认消费、输出拒绝、部分交付、材料阶段失败、空材料、通知异常和液体内容物检查。全部仅临时图，不交付玩家或保存。测试脚本`verification/Invoke-EZapiNativeTest.ps1`复用PID/焦点/1920×1080门控，Close等待进程真正退出后才能安装下一版。

统一机器注册、追加配方、自动文字图鉴、共享终端、同步外电和夜间调度已有各自API及验收范围。0.1.55已支持[已有容器液体输出](machine-liquid-output.zh-CN.md)。0.1.57已补齐显式含内装物材料消费；外电跨夜持久恢复、配方JSON导入及完整Forge覆盖仍待完成。API/源码/错误索引与结果JSON schema均在docs和schemas；结果JSON不是配方导入器。

## 指定成分最小用法与验收

```csharp
// 机器须已有water液体槽；liquidId取当前游戏真实液体目录中的ID。
var recipe = new MachineRecipe("author.tools.recipe",
    new[] { new MachineIngredient("author.tools.material", 2) },
    new MachineItemOutput("author.tools.product")) {
    LiquidInputs = new[] { new MachineLiquidIngredient("water", 1, liquidId) }
}; // 不写Transaction，由框架消费指定成分1ml。
```

可编译示例为`MachineRecipeExamples.RegisterComponentLiquid`。0.1.54在槽534同一进程通过156项材料断言：混合液保留其他成分、同成分连续扣取、先指定再混合、扣空、交付拒绝和部分交付失败补偿。完整容器/状态字典/父库存身份保持，失败后液体全部标签、材料和电量恢复；冻结均释放。五组合计403/0，全部536份ES3哈希不变。证据见`verification/component-liquid-native-0.1.54.json`。这里只验证同步事务，不承诺跨进程崩溃恢复或其他Mod组合。

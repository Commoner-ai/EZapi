> 0.1.63：机器运行时、手动加工与夜间开关分别查询；原生867/0，旧档读取19/0。参见[机器状态](machine-status.zh-CN.md)和[AI索引](machine-status.index.json)。完整覆盖仍34/39组。

> 0.1.62：复杂材料图、液体物品/容器产出、普通电池及外电已补充两晚真实保存和冷启动验证；下方历史边界以本条限定的矩阵更新，完整范围见[跨夜指南](machine-persistence.zh-CN.md)。不包含原版熔炉所有材料策略或崩溃恢复。

> 0.1.61：图鉴支持可选LocalizedItemText，游戏语言切换自动刷新；旧固定文本保留。见[中英指南](recipe-guide-localization.zh-CN.md)。

> 0.1.57：统一注册中的MachineRecipe可声明MaterialContents=ConsumeGraph，由标准事务处理含内部物品材料。见[machine-material-graph.zh-CN.md](machine-material-graph.zh-CN.md)。

# EZapi 统一机器注册（0.1.27）

一个 `MachineRegistrationApi.RegisterMachine` 声明机器物品工厂、独立原生布局、可执行配方、输入过滤和自动图鉴。代码只依赖 EZapi 和当前游戏程序集。完整可编译示例见 `../src/EZapi.Sample/MachineRegistrationExamples.cs`；标准材料消费见 [machine-materials.zh-CN.md](machine-materials.zh-CN.md)。

## AI 应按这个顺序开发

1. 确认材料和产物的稳定 ID。普通物品使用现有 JSON/API 注册，不能只写图鉴来创建物品。
2. 在自己 Mod 的 `OnInitializeMelon` 中调用 `RegisterMachine`，在游戏物品目录出现前完成。`MachineId` 和基础配方 ID 均以作者完整 owner 加点号开头。
3. `Template` 复用 `MachineLayoutDefinition`。内部网格和机器外部占格分别配置；默认外部为 2×2。`ConfigureItem` 只修改本次新物品的外形、数值和外观；布局由框架随后建立。
4. 读取 `ContentSubmissionResult.Accepted/Reason`。整批预检失败时不会加入此机器工厂、配方或图鉴。`Staged` 仅表示声明接收，`Applied` 仅表示原生目录接受；工厂执行计数和失败另行查询。
5. 进入周目后，针对拥有 EZapi 布局且属于玩家的单件机器，显式调用 `MachineRecipeApi.ProcessBatch(machine)`。不要每帧调用，也不要在失败后循环补发。
6. 交易、消费、产物和失败恢复由既有标准事务执行；`Transaction` 非空时使用作者自定义事务。需要真实加工示例请复制 Sample，不要写永远返回 true 的空事务。

```csharp
var result = MachineRegistrationApi.RegisterMachine("author.mod", new(
    "author.mod.press", new() { ItemInput = new(4,4) }, new[] {
        new MachineRecipe("author.mod.press_recipe",
            new[] { new MachineIngredient("author.mod.material", 2) },
            new("author.mod.product"))
    }) { Power = new(0), ItemOptions = new("简易压制机") });
if (!result.Accepted) throw new InvalidOperationException(result.Reason);
```

## 公共入口与生命周期

| 入口 | 时机和结果 |
| --- | --- |
| `RegisterMachine(owner, definition)` | 初始化主线程、目录出现前；同一个机器/物品/既有低层配方目录 ID 只能声明一次 |
| `RegisterAdditionalRecipes(owner, machineId, recipes)` | 主线程空闲阶段整批追加；目标必须是统一注册的机器；不覆盖、不删除；动态准入即时读取新目录 |
| `RegisteredRecipes(machineId)` | 复制的配方与真实 `OwnerId`；保留追加顺序 |
| `Recipes(machineId)` | 复制的配方定义；动态委托仍是作者声明，读取快照不会执行它们 |
| `Snapshot()` | 复制的纯托管机器视图：owner/模板/配方数/目录状态/加工可用性/工厂计数与错误；可用 JSON 序列化 |
| `GuideSnapshot()` | 仅包含统一注册生成的自动图鉴；手写图鉴使用 `RecipeGuideApi.Snapshot()` 查询 |

全框架关闭时释放自动准入句柄和此注册层的配方/图鉴分区；旧工厂代际失效。注册工厂不支持热替换或卸载后重新复用旧原生目录，更新 DLL 应退出并重新启动游戏。物品和库存引用只能在当前同步调用内使用，读档后重新取得实例。

输入过滤保留原生拒绝，只允许合并目录的材料或 `AuxiliaryItemIds`。它不会自动消费辅助物品。配方按基础声明、之后追加的顺序选择；先匹配的配方尝试失败不会自动改选另一条。追加作者不能调用低层 `RegisterRecipes` 抹掉整个合并目录。

## 自动图鉴与动态配方

`MachineDefinition.Guide` 默认 `new RecipeGuideOptions("synthesis")`。`MachineRecipe.Guide` 可以覆盖分类和说明；两者都为 null 时不生成该配方图鉴。手写图鉴与自动图鉴分别存放：手写 `RegisterRecipes(owner, [])` 不会删除自动图鉴。两者的同作者同配方 ID 冲突时拒绝此次提交，保留原状态。

材料、液体投入和物品产出的 `Guide` 使用 `RecipeGuideDisplay`：`IllustrationItemId/Name/Requirement/Quantity` 均为可选固定文本覆盖。仅按标签匹配的材料以及无指定成分的混合液体，必须提供 `IllustrationItemId`，或停用该配方的自动图鉴。数量单位为材料件数、液体 ml；运行中的液体物理数量仍是 1000 parts=1 ml。

图鉴声明构建不会创建物品或查询工厂名称；显示图片时会按0.1.58缓存协议有限创建游离临时物品，见[图片指南](recipe-guide-images.zh-CN.md)。声明构建不会执行 `Condition/ResolveCount/ResolveItemId/ResolveMillilitres`。动态字段会显示“动态数量/产物”等说明，作者应提供更清楚的 `Quantity/Requirement`。静态显示不代表已计算出下一批真实产量。0.1.61已提供可选中英文本，见[中英图鉴指南](recipe-guide-localization.zh-CN.md)。

## 限制与兼容边界

- 最多 128 台统一机器、每台最多 64 条合并配方，整个可执行目录最多 2048 条；图鉴每作者最多 512 条、总计最多 2048 条。自动准入还受全局准入句柄数量限制。
- 支持items网格物品输出及container已有容器液体输出，配方输出种类必须与模板一致。见[machine-liquid-output.zh-CN.md](machine-liquid-output.zh-CN.md)。液体投入必须引用模板中存在的槽。模板级ContainerCondition及液体输入槽Condition已在0.1.56接入，统一注册自动安装；与配方MachineContainerOutput.Condition取交集。见[machine-container-conditions.zh-CN.md](machine-container-conditions.zh-CN.md)。
- 显式正电耗或动态电耗需要模板的电池槽；同步外电已接入，跨夜持久恢复仍待完成。`Power=null` 保留已有原生电耗解析规则，`Power=new(0)` 明确无电耗。
- `ProductionMarkupPercent` 是批次上下文参数；作者仍须在 `PrepareItem` 显式计算并设置产物价值。不会自动涨价或增加模块成长。
- `CreateItem` 可选；如提供，每次必须返回 ID 匹配、单件、游离、尚无窗口的新实例，创建失败前自行清理。框架只清理已确认属于此次工厂的游离实例。
- 机器定义、回调和真实配方目前使用 C#。原有布局 JSON 与图鉴 JSON 仍可独立使用；诊断/视图 JSON 是输出，不能作为可执行机器定义导入。
- 本版已有夜间调度、`ResolveNightlyBatchCount`、BeforeBatch/AfterBatch 订阅，见[自动化指南](machine-automation.zh-CN.md)。默认每晚一批，纯手动机器明确关闭EnableNightly。共享制造终端及其加价、同步外电已有独立API。NightlyInstalled报告实际就绪状态。完整Forge目标继续。

## 调试

只加载 `EZapi.dll`，载入测试档，打开左上角 EZapi 调试，点击“机器注册检查”。该按钮创建临时机器与材料，验证注册前/载入后两种追加、实际消费、产物交付和图鉴归属，结束后清理；不向玩家交付测试物品或请求保存。诊断在 `UserData/EZapi/diagnostics.json` 的 `machineRegistrationCatalog/machineRegistrationDebug`，错误按 `errors.index.json` 查询。

真实游戏快照输出示例：[machine-registration-view.json](examples/machine-registration-view.json)。采用默认PascalCase属性、数值枚举Status；Staged=0、Applied=1、Conflict=2、Failed=3。文件只是纯托管机器状态输出，不能导入执行。

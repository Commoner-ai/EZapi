> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

> 0.1.61：图鉴支持可选LocalizedItemText，游戏语言切换自动刷新；旧固定文本保留。见[中英指南](recipe-guide-localization.zh-CN.md)。

> 0.1.56：输入槽Condition/输出ContainerCondition仅C#使用。统一注册自动安装，低层Create前先Register并持有条件句柄；JSON不接受回调字段。详见[machine-container-conditions.zh-CN.md](machine-container-conditions.zh-CN.md)。

# EZapi机器布局与配方图鉴

0.1.72 新增机器窗口/槽位中英文声明及显示刷新，字段、Schema 和验证边界见 [机器布局中英指南](machine-layout-localization.zh-CN.md)。旧固定标题/槽标签保持原样。

本页是后续机器加工的基础层。它已经提供独立原生窗口、材料网格、物品或液体容器产物槽、多个液体输入槽、电池、模块和说明书槽，以及可打开的只读图鉴。**布局API本身只建立窗口；实际加工使用现有MachineRecipeApi/MachineRuntimeApi，统一注册和夜间调度见各自指南。任意原版机器配方追加不在本层范围。** 图鉴展示一条配方不代表游戏已经能制作它。

目标为当前 Probably Stolen Demo 050rev5-L，Assembly-CSharp SHA256 `E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF`。不依赖、加载或转发 Forge。完整 Forge 覆盖目标保持进行中。

## 给 AI 的实施顺序

1. 先读 [能力清单](capabilities.json)，确认需求属于布局/图鉴还是生产运行时。真实加工入口包括 `MachineRecipeApi.ProcessBatch`，契约见machine-materials.zh-CN.md与machine-liquid-output.zh-CN.md。
2. 制作机器物品时沿用 [独立工厂指南](native-items.zh-CN.md)。在工厂返回新物品前调用 `MachineLayoutApi.Create(machine, definition)`；外部形状、图像、价值仍由原物品工厂设置。每次工厂调用均重新建立布局，包括正常读档时目录重建。
3. 调用失败时，工厂负责销毁本次明确创建且尚未交付的整个物品。Create 已可能建立部分原生窗口；不能在同一半成品上盲目重试，不销毁玩家库存对象。
4. 需要读取槽位时，重新调用 `TryGetInventory`。返回的原生对象只在当前同步场景使用；换场景、重新读档后重新获取，不缓存包装对象。
5. 图鉴通过 `RecipeGuideApi.RegisterRecipes` 或 `RegisterJson` 登记。条目固定文本，数量是展示信息；真实材料核对和产出事务属于另一个运行层。
6. 编译示例见 [MachineGuideExamples.cs](../src/EZapi.Sample/MachineGuideExamples.cs)。完整独立 JSON 见 [machine-layout.json](examples/machine-layout.json) 和 [recipe-guide.json](examples/recipe-guide.json)。这些文件不会由 EZapiPacks 自动导入，也不能塞进 items.json；内容 DLL 要显式解析并调用对应入口。

## 最小调用

```csharp
// 放在你已有的物品目录工厂内，返回freshMachine之前。
var slots = MachineLayoutApi.Create(freshMachine, new MachineLayoutDefinition {
    ItemInput = new(4, 4),
    Output = new() { Kind = "items", Size = new(4, 4) },
    Battery = new(new(2, 2)),
    Modules = new() { Size = new(4, 4), AllowedTypes = new[] { "MODULE_TYPE_UNIVERSAL" } }
});
```

布局本身不会初始化机器工作周期。`MachineryHelper.InitMachinery`、`InitProgressTypeMachinery` 等官方入口各有不同语义，内容代码应在明确需要时选择；不要为显示窗口而偷偷添加生产调度。自0.1.59起，`MachineBatteryTemplate.Required` 会进入 `MachineInventory.RequiresBattery` 并由加工器执行：true时零电耗也需要有效电源，false时仅零电耗允许空槽；布局创建本身不扣电。见[必需电源指南](machine-required-power.zh-CN.md)。

```csharp
using var layoutFile = File.OpenRead("machine-layout.json");
var definition = JsonMachineLayout.Parse(layoutFile);
// 在工厂内对本次新物品调用 Create；不要把同一原生物品复用给多个工厂请求。

using var guideFile = File.OpenRead("recipe-guide.json");
var submitted = RecipeGuideApi.RegisterJson("author.tools", guideFile);
if (!submitted.Accepted) throw new InvalidOperationException(submitted.Reason);
// 店铺就绪、没有正在拖拽的物品时，交给你的按钮调用：
bool shown = RecipeGuideApi.Open();
```

## 布局与准入

内部格数与机器外部占格独立；每个网格宽高各1..12，液体输入最多8个。`GameSlotInventory(int,int,...)` 的两个整数是格数，不能乘像素尺寸。原生2×2根布局为：

| 坐标 | 内容 |
| --- | --- |
| (0,0) | 材料网格 |
| (1,0) | 产物、液体输入、说明书的竖列 |
| (0,1) | 可选电池槽；官方 GetBatterySlot 路由 |
| (1,1) | 可选模块网格；官方 GetModuleInv 路由 |

电池、模块和说明书分别调用官方 `SetupBatterySlot`、`SetupModuleBay`、`SetupManualSlot`。模块类型原样传入官方 helper，默认只有 `MODULE_TYPE_UNIVERSAL`；空列表不承诺任意模块准入。模块改动沿用原生回调与现有 ModuleApi。

液体输入和 `output.kind="container"` 只接纳真实原生单件液体容器；输入还可以选择 `allowedIds` 或 `allowedTags`，两类不可同时非空。输入标签是 OR 候选；必须满足原生液体资格及所选过滤。材料网格和普通产物网格暂不根据可执行配方过滤，因为本版尚无该加工层。原生拥有权、空间和辅助 helper 的拒绝始终保留。

实例回调使用已有 ClassInjector 接收器和游戏 Delegate.CreateDelegate，不使用核心 Delegate/DynamicInvoke 补丁。关闭框架后布局自有过滤惰性；不清空玩家窗口或其它 Mod 规则。

## 保存边界

稳定子库存ID为 `<machineId>.ezapi.machine.input/output/battery/modules/manual/liquid.<slotId>`。`EZAPI_MACHINE_LAYOUT_V1` 保存布局JSON；可选null字段会省略，`JsonMachineLayout.Serialize` 输出可通过严格 Parse。完整UTF-8布局不得超过65536字节。不同实例分别持有窗口和库存，不共享可变原生对象。

`TryGetInventory` 核对精确标记、实际槽种类/尺寸、父物品及独立指针。普通物品返回false；损坏、缺槽或不匹配时抛错，读取不迁移、不创建修复槽、不丢弃物品。已经保存的布局若发生改变，应显式设计迁移；不能以重建窗口覆盖旧库存。

本版已验证临时机器图的官方编码，但**没有验证此布局的正常保存后重载**。现有 SaveGraph.Decode 仍因当前原生水培恢复入口缺失而预检拒绝，不应以图编码成功推断解码或持久化成功。

## 图鉴契约

每次 RegisterRecipes 对该owner整组替换，空组只移除该owner。每owner最多512条，全局最多2048条；重复ID、错误owner前缀、坏字段或超额会保留原目录和revision。输入集合和输出在接收时拷贝，Snapshot排序稳定、只读，旧快照不会被后续替换修改。初始化线程调用，后台线程拒绝。

图鉴最多64个投入图示，每项数量1..1000000000。`Name` 为固定名称；`Requirement` 为条件说明；`Quantity` 可显示“500ml”“整堆”等自定义数量语义。0.1.58按ItemId自动显示图片，自动机器配方条目见机器注册指南。中英类型化文本已在0.1.61接入；图片边界见[图片指南](recipe-guide-images.zh-CN.md)。

Open 在当前店铺且无拖拽物品时可用；无可用周目或当前未启用时返回false。Close、Esc、换场景、重新读档或离开店铺会释放图鉴和透明输入遮挡。正文、投入和配方均可分页；图片预热会有限创建游离临时目录物品；不主动写玩家库存或保存。第三方工厂应自行避免副作用。界面只在目录revision变化时重取快照。

## 离线校验与实机检查

```powershell
dotnet tools/ezapi-validate/ezapi-validate.dll --machine-layout docs/examples/machine-layout.json
dotnet tools/ezapi-validate/ezapi-validate.dll --recipe-guide author.tools docs/examples/recipe-guide.json
```

CLI与运行时共用校验，未知/重复字段、显式null、嵌套错误和超限均拒绝。JSON Schema用于编辑器提示；owner前缀、液体槽唯一性、不可见控制字符与完整字节上限仍以CLI为准。

调试面板新增“机器槽位检查”“打开配方图鉴”。前者一次验证两个真实自定义目录工厂、槽位准入和完整子图编码，结束销毁临时物品；后者验证实际界面、材料/正文分页和关闭后的输入。结果字段为 `machineLayoutDebug`、`machineLayouts`、`recipeGuide`、`recipeGuideCatalog`。本阶段实机证据见 [版本验收记录](machine-layouts-guide-0.1.21.zh-CN.md)。

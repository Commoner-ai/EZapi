# Presentation API：名称、来源和动态 tooltip

`PresentationApi` 为 DLL Mod 提供物品登记文本查询、owner 展示名、模块运行时附加文字和自有模块效果的双语文本。注册在 EZapi 初始化主线程完成；所有文本回调都在游戏官方 tooltip 回调期间同步执行。EZapi 不调用 `RichTextBuilder.Build`，也不保留 tooltip builder 或 `GameItem` 引用。

## 注册顺序

先登记物品或效果，再登记其 presentation。物品 ID 必须以 owner ID 加点号开头；`RegisterModuleText` 要求该 ID 已声明为这个 owner 的 JSON/DLL 模块。`RegisterEffectText` 要求效果先由相同 owner 通过 `EffectApi.RegisterEffect(s)` 声明。重复注册、跨 owner 引用、无效文本或错过初始化窗口会返回 `ContentSubmissionResult`，查看 `Accepted` 和 `Reason`。

```csharp
const string owner = "author.tools";
const string moduleId = "author.tools.sensor";

var ownerText = PresentationApi.RegisterOwnerDisplayName(owner, "工具示例", "Tools example");
var item = ItemApi.RegisterModule(owner, moduleId, CreateSensor, new NativeItemOptions(
    LocalizedName: new("检测模块", "Sensor module"),
    LocalizedShortDescription: new("读取设备状态", "Reads device status")));

if (ownerText.Accepted && item.Accepted)
{
    var moduleText = PresentationApi.RegisterModuleText(owner, moduleId, (instance, english) =>
        new ModuleText(
            AdditionalEffect: english ? "Reads the current device state" : "读取当前设备状态",
            Lines: new[] { (english ? "Item ID: " : "物品 ID：") + instance.identifier }));
}
```

示例的 `CreateSensor` 仍需按官方 `ModuleHelper.InitModuleItem` 创建一个新的独立原生模块。不要在工厂外缓存实例。若同一 owner 使用 JSON 内容包，`RegisterModuleText` 也可引用已登记的 JSON `module`。

## API 语义

| 方法 | 行为 |
| --- | --- |
| `RegisterOwnerDisplayName(ownerId, chinese, english)` | 注册 owner 的双语来源名。物品没有自定义 `SourceName` 时，Late tooltip 来源行优先显示此名称；再回退内容包名称或 owner ID。 |
| `GetRegisteredItemName(itemId, english = false)` | 读取 EZapi 已声明的 JSON/DLL 物品文本，不创建物品。`english=true` 选择英文；缺失的语言回退另一侧。未知 ID 返回 `null`。未提供 DLL 名称时返回其稳定 ID。 |
| `GetRegisteredItemDescription(itemId, english = false)` | 读取登记的短简介；JSON 按 `description`、`shortDescription`、`longDescription` 回退。没有登记说明或 ID 未知时返回 `null`。 |
| `RegisterModuleText(ownerId, itemId, callback)` | 在官方 Late tooltip 回调中调用 callback，追加 `AdditionalEffect` 和 `Lines`。只针对该 owner 已应用的模块和实际匹配的实例 ID。 |
| `RegisterEffectText(ownerId, effectId, name, description)` | 为相同 owner 通过 `EffectApi` 登记的效果提供中英名称/说明。语言变化时更新该确切效果对象；停用时恢复仍由 EZapi 控制的字段。 |

`LocalizedItemText` 的 `Chinese` 用于 `zh*` locale，`English` 用于其它 locale；一侧为 `null` 时回退另一侧，空字符串表示有意清空。效果名至少要提供一种非空翻译。描述可省略；被省略的字段保留效果工厂原文。`GetRegisteredItemName/Description` 的 `english` 参数是显式查询选项，与游戏当前语言无关。

模块回调每次 tooltip 都会重新执行，因此只应读取传入实例并返回短文本，不要修改实例、库存或效果，不要启动任务，也不要保存实例。异常、超过 128 行、行长超过 2048 字符或附加效果文字超过 4000 字符会跳过本次输出并记录一次诊断；错误不会穿过官方 IL2CPP tooltip 回调。

效果本地化只写 EZapi 仍在效果目录中登记的同 owner 对象。若其它 Mod 替换同 ID 对象，EZapi 不会改写替代对象；若其它代码改动了 EZapi 效果的文字字段，EZapi 会放弃对该字段的后续写入，也不会在停用时覆盖外部值。

## 接口核对与验收边界

当前游戏程序集元数据确认 `Il2Cpp.ModuleEffectHelper.ModuleEffect.displayName` 和 `description` 为可写属性；tooltip 继续使用官方 `ModHook.OnCreateTooltipEarly/Late`。语言选择复用已精确构建门控的 `LocHelper.GetCurrentLocaleCode()`。这些签名证据只证明接口可用，不证明游戏内文字已正确显示。

离线 Presentation 专项覆盖名称查询、双语回退、owner 与模块归属、回调输入/输出边界、效果对象替换、外部字段保护和停用恢复。实际模块 tooltip 顺序、语言切换后的原生效果 UI 以及其它 Mod 并发改写，需要在只加载 EZapi 的可丢弃测试档中验收；本版未启动游戏。

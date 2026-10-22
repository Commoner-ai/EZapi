> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# RuntimeCapabilities：运行时能力快照

`FrameworkInfo.GetCapabilities()` 是编译期能力清单：它说明 EZapi 声明了哪些入口。`RuntimeCapabilities.Current` 则在读取时重新检查 EZapi 自身的运行门控，不保留 Unity/IL2CPP 对象，也不把编译清单当成运行就绪状态。

```csharp
RuntimeCapabilitySnapshot state = RuntimeCapabilities.Current;

if (!state.KnownGameBuild)
{
    // 当前 Assembly-CSharp 未匹配已核对的游戏构建；谨慎处理版本敏感功能。
}

if (state.InventoryTransfer)
{
    // 仅代表框架的搬运入口门控已就绪；仍须调用具体 API 并检查它的结果。
}
```

## 调用规则

- 在游戏主线程读取。非游戏线程返回所有字段均为 `false` 的快照，不触碰原生对象。
- 每次读取都会重新查询当前门控；调用者可以保留托管快照用于日志，但之后的决策应重新读取。
- `KnownGameBuild` 按当前 `Assembly-CSharp.dll` SHA256 精确匹配 `E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF`。无法读取或摘要未知时为 `false`。它是版本识别信息，不表示任何玩法已通过测试，也不会替代各 API 自己的就绪检查。
- 其他 `false` 表示该功能尚未实现，或当前运行门控/所需对象没有就绪。快照不提供失败原因；需要定位时查询对应 API 的 `GetRuntimeStatus()`、`IsAvailable` 或操作结果。
- `true` 只表示对应入口目前允许尝试，不保证游戏原生调用成功、其他 Mod 未拦截、数据已经写入或存档完成。执行状态改变前后都检查具体 API 结果。
- 快照是 EZapi 自己的诊断接口，不是官方游戏 API。官方指南定义了原生钩子；本接口读取 EZapi 对这些钩子的订阅和各功能就绪门控。

## 字段映射

| 字段 | 当前读取含义 |
| --- | --- |
| `KnownGameBuild` | `Assembly-CSharp.dll` 与已记录的构建摘要完全一致 |
| `NativeItemDirectoryHook` | 官方物品目录及 tooltip 委托订阅已建立 |
| `NativeModuleDirectoryHook` | EZapi 模块目录回调已处理，且当前处于游戏主线程 |
| `NativeAmenityDirectoryHook` | 当前未实现，固定为 `false` |
| `NightShopStock` | 物品夜间补货运行门控是否就绪 |
| `RunDataStaging` | 当前周目身份可由 EZapi 解析，周目数据暂存入口可用 |
| `DirectInventoryRead` | 当前周目可解析，直接库存读取允许尝试 |
| `WholeTransferPreview` | 当前周目可解析，整次搬运预览允许尝试 |
| `NativeEffectRegistration` | 至少一个效果已在官方目录应用，且登记结果可读回 |
| `InventoryTransfer` | 当前周目可解析，并且库存搬运所需目录/冻结/准入/列表门控就绪 |
| `MachineNightProcessing` | 机器配方、生命周期、库存准入和冻结门控都已安装 |
| `NpcTradeStock` | NPC 货源运行门控是否就绪 |
| `DossierExpansion` | 当前未实现，固定为 `false` |
| `PersistentInventoryEndpoints` | 端点钩子、目录、存档图恢复能力和当前主线程条件均就绪 |
| `ExternalPowerTransactions` | 外电服务已启动，且本机构建的运行时钩子核验通过 |
| `BranchingDialogue` | 对话 API 当前主线程可用，必需钩子就绪 |
| `ClientVisits` | 当前构建指纹、官方 `OnGenerateCustomerVeryLate` 订阅和对话运行门控全部就绪时为 `true`；不表示顾客已到访或对话已通过游戏内验收 |
| `ReadingWindow` | 阅读窗口 API 当前主线程可用，必需钩子就绪 |
| `PhoneContacts` | 电话联系人 API 当前主线程可用，必需钩子就绪 |
| `SupplierVisits` | 与 `ClientVisits` 共用已知构建、官方 `OnGenerateCustomerVeryLate` 钩子和 EZapi 对话门控；由 `SupplierApi.IsAvailable` 实时读取 |

`SupplierVisits` 为 `true` 表示当前构建指纹、官方 `OnGenerateCustomerVeryLate` 订阅及 EZapi 对话运行门控均已就绪。它不表示供应商已入队/到访、报价可购买或商品已交付；请检查 `SupplierApi.Queue/Status` 的结果。

运行时钩子规则遵照官方 `ModHook` 的订阅/取消约定及物品目录时机；实现所用官方指南、API 变更和 LearnerMod 示例的 SHA256 见 [`official-sources.index.json`](official-sources.index.json)，当前程序集指纹也记录在那里。官方文档没有定义 EZapi 的能力快照；本 API 的字段语义以 `RuntimeCapabilities.cs` 中调用的 EZapi 门控为准。

能力快照会写入现有 `UserData/EZapi/diagnostics.json`。请在游戏内点“导出诊断”后检查 `runtimeCapabilities`；初始化早期导出的记录可能在当前游戏主线程/目录尚未就绪时显示 `false`。

## 验证边界

离线测试覆盖线程失败关闭、周目门控、各项就绪映射、异常隔离、快照刷新、构建摘要匹配和未实现能力保持关闭。它们不证明原生物品、交易、机器、存档或 UI 行为已通过游戏内验证。具体功能仍须检查调用结果，并在目标游戏构建中单独验收。

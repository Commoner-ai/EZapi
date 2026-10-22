# 官方文档与当前 EZapi 入口

优先参考 [官方模组指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md)、[API 变更](https://github.com/dragonpan2/probably-stolen-modding/blob/main/APIChanges.zh-CN.md)和实际游戏签名。本地 skill 只是参考。当前适配基线为 Demo050REV5-L；官方内置加载器的 IMod 入口与本项目 MelonLoader 的 MelonMod 入口分别使用。

| 任务 | EZapi 入口 | 当前指南 |
| --- | --- | --- |
| 官方目录与工厂 | ItemRegistry / ItemApi / RegistrationApi | [物品](native-items.zh-CN.md)、[声明注册](registration.zh-CN.md) |
| Tooltip、名称、分类与来源 | ItemDefinition / PresentationApi | [表现](presentation.zh-CN.md) |
| 游戏语言与 CSV | GameLanguage / Localization | [语言](game-language.zh-CN.md)、[CSV](localization.zh-CN.md) |
| 官方生命周期与周目数据 | GameEvents / LifecycleApi / RunData | [阶段](lifecycle-content.zh-CN.md)、[周目记录](run-data-records.zh-CN.md) |
| 库存、物品图与只读存档 | InventoryApi / SaveGraphApi / SaveReadbackApi | [库存](inventory-services.zh-CN.md)、[物品图](save-graph.zh-CN.md)、[回读](save-readback.zh-CN.md) |
| 机器、配方、加工与电源 | MachineRegistrationApi / MachineRecipeApi / ExternalPowerApi | [机器](machine-registration.zh-CN.md)、[配方](machine-recipes.zh-CN.md)、[外电](external-power.zh-CN.md) |
| 对话、电话与供应商 | DialogueApi / PhoneApi / SupplierApi | [对话](dialogue.zh-CN.md)、[电话](phone.zh-CN.md)、[供应商](suppliers.zh-CN.md) |
| 离店地点 UI | DepartureApi | [外出按钮](departure-buttons.zh-CN.md) |

准确定义、重载和失败边界见[当前公开签名](public-api.index.json)、[任务索引](API.index.zh-CN.json)与各指南。表中入口是导航，不替代签名检查，也不表示实机验收通过。历史接口核对和实验保存在[旧映射记录](history/official-api-map.zh-CN.md)。

> 0.1.66：当前原生家居类型为 HOUSEHOLD_GOOD（实际 water_filter_adv 已验证），并保留旧 HOUSEHOLD 别名。随机批次可能只有未分类特殊物品，此时全部保持原样；有合格货位时才参与替换。最终原生 NPC 检查 48/0，九组共 954/0，见 [验证](verification/guide-mesh-native-0.1.66.json)。

# 固定商人货位与机器供应（0.1.64）

沿用现有 C# `NativeItemOptions.NpcTrade` / JSON `npcTrade`，无需再订阅补货事件。先声明并等待目录 Applied。

```json
[
  {
    "id": "author.tools.assembler",
    "name": "装配机",
    "value": 200,
    "npcTrade": {
      "category": "Machine",
      "weight": 2,
      "suppliers": "Inventor",
      "includeInNightShop": true,
      "skipWhenOwned": true,
      "minimumDay": 3
    }
  }
]
```

此例仅演示供货声明；JSON普通物品不会因此自动具有机器布局或加工行为。功能机器用 `MachineRegistrationApi.RegisterMachine` 并将同一 NpcTrade 声明放入 `MachineDefinition.ItemOptions`。内容包要求 minEzapiVersion=0.1.64。

原生发明家夜间/到访商店、小偷、小偷商贩和食物小偷的固定货物，以原物品已经取得的货位为起点，读取实例的官方类型。优先顺序为 MACHINE、MODULE、ORE、FOOD、MEDICAL、MATERIAL、HOUSEHOLD_GOOD、HOUSEHOLD；首个匹配类型决定唯一分类。未匹配的钥匙卡、工具、文档等保持原路径；某些化学品没有这些类型，也不会被猜测成材料。

原物品权重为1，合格自定义候选使用 Weight。例如唯一候选权重2时，每个符合分类的固定货位约有2/3概率换为该物品。保留原生生成货位的天数、解锁、概率与次数，不强行生成未解锁货位。不满足供应商、夜间、最低天数、当前持有等条件时不额外推进RNG。命名掉落表原有的权重算法保持不变。

当前玩家物品身份在商人生成开始时扫描，包括子容器；不能用原生 Owned 标记判断“玩家已持有”，因为新生成的商人货物也会带这个标记。拒绝替换已有图中的实例、已附着物品、已登记的EZapi物品。替代工厂保持原货物的原生拥有标记，实际货架接纳仍交给游戏。

ItemSpawner.Spawn/SpawnFromTable/SpawnFromTableGroup 在同一调用域中记录命名表生成的实例身份，交付时不再次抽取。查询/事件/工厂异常保留已经存在的原货物，不重复抽取或重跑原工厂。官方抽取事件清空 itemId 则取消该临时货位。固定货位合成事件表名为 ezapi.npc.inventor.fixed 或 ezapi.npc.thief.fixed。

被替换的原物品延迟到整个生成方法及官方 Late 回调结束后清理，不能让临时列表访问已销毁对象。调用域同时绑定周目和运行时代次；原生异常通过 finalizer 清理。框架停止后即刻停用路由，在途同步范围仍由 finalizer 完成释放。不修改全局掉落journal、拖放逻辑或玩家存档。

本轮实机发现并修复：原生预制机器会先初始化电源再设置ID。ExternalPowerApi.Find 对尚无ID的实例返回无提供方，让原版初始化正常执行。

验证：71项离线NPC适配检查；原生47项NPC检查包含真实发明家夜间/到访、小偷/小偷商贩/食物小偷和矿工生成回调，实际预制机器、医疗和食品货位替换。九组实机共923/0，旧版复杂机器存档读取19/0。所有货物在原生交付入口被调试夹具截获、读取并销毁；本轮没有执行真实货架接纳、购买或保存，不能据此宣称这些路径验收完毕。

参见 [AI索引](npc-fixed-stock.index.json)、[English](npc-fixed-stock.en.md)、[原有选项](npc-stock.zh-CN.md)。

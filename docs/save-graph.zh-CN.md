# SaveGraph（0.1.53 当前构建适配）

`SaveGraphApi` 是独立物品子图的原生编码/恢复入口，使用当前游戏的 `SaveManager.EncodeNodes/DecodeNodes` 与 `SaveState.saveItems`，再交给 Unity JSON 模块。它不引用 Forge，不调用 `SaveGame`/`InitialSave`，也不会自动把恢复根放入库存。

`Items(root)` 在当前主线程/周目内读取 root 及全部 GameItem 子孙，限制4096物品、8192图节点和边，拒绝空ID、重复实例ID、坏指针和环。`Encode` 会核对原图和原生 SaveItemNode 的实例ID、定义ID、数量、UUID与关系；编码期间周目变化或图变化会抛错。

`Decode(json, existingIds)` 先限制 UTF-8 JSON 不超过8 MiB、深度64、元素数262144、saveItems 1..4096，拒绝重复键、缺定义、自引用、多父、断开图和重复/冲突实例ID。`existingIds`用于调用方提供尚未在当前库存图中的保留ID。原生恢复必须返回未放置root；随后只对新图运行游戏读档辅助函数和 `onLoaded`，最后重新编码核对关系。任何中途不确定都抛 `EZAPI_SAVEGRAPH_INDETERMINATE`，不要盲目重试。

先检查 `SaveGraphApi.CanDecode` 和 `DecodeUnavailableReason`。能力检查确认当前游戏构建指纹与11个精确后处理签名；缺少时 `Decode` 会在调用 `DecodeNodes` 前抛 `EZAPI_SAVEGRAPH_UNSUPPORTED`。未知构建或任一签名缺失时均拒绝；不能跳过检查继续恢复。

接口只保证内存中的原生图恢复。游戏是否在后续夜间保存、换槽、读档后完整保留，必须由玩家用可丢弃存档验证；JSON字符串本身不是已写入存档的证明。恢复图的根和子项仍由调用Mod负责通过原生库存准入放置，调用方必须在失败时保留人工对账。

显式保存形状须为1..64每轴、outside=0、方向0..3、完整矩阵，总计不超过65536格；子库存索引限0..8191。超出当前有界契约时拒绝，不截断、不继续原生恢复。原生加载回调的完整玩法效果仍待当前游戏构建内验证。

编码/解码分别隔离并恢复SaveManager的currentUUID、itemHeap、itemDict、saveJson；这是临时堆保护，不是游戏存档事务。恢复时将当前currentUniqueId只向上推进，失败也不回退ID计数。内容Mod应提供所有离开普通库存图的保留ID；API只能自动观察当前可达物品。


## 当前加载序列与验证范围

0.1.53 按050REV5-L真实 `PlayerStore.DecodeSaveItem` 顺序调用 LoadUpgrade、UpdateWaterContainerSprite、LoadLockItem、LoadEdibleProduce、LoadNonOwnedToolTag、LoadHazardousWasteTag、LoadLockedContainer、LoadBrokenMachine、MachineHelper.OnLoad、LoadTurboBoostEligibleTag、CurrentConverterHelper.OnLoad，然后每个物品的onLoaded一次。当前游戏已删除旧水培恢复方法，不再把旧版本的方法作为本版本前置条件。

精确签名与构建指纹在创建图之前整组核对。对应实现集中在SaveGraphLoader.cs，产品不使用原生地址或字段偏移。该服务不调用PlayerStore.DecodeSaveItem，因为后者还会把图放入指定库存；EZapi只返回游离根给调用者。

50项离线检查通过，覆盖图完整性、冲突、后处理顺序、未知构建和缺签名拒绝、回调异常/重入及清理。当前游戏实机33项覆盖普通物品、多层容器、堆叠材料、带输入机器、液体数量/纯度、实例ID、状态标签、价值、父子关系与原生临时堆还原。普通容器的嵌套必须满足游戏原生NESTABLE_DEVICE_TAG规则，不能绕过准入来构造“成功”结果。实机持久库存端点临时操作14项另行通过。

这些证据证明内存中独立物品图的原生编码与恢复；磁盘保存、换档和新进程重载验收仍由具体持久功能负责，不由JSON字符串或CanDecode=true代替。

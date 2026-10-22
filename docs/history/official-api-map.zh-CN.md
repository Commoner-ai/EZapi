> 历史说明 / Historical notes；不作为当前能力或当前验收结论。[当前状态](../current-status.zh-CN.md)。

# 官方文档与 EZapi 0.1.47 对照

依据：[官方模组指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md)、[API 变更](https://github.com/dragonpan2/probably-stolen-modding/blob/main/APIChanges.zh-CN.md)、官方 LearnerMod 示例，以及本机 Assembly-CSharp 的完整签名。skill 仅为参考。文档说明与实际构建有差异时按官方指南自己的约定，以当前游戏可用接口为准。

本机目标为 Probably Stolen Demo 050rev5-L、MelonLoader IL2CPP、.NET 6。入口保留 MelonMod；官方指南中的 IMod/ModLog 面向内置加载器，不能把那种程序集入口直接当作本机 MelonLoader 入口。当前代码没有伪造 LoadedMod 或重复加载第三方 DLL。

| 功能 | 本次使用的官方接口 | EZapi 只负责 |
| --- | --- | --- |
| 生命周期 | ModHook 的 add/remove；相同委托启停配对 | IL2CPP 委托转换和异常记录 |
| 目录 | OnModItemDirectoryInit → Has/Add(ID,factory) | JSON/DLL统一ID、整组预检、逐项状态 |
| 模块与节点 | ModuleHelper.InitModuleItem / ModuleEffectHelper.InitNode | 八参数/JSON映射、保持helper默认标记；见native-items.zh-CN.md |
| 工厂 | ItemDirectory.CreateEmptyItem(ID) | 每次创建独立实例 |
| 名称和价值 | SetName、SetValue、SetUnitCount | 字段映射 |
| 简介 | shortDescription/longDescription/flavorText/customText | native 保留原生规则；early/late 只通过对应事件显示 |
| 提示文字 | OnCreateTooltipEarly/Late → RichTextBuilder.AddLine | 只追加自己的描述/标签/来源；不调用 Build |
| 动态模块/效果文字 | ModHook 官方 tooltip 回调；ModuleEffect.displayName/description 可写 | PresentationApi 限定 owner 与原生对象；不接管 builder 生命周期 |
| 分类 | SetGameItemType、TypeHelper | 自定义显示名为唯一内容相关兼容补丁 |
| 标签 | TagSystem.InitTagString/GetTag、TagState setters、GetTagReadonly | 默认值与动态显示绑定；long 写公开 valueLong |
| 形状 | GridShapeBuilder、SetShape | JSON 显式尺寸和 0/1 mask；不重写旋转和放置 |
| 单件规则 | GameItem.mayStackItemFunc | 每个新实例绑定 false；没有全局 MayStack 补丁 |
| 次数/违禁 | UseCountHelper、ContrabandHelper | 调用原生规则 |
| 图片绑定 | GameItem.SetSprite + 公开 RenderHandler.atlasCache | LegacySpriteAdapter 适配 JSON PNG；SpriteAssets 为 DLL Mod 提供隔离嵌入图集 |
| 库存生成 | DirectoryMaster.Item、TryInventorySlot、SlotMarker.TryAcceptOnce | 玩家按调试按钮时查当前库存、验证落点；不自动保存 |
| 音效 | soundUse 等公开声音 ID 字段 | 不创建 AudioSource、不扫描音频 |
| 内容包清单 | 官方 ModLoader 资源目录约定；当前 MelonLoader 入口自行读取 pack.json | ContentPacks 复用严格 JSON/PNG/掉落校验，依赖排序后才交给 ItemRegistry/LootTables；不伪造 LoadedMod |
| 掉落表 | LootRegistry.RegisterTable/RegisterGroup、Add/Remove/SetWeight、Roll；ModHelper 掉落便捷方法缺失 | JsonLoot 校验、双就绪门控、原生 journal 单次提交和只读快照；不写 tables/groups 字段、不生成库存物品 |
| 访客排队 | 官方 `ModHook.OnGenerateCustomerVeryLate(StoreClientManager)`；当前程序集公开 `StoreClientListDict.CreateStoreClient(string)` 与 `StoreClientManager.AddNextClient(StoreClient)` | 在末阶段钩子排入原生客户蓝图并读回队列；保留交易规则，覆盖模组肖像并通过独立 DialogueApi 展示脚本；精确构建门控，不声称自定义客户交易注册 |

## 本机实际缺少的文档接口

当前包装程序集的 GameItem 及其它类型中没有 SetSpriteAndShapeFromMod；ModHelper 仅暴露 GetSoundIdentifier，未暴露指南的 SpawnItem/GetLocalized/PlaySound 等便捷方法。这些不是 EZapi 已经实现的能力。

当前 Assembly-CSharp 还公开了 `LocHelper.GetCurrentLocaleCode(): string`。它不是官方 ModHelper 的 `GetLocalized` 扩展；EZapi 仅在当前已核对的程序集哈希匹配时调用它，为原生 DLL 物品的双语文本选择中文或英文，其它 locale 用英文。未知构建或调用失败回退英文，完整签名证据见 `verification/native-locale-contract.txt`。

PNG 因此有明确的兼容桥：JSON 包按 Textures/Items 读取，DLL Mod 用 SpriteAssets 读取程序集资源；两者预检 PNG 并放入不同的 EZapi 自有图集，再调用 SetSprite。没有 ResolveItemSprite/ResolveSpriteByName 补丁，不直接写 inner/fakeInner。此桥是当前构建适配，不冒充指南中的 SetSpriteAndShapeFromMod；完整调用规则见 sprite-assets.zh-CN.md。

官方图片透明度自动推导占格尚未接入；现有 JSON 使用 width/height/shapeRows 明确声明，不暗中改变旧包语义。ModLoader.Mods/LoadedMod 仅为官方自身加载流程，不向其中伪造模组。

Early/Late 表示官方提示流程的阶段，不保证整框第一/最后一行。两个 GetTooltip 方法可能共用原生入口，禁止重新为它们分别安装补丁。官方 builder 由游戏完成 Build，EZapi 不保存其引用。

## 0.1.3 激活行为

依据官方 LearnerMod/Item.cs 的治疗示例，直接给实例绑定 mayActivateSlotItemFunc、canActivateSlotItemFunc、onActivateSlotItemFunc；没有全局激活补丁。healWounds 按示例设置 HealthData.woundState=0、isWoundStable=true、isWoundedFresh=false。额外所有权/当前库存/伤势门控由 EZapi 提供；次数消耗调用本机构建的 UseCountHelper.CanUse/Use。它们仍需玩家验证实际交互与保存重载。

## 0.1.5 掉落表差异

本机构建公开了 `LootRegistry` 的表/组注册、增删改权重、重建和抽取方法；这些是当前程序集的直接证据。`ModHelper` 中的 `SpawnItem` 等便捷方法仍缺失，因此 EZapi 不伪造它们。新表/组按 owner 前缀隔离，JSON 与 C# 声明在两个官方就绪条件同时满足后提交一次。`GetSnapshot` 只复制托管数据；`TryRoll` 只返回 ID，会推进官方 RNG，不负责生成、发放或保存。原生 journal 的场景回放、实际拾荒/供应商获得和存档结果待玩家验证。

## 0.1.4 生命周期与数据差异

GameEvents 逐一订阅 OnGameLoadedInit/Early/Normal/Late，及 ShutterOpened/Closed、GoingSleep、WakingUp、LeavingStore、ReturningStore、HandlingNightlyServices 的 Early/Late，共18个官方阶段。没有事件 Harmony 补丁。

本机 ModHelper 无 SetModData/GetModData 系列，PlayerStore.modData 公开可读写。RunData 集中适配该字典；命名空间是 EZapi 自有格式，不伪装成缺失的方法。写入只是内存暂存，不调用 SaveGame，不自动覆盖损坏/未来版本记录。生命周期事件不代表保存成功。详见 lifecycle-rundata.zh-CN.md；真实保存重载待玩家测试。


## 0.1.6 内容包清单差异

官方文档描述的是内置 ModLoader 的资源扫描；当前 DLL 仍运行在 MelonLoader，因此 EZapi 只实现自己的 `EZapiPacks/<id>/pack.json` 读取。原有 `id/name/ezapiVersion` 清单保持兼容，可选 `version`、`minEzapiVersion`、`requires` 和 `dependencies` 只用于加载前检查。`ContentPacks.Check` 不声称目录依赖已满足，`CheckDirectory` 才对 JSON 包执行依赖拓扑排序。该检查不会扫描 DLL Mod、不会解码 PNG，也不证明游戏内物品或掉落已经生效。

## 0.1.7 当前构建的原生食品/液体接口

当前包装程序集公开 `FoodItemHelper.InitFoodItem(GameItem,int,bool,bool isMeat=false)`、`InitFreshFood(GameItem,int)` 和 `LiquidContainerHelper.InitLiquidContainerItem(GameItem,int,bool)`。从0.1.18起液体工厂改用当前 WaterHelper 的八参数初始化及 FillWithBaseWater；旧 LiquidContainerHelper 只有签名，实测未创建新版容量/成分。食品继续调用上述 FoodItemHelper；官方通用注册流程保持，food/liquid 的具体签名证据来自当前程序集，并非把治疗示例扩写成通用吃喝系统。原生 food/liquid 的可用场景、委托、效果、内容和保存需要游戏内证据。没有新增 Harmony 补丁或自己的进食/饮用回调。

## 0.1.8 本地化资源边界

官方指南明确 `Localization/<locale>.csv` 为无表头 UTF-8、首逗号分隔、`""` 转义和字面 `\n` 换行，真实跨行不支持。本机 `ModHelper` 仅保留 warnedLocalizationKeys 属性和 GetSoundIdentifier，没有可调用的 GetLocalized/GetCurrentLocale；证据见 verification/localization-api-contract.txt。

EZapi 实现自有纯托管 CSV 快照、显式语言查询、英语回退和调用者可读 fallback；API 命名为 `Localization`，不伪装官方方法。资源在完整预检且物品批次接受后公开，独立 `LoadDirectory` 可供 CLI/DLL 使用。不修改游戏字符串表、LoadedMod、物品名字或全局语言，无新增 Harmony 钩子。自动语言、JSON 文本键和已有物品刷新继续 Planned。


## 0.1.9 原生储物结构

官方 LearnerMod/PurpleBloodSynthesizer.cs 提供 PixelWindow、GameGridInventory、Attach、SetContentWindow 的结构示例；储物初始化使用本机构建完整签名确认的 ContainerHelper.InitContainerItem(GameInventory,GameItem,List<string>,List<string>)。矩形或 mask 网格和窗口按实例创建，identifier 为物品ID.storage。没有全局容器补丁，不覆盖准入/嵌套委托、不搬运物品、不自动保存。完整签名证据见 verification/container-api-contract.txt 和 container-helper-contract.txt；玩家操作与持久结果仍待验证，见 containers.zh-CN.md。


## 0.1.10原生运行时服务

物品目录调用当前包装程序集的DirectoryMaster.Has<GameItem>/Item与GeneralHelper.GetDisplayNameFromID。实例值使用GetCurrentValue，tooltip由GetTooltip/GetTooltipAdvanced完成。电力使用MachineryHelper.GetMachinePowerUsage、PowerHelper.GetAvailableEnergyFromItem(false)、CanDrawPowerSource/DrawPowerSource/SetPowerSourceAt。液体成分从已存在Liquid.Liquids的currentPart标签读取，容量/纯度/条件调用WaterHelper与WaterFeatureHelper。完整签名证据在verification/native-resources-contract.txt与native-item-access-contract.txt。

<!-- power-batch-0.1.43 -->

0.1.43 的电源扣款继续使用当前官方 PowerHelper.CanDrawPowerSource/DrawPowerSource/SetPowerSourceAt；身份读回使用 GameItem.uniqueId、identifier、unitCount 与 parentInventory。原生 helper 负责一次调用的实际电量差额；框架仅把原始堆叠数量当作身份快照，不按数量乘电耗，也不直接改 unitCount。多源 `PowerBatchMath.Plan` 是纯托管分摊，提交仍由调用者放入 `Transactions.Run` 并逐源核对。

Transactions是EZapi自己的同步补偿编排，不伪装官方数据库事务。Forge元数据仅作为最低功能范围参考，未引用或加载Forge DLL。存档/原生生命周期限制见native-services.zh-CN.md。


## 0.1.11 库存适配

观察使用本机构建FindAllItem/saveBags/GraphNodeStorage.children/childItems，几何用modifiedShape和公开GridShapeBuilder。规划通过原生TryInventorySlot验证，移动通过SlotMarker.TryAcceptOnce恰好一次；源目标纯托管快照审计拆堆/合堆后的实际变化。不直接改父图/数量或读取内存偏移。Forge仅为公共能力范围参考，不是依赖；接口证据与真实游戏行为分开记录，详见inventory-services.zh-CN.md。

## 0.1.17液体转移适配

LiquidTransferApi整次倒液直接调用WaterHelper.TransferLiquid(GameItem,GameItem):void，限量使用WaterHelper.SplitProportionally(Il2CppStructArray<int>,int)、Remove(GameItem,int)、AddLiquid(GameItem,string,int)。GetFilterItem用于拒绝无法补偿的过滤器。使用TagSystem.Clone及公开dict作同步补偿，无裸地址/字段偏移，无全局倒液补丁。LiquidCompositionMath纯计算与原生分配明确区分，LiquidMetadata只写EZapi命名空间标签；完整签名和引用核验见verification。行为和保存重载仍待新游戏进程验收，详见liquid-transfer.zh-CN.md。

## 0.1.18 原生适配

新局跳过开场可能没有加载事件：仅在店铺库存可见且玩家身份有效后建立 GameSession，不伪造 GameLoaded 通知。场景卸载时清理，不能在新场景 Awake 已发事件后再次抹身份。

GameItem 的谓词通过注入的原生接收器方法绑定，以兼容原生反射 DynamicInvoke；没有核心 Delegate 补丁。容器遮罩和过滤集合语义见 containers.zh-CN.md。详细失败实验与当前成功证据见 native-compatibility-0.1.18.zh-CN.md。

原生 customText 不是普通提示框文本：当前实例在库存上显示这段文字。PNG之外的默认占位图按 UISettings.current.gridSize 与声明尺寸生成；没有修改原生精灵解析器。

<!-- machine-layouts-guide-0.1.21 -->

0.1.21机器布局使用官方 PixelWindow/GridPixelElement/GameGridInventory/GameSlotInventory、MachineHelper.SetupBatterySlot/SetupModuleBay/SetupManualSlot。当前050rev5-L实测GetBatterySlot路由为(0,1)，GetModuleInv为(1,1)。不写裸地址/偏移。图鉴使用已验证的GUI.Box/Label及原生接收器输入隔离；未调用本构建unstripping失败的GUI.BeginScrollView/DrawTexture。图鉴条目不是原生生产配方。

<!-- machine-transactions-0.1.22 -->

0.1.22加工资源事务以公开目录创建单件，内容方使用官方Expel/TryInventorySlot/TryAcceptOnce/WaterHelper.Remove/PowerHelper.DrawPowerSource并自行核验。框架管理新产品和补偿调度。逐TagState.Clone修复TagSystem.Clone浅标签问题；原生调用仍受当前签名/主线程/生命周期约束，不调用SaveGame。调试临时资源不代表完整标准配方/跨夜机器运行时。

<!-- machine-transactions-0.1.23 -->

0.1.23加工资源事务以公开目录创建单件，内容方使用官方Expel/TryInventorySlot/TryAcceptOnce/WaterHelper.Remove/PowerHelper.DrawPowerSource并自行核验。框架管理新产品和补偿调度。逐TagState.Clone修复TagSystem.Clone浅标签问题；原生调用仍受当前签名/主线程/生命周期约束，不调用SaveGame。调试临时资源不代表完整标准配方/跨夜机器运行时。

<!-- machine-recipes-0.1.24 -->

0.1.24纯托管配方分配和动态解析复用当前原生库存观察、Power.GetMachineCost/CanDraw、液体实际快照与有界落点规划；真实写入仍由IMachineBatchTransaction负责。InstallInputAdmission使用既有精确库存AND准入组，保留原生拒绝。实机临时事务使用官方Expel/TryInventorySlot/TryAcceptOnce/WaterHelper.Remove/DrawPowerSource。没有额外Harmony补丁、SaveGame或原生夜间调度；不以Expel暂存输入冒充通用材料消费。

<!-- machine-materials-0.1.25 -->
0.1.25默认事务使用当前公开GetStackSlot/TryInventorySlot/TryAcceptOnce/Expel/Destroy，确认前保留材料可逆性；WaterHelper.Remove混合扣液、DrawPowerSource扣电、EmptyContainer/AddLiquid正数仅填充新产物。无新增原生hook目标；既有冻结hook增加内部精确许可。AddLiquid负数实测无作用，指定成分默认扣液写前拒绝。没有自动保存/跨夜。

0.1.26统一机器层复用官方ModItemDirectory/Add/CreateEmptyItem与既有布局/配方/事务。没有新增Harmony目标、没有夜间或自动保存调用；工厂通知在所有托管目录提交后分发。

0.1.27复用已核对的PlayerStore.EndNight→Lifecycle.BeforeNight，新增机器逻辑无新增Harmony目标；GameItem.state/SyncModifiedState保存夜次与故障标签。Unity场景对象先使用原生Unity存活判断，再读取inventoryCanvas.activeInHierarchy，不跨场景缓存。

外部电力采用官方PowerHelper与精确实例初始化，因官方没有可恢复外电扩展事件，目录就绪后安装独立十目标组；详情见external-power.zh-CN.md。普通电池沿用原生路径；原版熔炉外电批次恢复未实现。

<!-- suppliers-0.1.41 -->

0.1.41 供应商 API 优先采用官方 `ModHook.OnGenerateCustomerVeryLate(StoreClientManager)` 顾客生成入口；官方指南定义顾客生成层级为 `VeryEarly → Early → Normal → Late → VeryLate`，并要求在生命周期合适时订阅/取消事件及隔离订阅者异常。本机构建程序集直接确认 `StoreClient` 可创建、`StoreClientManager.AddNextClient(StoreClient)` 可调用；方法签名与程序集 SHA256 记录在 `official-sources.index.json`。官方指南将原生商店行为/内容修改列为不支持，但提供 `LootRegistry` 掉落表编辑；原生随机供应商商品应通过对应表维护。EZapi 的 `SupplierApi` 是独立对话访客与作者回调，不改商店货架、LootRegistry 或原生采购系统。2026-10-21 在游戏 0.46D 中实测了访客出现、肖像、报价预览/返回/确认、继续刷新和离场；回调只计数，未执行真实支付、发货或存档写入，见 `suppliers.zh-CN.md`。

## 官方本地克隆

开发工作区参考目录为 `references/probably-stolen-modding/`，提交 `f2137eb7bebeecfadca1ccabb0cae3757ea4319f`。本次通过已经核对官方 origin 的本机 Git 缓存独立克隆，未验证远程最新状态。来源索引记录逐文件 SHA256；本次 RunDataApi 依据中文指南“保存模组数据”段落，并继续使用当前程序集公开的 PlayerStore.modData。

0.1.47：官方指南支持带自定义图像的 Mod 顾客；当前构建公开 SpriteDict.Instance、spriteDictionary 与实例 GetSprite(string)。EmbeddedSpriteAtlas.RegisterPortrait 经窄适配登记自有映射并核对原生读回，未全局替换图片解析。不把此适配冒充新版资源便捷方法；场景重建后使用前重新注册，视觉与持久化验证单独记录。

0.1.54覆盖上文0.1.25指定成分写前拒绝的旧限制：标准事务改走官方ModifyTag与当前构建Remove使用的OnLiquidContainerPourOutOf，写前完整签名及构建门控，冻结内逐成分写后读回；不以AddLiquid负数、清空重灌或裸地址替代。精确扣液与失败补偿六场景已通过原生验收，见machine-materials.zh-CN.md。

0.1.55依据澄清：本地官方指南无专门液体处理章节；第554行说明物品状态使用ModifyTag/GetTagReadonly。官方PurpleBloodSynthesizer示例演示机器布局/电力/物品产出。WaterHelper.AddLiquid(GameItem,string,int):void来自当前游戏公开程序集，其数量为原生parts。快照、冻结、容器身份审计及同步补偿为EZapi独立策略，不能说成官方指南已给出完整液体事务。见machine-liquid-output.zh-CN.md与official-sources.index.json。

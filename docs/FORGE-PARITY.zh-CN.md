# EZapi 公开参考功能对应清单

当前基线0.1.71：39组已实现、0组部分、0组缺失。这里只说明独立功能对应，不承诺ABI兼容、所有玩法组合或完整项目目标完成。最新机器可读清单见 [forge-parity.index.json](forge-parity.index.json)。

| 功能 | EZapi入口 | 状态 | 边界 |
| --- | --- | --- | --- |
| 通用批量注册与依赖状态 | RegistrationApi.Register/Snapshot; RegistrationBatch.Require/Define | implemented | 七类定义元数据、整批提交/拒绝、幂等/冲突、最低版本与定义依赖、循环等待、不可变快照、生命周期已实现。DependenciesResolved不等于原生内容Applied。 |
| 嵌入图片与图集 | SpriteAssets.CreateEmbeddedAtlas / EmbeddedSpriteAtlas.GetSprite, TryInstall, Assign, RegisterPortrait | implemented | All embedded atlas public contracts implemented independently. EZapi 0.1.47 native smoke test passed 9/9 including current SpriteDict portrait registration, native GetSprite readback and idempotence; rejects foreign mappings and invalidates stopped handles. Portrait visual display, scene re-registration inside the real game, and save/reload remain unverified; offline tests cover scene replacement and cleanup failures. |
| 补偿事务 | Transactions.Run | implemented | 同步Check/Apply/Restore，包含部分失败步骤的反向补偿；无存档事务保证 |
| 运行时能力门控 | RuntimeCapabilities.Current | implemented | 覆盖20项同名运行时快照字段；快照只报告EZapi门控，具体操作结果和游戏行为仍须单独验证 |
| 访客排队与状态 | ClientVisitApi.Queue / ClientVisitApi.Status / ClientVisitApi.IsAvailable | implemented | Uses official OnGenerateCustomerVeryLate and the current build public StoreClientListDict.CreateStoreClient/AddNextClient path; reuses native trade behavior and only overrides sprite plus visit callbacks. Queue/readback logic has offline tests; in-game arrival, sprite rendering, dialogue and third-party patch combinations remain unverified. |
| 内容应用事件 | ContentApi.Subscribe | implemented | 全局通知/订阅者owner/唯一ID/释放/异常隔离；当前item/effect均发布真实转换，后续目录继续统一 |
| 分支对话 | DialogueApi.Create/Open/Close/IsAvailable/GetRuntimeStatus | implemented | Independent native choice graph and immutable script; no JSON dialogue importer; Phone/ClientVisit remain separate; no real third-party patch combination claim |
| 效果注册与快照 | EffectApi | implemented | 整批暂存、真实效果目录与随机排除/嵌套finalizer/确切对象清理；原生行为待验 |
| 外部电力事务 | ExternalPowerApi / IPowerTransaction | implemented | 外电两类型一属性八方法行为已审计；原版熔炉含材料子图显式ConsumeGraph、旧输出子图保留及失败恢复233/0，默认四参数ABI保留。EZapi外电两晚保存/冷启动有独立证据；本轮熔炉不覆盖所有跨夜组合，无异步/网络/崩溃恢复。 |
| 原生补丁目标检查 | HookApi | implemented | Interop生成元数据和入口检查；不安装用户补丁 |
| 库存准入扩展 | InventoryAdmissionApi | implemented | 指定容器/子库存AND准入；原生拒绝优先，游戏待验 |
| 库存观察与搬运预检 | InventoryApi | implemented | 原生观察、拥有量统计、直接快照、整件转移预检；游戏待验 |
| 持久库存端点 | InventoryEndpointApi.Open/Snapshot/CaptureContents/Store/Take/Delete | implemented | 当前构建原生保存、新进程恢复、删除后冷启动、外出返回和存取守恒已验证；充电14项含隔离家具过滤器，未验证真实突袭/联机组合。 |
| 嵌套物品图编码与恢复 | SaveGraphApi.Items/Encode/Decode | implemented | 已按050REV5-L真实DecodeSaveItem序列接入11步后处理，未知构建或精确签名缺失时写前拒绝。独立图内存往返与磁盘保存/冷启动是分别验收的行为。 |
| 物品冻结与释放 | InventoryFreezeApi | implemented | 原生移除/接纳门控，Epoch与句柄释放；游戏待验 |
| 整组网格布局规划 | InventoryPlacementApi | implemented | 有界原生准入规划；不提交移动，游戏待验 |
| 物品/节点/生活用品/模块注册及目录访问 | ItemRegistry / ItemApi / Items | implemented | 独立物品/节点/设施/模块工厂及目录已验证；夜间供应改用官方Late显式上架，保留原生容量/perk规则。0.1.65完成原生完整补货、真实购买扣款、磁盘读回和新进程恢复；NPC命名表/矿工/固定货位有0.1.64证据。特定NPC对话购买及其它Mod组合不是本轮验收范围。 |
| 实例价格与提示 | ItemInspection | implemented | 支持正数堆叠的原生当前单件价值，无拆分/数量修改；0.1.49估值425项实机断言通过。普通/高级提示沿用原生完成结果，未在本轮重新做视觉验收。 |
| 实际物品搬运 | ItemTransferApi | implemented | 单次原生接纳及写后差额核对；不自动重试/保存，游戏待验 |
| 生命周期订阅 | LifecycleApi.Subscribe | implemented | 10阶段精确7原生入口；全int优先级/owner/ID/释放/周目日快照；独立钩子门控，游戏待验 |
| 液体容器与成分快照 | Liquids.IsContainer/Capture/WaterQuality | implemented | 原生成分/容量/纯度只读快照；游戏内单位与行为仍待验证 |
| 液体转移与核对 | LiquidTransferApi.Pour | implemented | 原生整次/限量双容器冻结与同步补偿；过滤反应及状态未知时停止，游戏待验 |
| 列表库存创建与搬运 | ListInventoryApi | implemented | 独立原生窗口/网格、尺寸标签恢复/有限扩容、审计转移；保存待验 |
| 机器配方自动化事件 | MachineAutomationApi / MachineAutomationContext | implemented | synchronous ordered Before/After callbacks, selection/conflict/block, disposal and errors; exact DTO documented |
| 机器注册与追加配方 | MachineRegistrationApi | implemented | 34类型114属性20方法行为审查完成；0.1.70补齐独立日志、窗口捕获和终端规范化辅助入口。12组1587项实机通过，普通两晚保存重载证据复用。无Forge二进制兼容或所有第三方组合验证承诺。 |
| 机器槽位与批次执行 | MachineRuntimeApi / MachineRecipeApi / MachineLayoutApi | implemented | 34类型114属性20方法行为审查完成；0.1.70补齐独立日志、窗口捕获和终端规范化辅助入口。12组1587项实机通过，普通两晚保存重载证据复用。无Forge二进制兼容或所有第三方组合验证承诺。 |
| 模块准入/重算/动作/成长 | ModuleApi | implemented | 八入口/四组钩子/同步上下文/逐调用成长；临时实机验收通过，原版生产与持久化待验 |
| 电话联系人 | PhoneApi.Register/Unregister/Snapshot/IsAvailable/GetRuntimeStatus | implemented | Native phone book and dial with current epoch; independent immutable script; C# only; native saved-contact serialization and real third-party combinations not verified |
| 电力读取与扣电 | Power.GetMachineCost/GetEnergy/TryDebit | implemented | 独立外电需机器上下文，使用外电与批次事务接口 |
| 名称/说明/效果显示 | PresentationApi | implemented | 双语 owner 来源名与登记文本查询；模块 Late tooltip 动态文字；只本地化同 owner 且仍应用的效果对象，并按字段所有权恢复；UI顺序与实机语言切换待验 |
| 批次投入价值与产出计价 | ProductionValueApi / ProductionValueMath / MachinePowerMath | implemented | 合计投入/选择器/decimal向上取整/独立基础价值标签与失败恢复；保存重载待验 |
| 阅读窗口 | ReadingApi.Open/Close/IsAvailable | implemented | C# actual native handnote panel; no reading JSON; owner/document/run scope; real third-party patch ordering not verified |
| 配方图鉴 | RecipeGuideApi / JsonRecipeGuide | implemented | 六公开类型22属性及三个方法已审计；手动/自动图鉴、双语字段、原生语言切换、分页、紧密/旋转网格图片和资源释放有证据。0.1.66九组954/0、图片界面7/0、中英8/0；等语言资源就绪后才建预览。默认实例图像，不覆盖实时材质/动画或第三方工厂副作用。 |
| 周目数据读取与暂存 | RunDataApi.Read/Stage + RunDataStatus/RunDataResult; RunData.Open preserved | implemented | JSON object records, exact-text compare-and-stage, all eight result states, owner keys, slot/run checks and readback; easy overload also checks event epoch. Native persistence unverified; no automatic Forge/ModHelper/legacy-key migration. |
| 周目资金与购买事务 | RunPurchaseApi.Purchase | implemented | native GetCash/PayMoney/ModCash; current identity, 0..128 author steps, shared Transactions exclusion; unknown cash or failed delivery compensation retains payment and reports RecoveryFailed; no save/crash atomicity |
| 存档文件只读解析 | SaveReadbackApi / SaveReadStatus / SaveReadResult | implemented | 只读有界文本快照、UTF-8/ES3规范化、文件变化/锁检测和唯一属性读取；不写存档、不恢复物品、不证明保存事务。 |
| 开局身份注册 | StartApi | implemented | Owner/ID validation, enum validation, initialization-window/thread checks, idempotent duplicate/conflict handling and immutable sorted snapshot are implemented. This stores a mapping only; menu integration and changes to starting gameplay are outside this API and remain unimplemented. |
| 供应商排程与商品 | SupplierApi / SupplierDefinition / SupplierPage / SupplierOffer | implemented | Native test on Probably Stolen 0.46D with EZapi 0.1.41 verified visitor arrival, portrait, quote preview/back/confirm, page refresh and dismissal on disposable slot 533. The callback only counted; real payment, stock mutation, delivery, compensation and saving were not exercised and remain author-owned. Uses the official customer-generation hook and known-build StoreClient/AddNextClient surface to queue an isolated DIALOGUE visitor; does not change native store shelves or LootRegistry. EZapi adds a process-only Status query. |
| 工坊页/进度链/解锁 | WorkshopApi / WorkshopSnapshot / WorkshopEntry | implemented | All six public API members and workshop DTOs implemented independently. 0.1.48 real-game automated checks passed 23/23 for list rendering, long-text arrows/wheel/reverse wheel, progress graph links/node details, managed action callback and close cleanup. Native test exercised a registered tab; RegisterChain/OpenChain remain compile/offline verified only. Qualification, payment, delivery, compensation, idempotence and persistence remain author-owned; no claim of integration into the native game workshop screen. |

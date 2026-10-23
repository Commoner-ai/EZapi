# 当前参考功能覆盖

当前程序集 0.1.77：参考清单中 39 组具有独立实现入口，0 组部分、0 组缺失。这个数字只描述参考功能覆盖，不表示全部开发完成、行为组合认证、Forge ABI 兼容或当前 DLL 实机验收。

当前状态见[统一版本与验收状态](current-status.zh-CN.md)。精确重载见[公开 API 签名](public-api.index.json)。索引里的历史 native 标记已单独保存，当前 native/runtime 验证均为 false。

| 功能 | EZapi 入口 | 实现状态 |
| --- | --- | --- |
| 通用批量注册与依赖状态 | RegistrationApi.Register/Snapshot; RegistrationBatch.Require/Define | implemented |
| 嵌入图片与图集 | SpriteAssets.CreateEmbeddedAtlas / EmbeddedSpriteAtlas.GetSprite, TryInstall, Assign, RegisterPortrait | implemented |
| 补偿事务 | Transactions.Run | implemented |
| 运行时能力门控 | RuntimeCapabilities.Current | implemented |
| 访客排队与状态 | ClientVisitApi.Queue / ClientVisitApi.Status / ClientVisitApi.IsAvailable | implemented |
| 内容应用事件 | ContentApi.Subscribe | implemented |
| 分支对话 | DialogueApi.Create/Open/Close/IsAvailable/GetRuntimeStatus | implemented |
| 效果注册与快照 | EffectApi | implemented |
| 外部电力事务 | ExternalPowerApi / IPowerTransaction | implemented |
| 原生补丁目标检查 | HookApi | implemented |
| 库存准入扩展 | InventoryAdmissionApi | implemented |
| 库存观察与搬运预检 | InventoryApi | implemented |
| 持久库存端点 | InventoryEndpointApi.Open/Snapshot/CaptureContents/Store/Take/Delete | implemented |
| 嵌套物品图编码与恢复 | SaveGraphApi.Items/Encode/Decode | implemented |
| 物品冻结与释放 | InventoryFreezeApi | implemented |
| 整组网格布局规划 | InventoryPlacementApi | implemented |
| 物品/节点/生活用品/模块注册及目录访问 | ItemRegistry / ItemApi / Items | implemented |
| 实例价格与提示 | ItemInspection | implemented |
| 实际物品搬运 | ItemTransferApi | implemented |
| 生命周期订阅 | LifecycleApi.Subscribe | implemented |
| 液体容器与成分快照 | Liquids.IsContainer/Capture/WaterQuality | implemented |
| 液体转移与核对 | LiquidTransferApi.Pour | implemented |
| 列表库存创建与搬运 | ListInventoryApi | implemented |
| 机器配方自动化事件 | MachineAutomationApi / MachineAutomationContext | implemented |
| 机器注册与追加配方 | MachineRegistrationApi | implemented |
| 机器槽位与批次执行 | MachineRuntimeApi / MachineRecipeApi / MachineLayoutApi | implemented |
| 模块准入/重算/动作/成长 | ModuleApi | implemented |
| 电话联系人 | PhoneApi.Register/Unregister/Snapshot/IsAvailable/GetRuntimeStatus | implemented |
| 电力读取与扣电 | Power.GetMachineCost/GetEnergy/TryDebit | implemented |
| 名称/说明/效果显示 | PresentationApi | implemented |
| 批次投入价值与产出计价 | ProductionValueApi / ProductionValueMath / MachinePowerMath | implemented |
| 阅读窗口 | ReadingApi.Open/Close/IsAvailable | implemented |
| 配方图鉴 | RecipeGuideApi / JsonRecipeGuide | implemented |
| 周目数据读取与暂存 | RunDataApi.Read/Stage + RunDataStatus/RunDataResult; RunData.Open preserved | implemented |
| 周目资金与购买事务 | RunPurchaseApi.Purchase | implemented |
| 存档文件只读解析 | SaveReadbackApi / SaveReadStatus / SaveReadResult | implemented |
| 开局身份注册 | StartApi | implemented |
| 供应商排程与商品 | SupplierApi / SupplierDefinition / SupplierPage / SupplierOffer | implemented |
| 工坊页/进度链/解锁 | WorkshopApi / WorkshopSnapshot / WorkshopEntry | implemented |

每项边界及历史研究标记见[机器可读功能映射](forge-parity.index.json)。参考清单的 149 种公开类型覆盖属于元数据映射，不代表逐项运行行为均已验收。自适应外出按钮等后续独立扩展不计入该固定参考组数。

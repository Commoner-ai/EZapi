> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 同一次游戏会话完成批量验收（）

EZapi 继续独立实现 Forge 公共能力。本次包含 0.1.15 的生产计价与效果注册，增加批量调试入口并修复当前游戏接口适配；完整 Forge 范围仍未完成。多语言扩展暂缓。

## 已发现的问题

2026-10-19 23:00 的旧进程实际加载 0.1.14（DLL SHA256 07617F982B27E885A7D15D0FC0CD81B2E5977946FDF7EFB7AA2CD25E96BDBF8B）。23:11 的工厂调用反复抛出 MissingMethodException：FoodItemHelper.InitFoodItem(GameItem,int,bool)。该进程的十项临时物品检查全部失败，不能沿用为新版本通过证据。

当前 Assembly-CSharp.dll SHA256 E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF；官方生成包装签名为 InitFoodItem(GameItem item,int calories,bool isCanned,bool isMeat=false)。新增 food.meat / FoodDefinition.Meat，默认 false。显式调用四参数版本。原先函数在JIT解析旧引用时可连带阻止普通物品工厂完成，日志中未初始化的占位物品不是创建成功。

同一旧进程在普通游戏加载时出现 EZAPI_LIST_DIMENSIONS。原实现用 GetTagReadonly 非null 判断是否是自己的列表，无法可靠证明保存标签存在。现读取 owner.state.dict 的精确键，仅真正存在的列表标记进入恢复；标签缺失不创建、不写入、不阻挡普通网格。真实标签缺半、损坏、要求缩容仍拒绝，避免隐藏损坏。非null缺失标签替身回归已覆盖；实际原生只读标签实现仍待新进程验证。

## 一次游戏会话中的步骤

1. 确认 Mods 仅启用 EZapi.dll，日志版本为 0.1.17，使用可丢弃测试档进入店铺，显示主库存；放下正在拖拽的物品。
2. 左上角“EZapi 调试”→“一次批量检查”。它逐帧运行五组：开启生命周期记录；全部已声明物品的两件临时实例/独立状态/形状/标签/tooltip/容器结构；当前库存只读；当前资源只读；生产计算及效果注册状态。临时实例结束销毁，最多64声明；不向玩家库存发放、不搬运、不消耗、不触发保存。
3. 等待日志 Batch collected 5 groups，自动输出 UserData/EZapi/diagnostics.json。batchDebug.state=collected 表示收集结束；每组 collected 不能代替各自领域检查通过。nativeChecks 应是10通过0失败；inventoryGuards 三组及 forgeLifecycle 钩子应 Ready=true。productionDebug 示例为44/件，电量7/批、21/三件。默认没有自定义效果声明，空效果列表与未安装随机守卫不算失败。
4. 同次会话按单件生成按钮测试食品、水瓶、两种收纳容器及敷料。检查中文名称/图片/占格/悬浮说明，普通物品和敷料能拖入允许的容器，被过滤物品不能拖入；关闭窗口后拖动应正常。实际吃喝消耗要观察前后数量/内容，不能仅凭工厂属性成功。
   同次会话另做新液体接口验收：准备一满一空两瓶（水瓶页可生成新空瓶），点“倒液预览”看清源→目标，再点“执行预览≤1ml”；检查liquidTransferDebug前后成分。此操作明确写液体，独立于只读批量按钮，不自动保存。详细边界见liquid-transfer.zh-CN.md。
5. 正常睡觉一次并外出回店。阶段记录保持开启；点击“导出诊断”记录 lifecycleTrace。批量检查运行中若切场景/换周目/开始拖动，会取消剩余组并记原因；重新点击才重跑。
6. 在该测试档完成一次正常保存并通过游戏正常载入，在同一进程中重新点击批量检查，核对保存的物品ID、容器内容和标签；额外点“读取测试数”对照显式+1后的存档恢复。跨进程持久性还需下一次正常启动回读，不能用同一会话检查替代。

## 证据边界

本轮已按用户最新明确授权自行启动、操作和检查游戏，使用有 PID/窗口尺寸/焦点门控的 Win32 点击脚本重放已确认坐标。具体产物、失败实验、真实玩法与保存回读范围见 native-compatibility-0.1.18.zh-CN.md；不能将批量 collected 当作全部 PASS。

离线测试、构建和当前产物的直接游戏方法引用完整签名检查不证明IL2CPP钩子、Unity布局、吃喝/生产/随机效果或保存重载通过。当前官方水培恢复入口缺失，SaveGraph.Decode 会在创建原生对象前明确拒绝；不能把导出物品图或保存请求当作恢复成功。当前使用最新声明的效果API仍需独立注册/随机排除原生路径验证，批量按钮只读取状态，不注入临时效果。

## AI维护索引

- src/EZapi/DebugBatch.cs：排队、分帧、周目校验、取消、报告边界。
- src/EZapi/EZapiDebugOverlay.cs：玩家点击；Update执行，不在OnGUI生成。
- src/EZapi/NativeItemFactory.cs / ItemDefinition.cs：食品官方参数与默认值。
- src/EZapi/ListInventory.cs：精确持久标签读取，普通容器隔离。
- verification/native-reference-check.json：编译产物对当前游戏直接引用检查（不含反射和补丁）。
- verification/batch-validation-tests.json：真实调度代码加替身测试，不是原生运行。
- docs/forge-parity.index.json：未完成的完整Forge能力对照，禁止缩小目标。

0.1.18 的已执行范围以 native-compatibility-0.1.18.zh-CN.md 为准：原生十件检查、真实过滤/拖放、限量倒液、跨天和两种重载已执行。普通外出被第2天重要访客流程限制，未完成往返；不强制修改剧情状态。联机大厅未加载。

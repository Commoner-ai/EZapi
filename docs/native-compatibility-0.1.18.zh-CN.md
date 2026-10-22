# EZapi 0.1.18：当前构建适配与实际游戏验收

日期：2026-10-20。游戏画面版本 **050rev5-L**，Melon/Unity 元数据仍标记旧版本，不能据此选用旧接口。当前 Assembly-CSharp.dll SHA256 为 `E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF`。本版是兼容修复阶段，Forge 全部公共功能的独立覆盖目标仍未完成。

## 修复原因与实现

1. **新局没有可用周目身份**：部分新局路径不发出 GameLoaded。GameSession 仅在主线程、店铺真实库存可见且玩家身份有效后观察绑定；已有身份不重复建立。卸载场景清理，不在新场景 Awake 已发官方事件后再次抹掉身份；不伪造 GameLoaded 通知。
2. **原生回调 TargetException**：原生 GameItem.MayStack 通过 Delegate.DynamicInvoke 调用普通托管闭包会失败。NativeDelegateBridge 使用 ClassInjector 注册无物品/玩家字段的原生接收器，并绑定其原生方法，供堆叠和激活委托使用。进程保留委托和接收器，不缓存周目对象。不要直接使用宿主接收器；内容作者仍调用 ItemActivation.Register。
3. **水瓶容量为零**：旧 LiquidContainerHelper 虽有签名，实测未创建新版液体状态。改用当前 WaterHelper.InitLiquidContainerItem 的完整八参数签名，满瓶另调用 FillWithBaseWater。容量单位 ml；500ml 对应 500000parts。不会在加载后重灌玩家已有瓶子。
4. **库存边界与物品形状混淆**：当前物品遮罩 1=占格、外部=0；库存遮罩 1=禁止、0=可用、外部=1。InventoryShape 增加 OutsideBounds，保留原七参数构造器；快照和放置预检区分两类语义，原生 TryInventorySlot 仍作最终接纳判断。
5. **异形容器内部反转**：JSON `container.shapeRows` 的 1=可用。NativeContainerAdapter 只对内部网格转换为原生禁止遮罩，物品外形不反转。`["111","110"]` 是五格可用、右下角禁止。
6. **ID 限定容器接受无关物品**：原生标签/ID 条件按 OR 组合；一个参数为 null 会开放对应分支。无过滤时两个参数均为 null；有过滤时两个参数均非 null，未配置的类别传空集合。保留原生准入委托，不添加全局过滤补丁。声明仍禁止两种过滤列表同时非空。
7. **占位图比例**：无 PNG 时按 UISettings.current.gridSize 和声明宽高生成棋盘图；不再用一张 2×2 方图表达所有矩形。图集仍隔离，使用公开 SetSprite，未改全局精灵解析器。已有 PNG 的实际比例保留。

库存调试增加最多 64 件物品的现有渲染节点只读快照，每件最多 8 个 Image；仅按钮触发，不每帧扫描或强刷 UI。对照发现 `descriptionPlacement=native` 的 `customText` 可成为库存上的原生文字，它不是普通 tooltip 行。只想加提示文字应使用 tooltip.lines 或 early/late 模式。示例工具包故意保留该字段，不能把其文字显示误判为丢失物品。

## 失败实验留档

早期候选曾尝试修补核心 Delegate.DynamicInvoke，导致 coreclr.dll `c0000005` 进程崩溃。该实验已完全移除，当前 `delegateBridge.coreDelegatePatches=0`。原始崩溃日志保留于开发区 `work/native-0.1.18-20261020/candidate1-crash.log` 和 `candidate1-windows-crash.json`。不得重新引入核心委托补丁。

## 实际执行与证据

全部游戏操作在独立测试档 527 完成，RunID 为 `a8aa3349-8594-4e48-86a2-9be168ed8819`。历史存档提前备份；框架不主动调用保存。正常保存通过开门营业→打烊→睡袋→结算→翌日完成。

- 十件临时工厂实例：属性、分类、标签、普通/详细 tooltip、原生堆叠委托、容器形状/ID 谓词、液体容量/初量，10 通过、0 失败。临时检查不交付物品、不治疗、不消费。
- 真实生成、打开容器和拖放：工具包被限定容器拒绝，敷料被接纳；右下禁止格拒绝放置；关闭窗口后工具包可移到主库存 (7,6)。
- 限量倒液：源瓶 500000→499000parts，新空瓶 0→1000parts；`Poured`、`PartsMoved=1000`、`Indeterminate=false`。两侧成分逐项前后记录可核对。
- 正常跨天：第 1 天→第 2 天，七个夜间生命周期阶段记录完整，实际槽文件落盘。测试计数 1 保留。
- 同进程返回主菜单后加载：同一槽、同一 RunID、Epoch 更新；物品数量/位置、盒内敷料、液量及计数保留。
- 正常退出后冷启动：再次从槽 527 读取到三瓶 **499ml / 500ml / 1ml**，工具包位置及盒内敷料保留，计数 1；未通过重生成恢复这些状态。

只读批量检查结束的 `collected` 不等于所有玩法通过。检查各域报告、真实库存/液量、阶段记录及新日志。发布包 `verification/` 提供精选诊断、截图、输入记录及哈希，不包含用户完整存档。

离线执行 10 套共 **564** 个唯一检查：session49、activation35、consumable46、container44、batch19、inventory83、guard78、lifecycle52、liquid-transfer96、packs62。离线替身检查不计为原生玩法验收。

## 可重用点击脚本

`verification/Invoke-EZapiNativeTest.ps1` 支持 Launch、Close、Capture、Archive、RunPlan。使用 PowerShell 7；不要嵌套 Windows PowerShell 5 读取中文脚本。计划为 JSON 数组，每步有唯一 name 和 kind，可用 Click / RightClick / DoubleClick / Move / Drag / Key / Snapshot。完整计划先校验，输入前检查进程唯一、PID 不变、1920×1080 窗口及前台焦点；每步截图和 input-journal.jsonl，不盲重试。Drag 始终释放鼠标。

坐标必须根据当前截图确认。游戏/UI/分辨率变化时不要直接重放旧计划。正常开始营业是右墙绿色按钮；库存绿色下箭头是“转移所有物品”，不是开门。脚本不直接读写存档，也不调用游戏保存方法。

## 尚未证明或尚未实现

- 没有成功食用/饮用的实际消耗证据：店铺内双击进入目标选择不等于消费。治疗消耗、标签过滤实战、其它 Mod 组合也不以本次结果代替。
- 外出回店只在取得实际场景往返证据时计入；联机大厅及搜打撤未加载，不能宣称联机回店已验证。
- 旧版本已保存的反转异形库存不会被本版无条件覆盖，旧数据迁移尚未实现。
- SaveGraph.Decode 在当前构建缺水培恢复入口时提前拒绝；不能跳过恢复链再宣布成功。
- ModuleApi、机器/配方、节点/设施、自定义工厂、角色/对话/商人等完整差距以 forge-parity.index.json 为准；本轮修复不提升覆盖数量。多语言扩展仍暂缓。

> 历史自动化命令与验收日志。不要直接执行旧槽号或旧 ExpectedVersion。[当前工具](../automation.zh-CN.md)。

> 当前 API 指南。见[统一版本与验收状态](../current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi 固定进游戏与批量验收脚本

开发工作区脚本位于 `work/`，发布包副本位于 `verification/`。脚本默认不逐步截图；以当前进程日志、新写出的诊断、版本、周目槽和具体结果字段为验收依据。首次接入新界面、检查视觉结果或异常时再截图。

## 一条命令进入测试店铺

```powershell
./work/Enter-EZapiTestStore.ps1 -EvidenceDir work/native-entry
```

流程：必要时启动游戏 → 等待实际游戏窗口与本次版本诊断 → 打开载入菜单 → 载入最近的测试档 → 等待目录应用 → 关闭每日报告 → 打开调试面板 → 导出并核对当前周目。已经处于测试店铺时复用当前进程。Steam 可能创建短命启动器进程，脚本在任何点击前等待实际窗口；首次等待默认150秒（`-StartupTimeoutSeconds` 可调），不因初始化较慢重启；绑定后 PID 变化就停止，不自动重启或重复点击。

固定路径要求 1920×1080、窗口位于屏幕原点，且最近存档是测试槽 **534**。可通过 `-ExpectedSlot` 改核对目标，不能借此假定其他存档位于相同列表行。当前入口及验收脚本默认版本为 `0.1.56-container-conditions`，均支持 `-ExpectedVersion`。脚本在载入后验证槽位和调试面板状态，错误槽位不继续运行测试动作；它不创建新局、不请求保存。测试前仍由安装流程备份用户存档。

成功时生成带时间标识的 `*-result.json`、`*-ready-diagnostics.json`、输入计划和 JSONL 操作记录。`-CaptureResult` 可加一张最终画面；失败时尝试保留异常画面。超时会留下原因，不自动重复点击或换进程重试。

## 进入一次，连续运行多组检查

```powershell
./work/Invoke-EZapiTestBatch.ps1 -EvidenceDir work/native-batch
# 也可以只选本次改动相关的组：
./work/Invoke-EZapiTestBatch.ps1 -Groups NightStock,NpcStock -EvidenceDir work/native-stock-batch
```

默认依次运行 `Inspection`（单件估值）、`NightStock`（夜间补货）、`NpcStock`（NPC 货源）、`Registration`（初始化与载入后的通用元数据）、`SaveGraph`（原生物品图恢复）、`Endpoint`（临时隐藏库存）。整批只调用一次进入脚本，绑定同一 PID、槽 534、RunId 和 Epoch；每组点击前检查面板状态，点击后等待该组报告时间和诊断文件均更新，逐项核对断言及上下文清理。不会把上一轮保留的报告当作本轮结果。底层输入计划也接收绑定的 PID，换进程立即停止。

普通流程截图数为零。每组保留诊断、输入计划和操作日志，最终汇总写入 `batch-*-result.json`；异常停止并尝试留一张截图，不重复点击。`-CaptureResult` 可选择最后留一张视觉验收图。

这些组只使用临时原生夹具与抽取检查，不验证真实 NPC 到访、货架放置、购买或保存重载。新增检查组时需补齐按钮坐标、报告字段、新报告时间和领域断言；只有新布局、视觉验收或失败排查需要识图。开发时优先复用本脚本，不再逐次手动启动、点击和看图。

> 历史验收记录（0.1.51），不作为当前 DLL 通过依据。
> 2026-10-21 已在 `0.1.51-npc-stock` 实测：复用运行中的游戏通过三组检查；再正常退出并从关闭状态全自动启动、载入和验收，共 459 项通过、0 失败、8 次固定点击、0 张截图，535 个 ES3 文件哈希不变。冷启动证据位于工作区 `work/native-entry-batch-cold-20261021/automation-verification.json` 和同目录 `batch-*-result.json`。本次 MelonLoader 远程 API 查询超时后继续正常初始化；脚本未因等待较长而重启进程。测试结束后游戏已正常退出。

> 历史验收记录（0.1.51），不作为当前 DLL 通过依据。
> 同日最终 DLL 又通过一次自动启动、载入与三组批量验收：468 项通过（425 + 9 + 34）、0 失败、常规截图为零，证据位于 `work/native-0.1.51-npc-stock-final/`。这些是已有实测记录；查阅记录本身不会再次启动游戏。后续固定流程直接调用脚本，不能为确认已知按钮位置而逐步截图。

## 单独运行工坊回归

```powershell
./work/Test-EZapiWorkshopNative.ps1 -EvidenceDir work/native-workshop
```

该脚本调用上面的入口，再依次检查列表渲染、滚动箭头、滚轮和反向滚动、进度图连线、节点详情、演示回调与关闭清理。结果来自本次导出的 `workshop.browser.lastClosedView`，避免为了读滚动位置或回调状态逐张识图。关闭记录仅保留字符串和数值，不持有原生窗口、物品或作者回调。

演示回调只改变本进程内存，不扣款、不发奖励、不保存。`-CaptureResults` 可额外保存每组最终画面；默认关闭。此脚本要求本版调试夹具和既定布局，不是任意 Mod 界面的通用点击器。

## 底层输入计划

`Invoke-EZapiNativeTest.ps1 -Action RunPlan -Plan <JSON> -EvidenceDir <目录>` 支持 Click、Move、RightClick、DoubleClick、Drag、Wheel、Key 和 Snapshot。Wheel 的 `delta` 是非零整数，范围为 -12000 到 12000；120 表示常见的一格滚轮增量。每份计划在输入前整体验证，并检查进程、焦点与窗口尺寸。

普通动作只写操作日志。需要保存某个验收结果时使用显式 Snapshot；`-CaptureSteps` 仅用于逐步排障。脚本没有安装 DLL、支付、交易或存档写入入口。

## 单件估值批量验收

`./work/Test-EZapiItemInspectionNative.ps1 -EvidenceDir work/native-inspection` 一键进入测试店铺，点击“单件估值”，用新诊断核对84组临时夹具与425项断言。默认不截图；失败保留诊断与异常截图。临时容器/子物品均游离于玩家库存，测试结束销毁，不发物或保存。

0.1.50 补货验收脚本 `Test-EZapiItemStockNative.ps1` 使用右侧“补货检查”按钮（固定1920×1080布局），核对新诊断的 `itemStockDebug`。操作真实官方事件及独立列表，不改变真实货架和玩家物品。

0.1.53新增SaveGraph与Endpoint组，沿用同一进程/周目与新报告时间校验。图恢复夹具销毁其创建的源图后恢复相同实例ID，不移动玩家物品；端点组使用临时隐藏根并在结束删除，不请求磁盘保存。

## 持久端点离店与保存脚本（0.1.53补充）

`work/Invoke-EZapiEndpointLifecycle.ps1`默认绑定槽534及用户准备的新周目。Stage依次使用Prepare、SaveNight、VerifyCold、LeaveStore、ReturnStore、Delete、SaveNight、VerifyMissing；冷启动阶段之间必须正常退出再启动。阶段先检查PID/槽/周目/版本，新结果按时间戳核对，失败停止。

LeaveStore在MORNING先开门等待，再F8原生EndAndClose关门，从左侧出口离店；新档选择不带背包并二次确认，再进入配给站。ReturnStore返回原生店铺并核对端点身份、数量、父子关系。每天的原生夜间活动次数有限，已经太晚时不反复点击，应先睡觉结算；SaveNight会关闭次日晨报，避免后续开门被窗口遮挡。

底层Drag可选heldProbeName，在鼠标保持按住时调用独立测试工具F6并核对真正dragActive=true，再释放；无探针不执行此分支。测试工具源码仅在开发工作区，正式Mods目录不得包含它。本文不把店铺中的隔离家具过滤器检查当作突袭玩法验收。

0.1.54新增Materials、Recipes、ExternalPower组，均支持ExpectedSlot（默认534）。本次命令：

```powershell
./work/Invoke-EZapiTestBatch.ps1 -Groups Materials,Recipes,ExternalPower,SaveGraph,Endpoint -ExpectedSlot 534 -ExpectedVersion 0.1.54-component-liquid -EvidenceDir work/native-component-liquid
```

403项在同一PID/周目通过，不逐步截图，不保存。材料组还要求component-mixture与component-partial-output新场景真实存在；旧报告或单纯点击不能通过。出门须先开门→等待→强制关门→点击最左出口，MORNING物理关门不代表可出门；此顺序由端点生命周期脚本实现，0.1.53已有补充验收。

## 0.1.55已验证命令与焦点修复

```powershell
./work/Invoke-EZapiTestBatch.ps1 -ExpectedSlot 534 -ExpectedVersion 0.1.55-liquid-output -Groups Materials,Recipes,ExternalPower,SaveGraph,Endpoint -EvidenceDir work/native-liquid-output
```

> 历史验收记录（0.1.55），不作为当前 DLL 通过依据。
> 实际成功记录在verification/liquid-output-native-0.1.55/batch。材料238、配方118、外电82、物品图33、端点14，共485/0。常规步骤无截图；早期焦点失败排查截图保留在work，不能说整个开发过程没有截图。底层FocusWindow短暂AttachThreadInput后激活窗口并finally解除，输入前仍核对实际前台句柄、绑定PID和1920×1080尺寸。焦点无法取得则停止，不向其他窗口点击。窗口就绪失败不自动重启游戏。

> 历史验收记录（0.1.56），不作为当前 DLL 通过依据。
> 0.1.56沿用上述同一批命令，将ExpectedVersion改为0.1.56-container-conditions。材料333、配方118、外电82、物品图33、端点14，共580/0，九个condition-*场景必须真实出现在新报告中。条件抛错场景有一条预期WARNING，用于证实故障停用；所有调试临时规则finally释放，统一机器初始化规则保留。证据见verification/container-conditions-native-0.1.56.json。

> 历史验收记录（0.1.57），不作为当前 DLL 通过依据。
> 0.1.57-material-graph最终批次：Materials458、Recipes118、ExternalPower82、SaveGraph33、Endpoint14，共705/0。全部12个graph-*模式必须出现。脚本一进程进入槽534，常规步骤不截图；游戏退出后536份ES3哈希全未变。早期Destroy诊断钩子触发退出，最终版已移除该探针，仅读取事务纯托管清理收据。见verification/material-graph-native-0.1.57.json。

0.1.58最终批次：SpriteAssets34、Materials458、Recipes118、SaveGraph33、Endpoint14，共657/0；Invoke-EZapiGuideImages.ps1按目录ID定位配方，界面7/0，2张布局QA截图，常规输入不截图。先前错误按钮坐标及配方顺序假设的失败证据原样保留；曾复现工厂地址复用误判，已修复并验证。

0.1.59最终批次：MachineRegistration88、Materials458、Recipes118、ExternalPower82、SaveGraph33、Endpoint14，共793/0；8种Required边界，3种真实零电量读回；常规流程无截图。第一轮电池初始化并非零电量的实验保留，以最终证据为准。

0.1.60最终批次：MachineRegistration124、Materials458、Recipes118、ExternalPower82、SaveGraph33、Endpoint14，共829/0；包含8种显式库存模式。脚本一次进入游戏，常规流程无截图。

> 历史验收记录（0.1.61），不作为当前 DLL 通过依据。
> 0.1.61最终DLL：七组原生863/0，Invoke-EZapiGuideLocalization.ps1实际切英语/中文验证6/0并恢复原语言，Invoke-EZapiGuideImagesCurrent.ps1图片界面7/0；4张布局QA截图已核对，常规流程无截图。首轮截图英文拆词后修正并用新进程最终DLL复验，历史证据保留。

> 历史验收记录（0.1.62），不作为当前 DLL 通过依据。
> 0.1.62：共享快照与夜间候选引用隔离修复；自动脚本验证复杂机器两晚正常保存/冷启动，移除辅助工具后仅EZapi再冷启动。原生七组863/0，磁盘状态165/0，仅save_534.es3与saves_index.es3变化。该旧阶段记录当时的覆盖状态；见[machine-persistence.zh-CN.md](../machine-persistence.zh-CN.md)。

> 历史验收记录（0.1.63），不作为当前 DLL 通过依据。
> 0.1.63：机器状态门控、旧构造/解构兼容、168项离线注册检查；七组原生867/0，旧档状态19/0；无需再次保存或辅助Mod。详见[machine-status.zh-CN.md](../machine-status.zh-CN.md)。

> 历史验收记录（0.1.64），不作为当前 DLL 通过依据。
> 0.1.64：固定商人货位/机器分类、命名表来源去重、原生已有Owned标记和预制机器空ID边界已修复。九组原生923/0，旧档状态19/0；所有536份ES3保持不变。仍待真实货架、购买和保存验证。见[npc-fixed-stock.zh-CN.md](../npc-fixed-stock.zh-CN.md)。
> 
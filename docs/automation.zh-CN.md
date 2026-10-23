> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 进入游戏与批量操作脚本

开发包的 `developer-tools/automation` 包含既有流程：

| 文件 | 用途 |
| --- | --- |
| Enter-EZapiTestStore.ps1 | 启动或绑定已有游戏进程，载入最近存档，打开开发面板并导出诊断，验证预期槽位 |
| Invoke-EZapiNativeTest.ps1 | PID/焦点/1920×1080窗口几何守卫、点击/拖拽/按键计划、截图、日志归档和正常关闭 |
| Invoke-EZapiTestBatch.ps1 | 一次进入，同一进程批量执行指定回归组；要求显式启用 regressionFixtures |
| Invoke-EZapiGuideLocalization.ps1 | 维护者原生中英展示回归；同样需要回归模式和正确的已载入测试周目 |

普通进入流程不需要额外 DLL，不依赖 `ezapi.debug.*` 目录日志。它要求先启用开发包的普通调试设置，进入脚本启动时会检查该设置。

输入助手按绑定 PID 定位唯一可见的 Unity 游戏窗口。MelonLoader 控制台与游戏可能共用 PID，并被系统误列为 MainWindowHandle；脚本在聚焦、截图和正常关闭时排除控制台。

## 使用

先准备一个可丢弃测试档并让它成为“最近载入”，确认游戏分辨率与窗口位于1920×1080、屏幕原点。菜单坐标来自050REV5-L当前布局；其它版本先核对坐标和菜单，不要盲跑。测试档应完成跳过教学；若要出门，仍须先开门、关门再从最左侧离店。

启用开发面板并重启后，在解压的开发包根目录运行：

```powershell
./developer-tools/automation/Enter-EZapiTestStore.ps1 -GameDir '你的游戏目录' -ExpectedSlot 你的测试槽编号 -ExpectedVersion '当前诊断version值' -EvidenceDir './outputs/entry-check'
```

`ExpectedSlot` 和 `ExpectedVersion` 都为必填，没有默认玩家档号或旧版版本值。进入流程会载入最近档并核对槽号；不匹配时停止，不执行回归或保存动作。加载动作本身会进入该存档，因此务必先确认最近档。已有进程需处于主菜单或预期店铺；不自动接管打开的工坊/图鉴模态窗口。

常规步骤不截图识别。可加 `-CaptureResult` 留一张成功截图，失败会尝试截图。输入前验证PID、焦点、窗口坐标，状态不对停止，不盲目重试。证据目录保留输入计划、时间和结果，不能把点击完成当成功验收。

## 回归与边界

批量回归需要在测试环境显式设置 `{"enabled":true,"regressionFixtures":true}` 并重启，才会注册测试内容及显示完整内部按钮。`Invoke-EZapiTestBatch.ps1 -ExpectedSlot <测试槽> -ExpectedVersion <诊断version> -Groups Registration,SaveGraph,Endpoint ...` 可复用同一PID顺序检查；这是开发者操作，不在安装时自动执行。回归组可能创建临时对象、改变语言或状态；应先备份可丢弃档。

中英回归通过单独的 `UserData/EZapi/regression-session.json` 显式绑定可丢弃档，不必为每个新测试档修改源码。先退出游戏，用 `Set-EZapiDeveloperTools.ps1 -GameDir '<游戏目录>' -EnableRegressionFixtures -RegressionSlot <测试槽> -RegressionRunId '<D格式周目GUID>'` 配置并重启。RunId 在诊断的 `lifecycle.run.RunId`；槽号在 `SaveSlotId`，二者必须完全匹配。

进入测试店铺并打开开发面板后运行：

```powershell
./developer-tools/automation/Invoke-EZapiGuideLocalization.ps1 -GameDir '<游戏目录>' -EvidenceDir './outputs/locales' -ExpectedSlot <测试槽> -ExpectedRunId '<周目GUID>' -ExpectedVersion '<诊断version>' -ExpectedDllSHA256 '<安装DLL的64位SHA256>'
```

脚本校验新鲜诊断、DLL、槽/周目/epoch、开发选项、窗口与同一PID；一次点击后等待，不自动重启或重复点击。报告分别包含中英的物品、机器、阅读、工坊、对话、电话、供应商临时夹具；供应商到访是夹具显式触发，不能当成自然到访、支付、交货或磁盘保存验收。结束恢复原语言；历史磁盘物品夹具只在它自己的原测试档读取，其他档跳过。深度磁盘保存/冷重载脚本及强制关门插件不作为通用进入工具分发；它们绑定内部周目并显式保存，不能误用到其他玩家。

通用输入助手保留历史 `heldProbeName` 选项，但该选项要求未分发的内部TestTools插件，普通开发者不应使用。基础点击、拖拽、进入、截图和批量框架测试不需要它。不要在电脑用于其他工作时运行输入脚本；它会聚焦游戏和移动鼠标。

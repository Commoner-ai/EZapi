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

## 使用

先准备一个可丢弃测试档并让它成为“最近载入”，确认游戏分辨率与窗口位于1920×1080、屏幕原点。菜单坐标来自050REV5-L当前布局；其它版本先核对坐标和菜单，不要盲跑。测试档应完成跳过教学；若要出门，仍须先开门、关门再从最左侧离店。

启用开发面板并重启后，在解压的开发包根目录运行：

```powershell
./developer-tools/automation/Enter-EZapiTestStore.ps1 -GameDir '你的游戏目录' -ExpectedSlot 你的测试槽编号 -EvidenceDir './outputs/entry-check'
```

`ExpectedSlot` 为必填整数，没有默认玩家档号。进入流程会载入最近档并核对槽号；不匹配时停止，不执行回归或保存动作。加载动作本身会进入该存档，因此务必先确认最近档。已有进程需处于主菜单或预期店铺；不自动接管打开的工坊/图鉴模态窗口。

常规步骤不截图识别。可加 `-CaptureResult` 留一张成功截图，失败会尝试截图。输入前验证PID、焦点、窗口坐标，状态不对停止，不盲目重试。证据目录保留输入计划、时间和结果，不能把点击完成当成功验收。

## 回归与边界

批量回归需要在测试环境显式设置 `{"enabled":true,"regressionFixtures":true}` 并重启，才会注册测试内容及显示完整内部按钮。`Invoke-EZapiTestBatch.ps1 -ExpectedSlot <测试槽> -Groups Registration,SaveGraph,Endpoint ...` 可复用同一PID顺序检查；这是开发者操作，不在安装时自动执行。回归组可能创建临时对象、改变语言或状态；应先备份可丢弃档。

中英回归目前仍有内部固定槽/RunId约束，需要维护者按夹具源码调整并重新构建。深度磁盘保存/冷重载脚本及强制关门插件不作为通用进入工具分发；它们绑定内部周目并显式保存，不能误用到其他玩家。

通用输入助手保留历史 `heldProbeName` 选项，但该选项要求未分发的内部TestTools插件，普通开发者不应使用。基础点击、拖拽、进入、截图和批量框架测试不需要它。不要在电脑用于其他工作时运行输入脚本；它会聚焦游戏和移动鼠标。

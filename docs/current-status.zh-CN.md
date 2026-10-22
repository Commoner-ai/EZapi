# EZapi 当前版本与验收状态

当前预发布 **0.1.76-preview.1**，程序集 **0.1.76**，许可证 **MIT**。玩家包仅一份 `Mods/EZapi.dll`；开发包提供同一 DLL、XML 注释、校验器、文档、示例和进入游戏脚本，调试默认关闭。框架源码在 GitHub 单独获取。

本版增加[原生 UI 积木与 AI 布局预览](native-ui-preview.zh-CN.md)，以及双语开发面板和显式测试档绑定。JSON 可重载真实原生窗口，报告实际字体、字号、对齐、换行、文字溢出和控件坐标。已有 Mod 窗口可用只读 InspectWindow 检查。

| 项目 | 当前结论 |
| --- | --- |
| 正式工程与独立 DLL 调用示例 | 编译验证 |
| 源码链接离线测试 | 60 组；另有 25 项 CLI 独立进程检查 |
| 参考功能覆盖 | 39 组实现；不代表全部开发完成或实机验收 |
| 当前 DLL 局部原生 UI 和中英回归 | 421项通过、0项失败；见 scopedNativeObservations |
| 全框架、组合 Mod 和当前 DLL 保存重载 | **仍待验收** |

准确 DLL 哈希与验收范围统一读取 [release-status.json](release-status.json)。几何与字体检查不代表机器加工、任意 Mod 兼容或保存正确。此次测试使用显式绑定的可丢弃档与临时窗口，不请求保存。

- [AI 开发入口](AI-START-HERE.zh-CN.md)、[公开 API 完整签名](public-api.index.json)、[任务索引](developer-workflows.index.json)、[源码索引](source.index.json)。
- [安装与可选调试](install-and-debug.zh-CN.md)、[源码构建](build-from-source.zh-CN.md)、[游戏自动化](automation.zh-CN.md)。
- [可选原版 Tooltip 规范](tooltip-style.zh-CN.md)、[物品美术参考](item-art.zh-CN.md)、[兼容性边界](compatibility.md)。

[历史目录](history/README.md)及旧版发布说明只证明对应 DLL。0.1.74 的王尔德界面反馈、0.1.75 的中英回归、0.1.72 的 415 项检查均不得转移为本版证据。私人原始日志、存档和内部审计资料不分发。

开发包保留 96 张可选原版美术参考与中英 AI 提示词；原版美术保留独立权属，不按 EZapi MIT 授权。

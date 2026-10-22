# 当前兼容性边界

适配基线为 Probably Stolen Demo050REV5-L、MelonLoader IL2CPP .NET6。运行 DLL 目标 .NET6；离线校验器目标 .NET8。不依赖 Forge DLL，不分发游戏、加载器或 Unity 程序集。

- 版本敏感入口按当前游戏程序集签名和构建标识检查；未知构建或缺少必要接口时关闭对应能力。运行门控为 true 只表示入口条件具备，具体操作仍需检查结果。
- 物品、标签、Tooltip 和目录优先使用官方事件与工厂。Harmony 的精确安装目标见[补丁索引](patches.index.json)，不以早期版本的“补丁总数”描述当前实现。
- JSON 内容包和 DLL 注册共享稳定 ID 与 owner 隔离。Queued/Accepted 不等于原生 Applied，Applied 不等于玩家交付或成功保存。
- 当前语言跟随游戏设置，CSV 支持现有官方语言及回退。物品的 localized 字段、自定义机器窗口、对话和阅读等入口见[当前语言指南](game-language.zh-CN.md)。早期“自动物品翻译尚未实现”的记录已过时。
- 读档、新局、场景切换与对象销毁可能使句柄失效。重新取得当前快照，不跨回调保存原生对象，不自动重试不明确的支付、搬运或保存结果。
- 外出按钮只管入口 UI 和点击分发；目的地、次数、物品同步及保存由调用 Mod 执行。
- 与其他 Mod 共用目标时仅修改自己的资源，不绕过原生准入。布局、交易、跨天和保存组合仍须在目标游戏与 Mod 组合中验收。

**当前 DLL 尚未完成实机验收。** 完整状态见[当前版本](current-status.zh-CN.md)；[早期兼容性沿革](history/compatibility.md)仅供历史研究。

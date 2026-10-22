> 历史记录 / Historical record：只适用于标题所列旧版本；不代表当前 DLL 验收。[当前状态](../current-status.zh-CN.md)。

# EZapi 0.1.38 — Workshop API 验收记录

- 构建日期：2026-10-21
- 范围：Workshop 页签/进度链注册、快照校验、列表与图谱窗口、动作前刷新、单次作者回调，以及只读调试预览。
- Release 构建：EZapi、示例 Mod、内容校验器均 0 警告、0 错误。
- 离线 API 检查：Start 15/15，Workshop 19/19。
- 内容校验：示例物品、机器布局、配方指南、掉落表、内容包依赖和本地化均通过。
- 包核对：包含 Workshop 源码、示例、离线检查工程；Workshop 源码和索引与本轮构建输入一致。DLL SHA256 见同目录 `EZapi-0.1.38-workshop-api-final-verification.json`。
- 游戏内效果：本记录不宣称已验证。Workshop 自有窗口只展示只读快照；资源扣除、解锁、奖励交付与保存仍由 Mod 回调负责。

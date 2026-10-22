# EZapi 0.1.39 — RuntimeCapabilities 验收记录

- 构建日期：2026-10-21
- 范围：`RuntimeCapabilities.Current`、20 项只读快照、主线程失败关闭、Assembly-CSharp SHA256 指纹、诊断导出及 Forge parity 对照更新。
- 官方资料：本地官方 `ModdingGuide.zh-CN.md`、`APIChanges.zh-CN.md` 和 LearnerMod 示例的 SHA256 均与 `official-sources.index.json` 一致；官方 `ModHook` 订阅时机与本机 `Assembly-CSharp.dll` SHA256 已核对。能力快照属于 EZapi 自有诊断 API，官方文档没有同名接口。
- Release 构建：EZapi、示例 Mod、内容校验器均 0 警告、0 错误。
- 离线 API 检查：Start 15/15，Workshop 19/19，RuntimeCapabilities 10/10。
- 内容校验：示例物品、机器布局、配方指南、掉落表、内容包依赖和本地化均通过。
- 包核对：发行包包含 RuntimeCapabilities 源码、API/能力/源码索引、指南和离线测试工程；DLL/示例/校验器 SHA256 见输出目录及包根目录中的 `EZapi-0.1.39-runtime-capabilities-verification.json`。
- Forge parity：39 组中 24 组实现、13 组部分实现、2 组缺失；完整目标仍未完成。
- 游戏内效果：本记录不宣称已验证。能力为 `true` 只表示当时 EZapi 门控就绪，不代表原生操作成功、保存完成或兼容其他 Mod。

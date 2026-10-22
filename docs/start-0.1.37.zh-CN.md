# EZapi 0.1.37 Start API 验收记录

## 本次交付

- 增加 `StartApi.RegisterStart` 的整数和官方 `NewGameData.StartType` 重载，以及 `Snapshot()`。
- 覆盖 owner/ID 命名空间校验、当前枚举成员校验、初始化窗口和线程限制、相同映射幂等、ID 冲突及排序只读快照。
- 加入 `StartRegistrationView`、C# 示例、中文指南和机器可读 API/parity 索引。
- 该 API 只保存“Mod 开局身份 → 官方 StartType”映射；不创建游戏菜单项、不发放开局物品、不改资金或开局效果。
- parity 统计更新为 22 组实现、14 组部分实现、3 组未实现；完整 Forge 覆盖目标仍未完成。

## 已完成检查

- `work/ezapi-start-api-tests/StartApiTests.csproj`：15/15 通过。测试链接实际 `StartApi.cs` 与 `Registration.cs`，使用当前官方枚举值的离线桩；覆盖注册、幂等、冲突、无效输入、时机/线程和快照行为。
- `Build-EZapi.ps1` Release 构建：EZapi、示例 Mod、内容校验器均成功，0 警告、0 错误。
- 示例物品、机器布局、配方指南、掉落表、完整内容包和本地化校验通过。
- API、parity 与 Forge 公共契约 JSON 均可解析；成员统计为 22/14/3。
- 构建包中的 Start API 源码、文档、示例和离线测试工程均已检查。

## 尚未验收

本轮未进行游戏启动、DLL 加载、开局菜单或新开局行为验收。因此不能据本次结果声称新开局玩法已经接入或生效。当前环境缺少原生测试流程要求的 `probably-stolen-native-test/SKILL.md`，本轮未替换游戏目录 DLL 或启动游戏。

## 构建产物

- 包目录：[`EZapi-0.1.37-start-api`](../outputs/EZapi-0.1.37-start-api/)
- DLL：[`EZapi.dll`](../outputs/EZapi-0.1.37-start-api/Mods/EZapi.dll)
- SHA-256：`dde463fd17a33f6ea04a6b0ce10abee91ee10f6763fd2add7dd8f7bb9516825d`

> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 安装与可选开发工具

此版为 0.1.73-preview.3。运行 DLL 的程序集版本为 0.1.73，调试功能开关在启动时读取，修改后重启游戏。

| 下载 | 用途 | 内容 |
| --- | --- | --- |
| EZapi-0.1.73-preview.3-game.zip | 玩家和仅需要前置的用户 | 仅一份 Mods/EZapi.dll，默认不显示调试 |
| EZapi-0.1.73-preview.3-developer.zip | Mod作者和AI | 同一份前置、可选调试配置、校验器、完整指南、索引和样例；不需要额外游戏插件 |

## 玩家安装

先安装游戏适用的 MelonLoader IL2CPP .NET6。游戏退出后，把 game.zip 内的 `Mods/EZapi.dll` 放到游戏的 `Mods`。也可在解压目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./Install-EZapi.ps1 -GameDir '你的游戏目录'
```

安装脚本核对 DLL 哈希并将旧 DLL 备份至 `UserData/EZapi/Backups`。保留现有 Mod、内容包、存档和调试偏好。普通运行默认没有 EZapi 调试按钮，不注册回归测试物品，也不自动安装示例内容包。未知游戏构建的敏感功能会关闭自身门控。

## 启用或关闭可选工具

先装游戏包，再解压开发包，在开发包的 `developer-tools` 目录中操作。退出游戏后执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./Set-EZapiDeveloperTools.ps1 -GameDir '你的游戏目录'
powershell -NoProfile -ExecutionPolicy Bypass -File ./Set-EZapiDeveloperTools.ps1 -GameDir '你的游戏目录' -Disable
```

也可将开发包 `developer-tools` 内的 `UserData` 复制到游戏目录；已有配置建议用脚本以保留备份。配置为 `UserData/EZapi/developer-tools.json`：

```json
{"enabled": true, "regressionFixtures": false}
```

缺文件、非法 JSON、重复/未知字段、非布尔值或文件超限都会关闭开发工具并记录原因。配置不与存档绑定，不会设置游戏语言。

启用后左上角出现“EZapi 调试”：导出诊断、复制物品 ID、只读库存与资源、单件估值、打开配方图鉴、分页查看已登记物品和手动生成一件物品。生成会改变当前游戏内库存，之后正常保存会保留，建议使用测试档。诊断位置为 `UserData/EZapi/diagnostics.json`，排查时附上相同启动过程的 `MelonLoader/Latest.log`；分享前检查日志和存档身份信息。

## 内部回归夹具

`regressionFixtures` 默认为 false，公开开发工具预设也不会打开它。只有维护者专用测试环境才应同时启用两个选项。此模式会注册 `ezapi.debug.*` 内容，并提供冻结、跨夜、语言切换和持久测试夹具，不能当作普通玩家工具。

历史 `EZapi.TestTools.dll` 是内部自动化插件，绑定特定存档/周目，并带强制关门和显式保存测试，不随上述安装包发行；更新前在游戏退出时将旧插件移出 Mods。仅关闭配置不能卸载另一个 DLL。

关闭开发工具不会删除已有测试物品或玩家数据。需要清理测试内容时先在测试档按游戏规则处理；不要直接删除真实存档的标签。卸载前置前，确认依赖它的 Mod 和存档内容已按其作者说明处理。

## 开发者入口

开发包中的样例包放在 `examples/ContentPacks`；仅当需要示例时复制到 `Mods/EZapiPacks`。DLL 样例和 JSON 包不要重复安装同一组示例物品。离线校验器需要 .NET8；游戏内前置需要 MelonLoader 的 .NET6。从 [AI 入口](AI-START-HERE.zh-CN.md)、[开发路径索引](developer-workflows.index.json) 和 [完整公开签名](public-api.index.json) 开始。

本次开关和封装改变了 DLL 哈希；此前的 415 项中英/保存重载报告保留为历史证据，不能冒充新 DLL 的完整实机验收。

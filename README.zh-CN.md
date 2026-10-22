# EZapi

[当前版本与验收状态](docs/current-status.zh-CN.md) · [Historical records / 历史资料](docs/history/README.md)

EZapi 是 Probably Stolen 的独立 Mod 前置框架，提供 DLL API、JSON 内容包、离线校验器、可编译示例及 AI 可读索引。优先使用游戏官方接口，并在当前游戏构建中集中处理兼容适配。

**当前预发布：0.1.73-preview.4，程序集版本0.1.73。** 完整目标尚未完成。

本仓库按 **MIT 许可证开源**：框架、离线校验器、测试、构建脚本、文档、接口索引和调用示例均可查看、修改与分发。旧私有仓库的历史不迁入，游戏程序集及私人数据不分发。玩家从 Releases 下载安装包，源码开发见[构建说明](docs/build-from-source.zh-CN.md)。

## 开发从这里开始

- [AI 开发入口](docs/AI-START-HERE.zh-CN.md)：任务选择、JSON/DLL路径、查询与写入边界。
- [全部公开签名索引](docs/public-api.index.json)：从当前DLL生成所有公开声明，重载独立编号并附调用说明；准确数量与程序集哈希见索引的 `counts`、`dllSha256`。
- [常见 API 任务索引](docs/API.index.zh-CN.json)、[领域索引](docs/source.index.json)、[错误索引](docs/errors.index.json)。
- [当前游戏语言指南](docs/game-language.zh-CN.md)、[English guide](docs/game-language.en.md)、[语言任务索引](docs/game-language.index.json)。
- [知识库升级/服务/前置条件扩展](docs/progression-requirements.index.json)、[物品版本化数据](docs/item-data.index.json)、[Forge公共能力对照](docs/forge-parity.index.json)。
- [本次版本与验证范围](docs/releases/0.1.73-preview.4.md)。

## 两个下载包

[GitHub Releases](https://github.com/Commoner-ai/EZapi/releases) 提供：

| 包 | 用户 | 内容 |
| --- | --- | --- |
| game.zip 游戏包 | 玩家 | 仅 Mods/EZapi.dll；默认关闭调试、不装示例或测试插件 |
| developer.zip 开发包 | 作者和AI | 同一份前置、可选调试配置、校验器、文档、索引和示例 |

退出游戏后复制游戏包的 Mods/EZapi.dll，或执行 Install-EZapi.ps1 -GameDir '<游戏目录>'。只安装一份前置。玩家不需要再安装任何EZapi调试DLL。开发包工具目录的DLL用于离线开发，不要复制到游戏Mods。

需要调试时，在开发包 developer-tools 目录运行 Set-EZapiDeveloperTools.ps1 -GameDir '<游戏目录>'；加 -Disable 关闭，重启生效。见[安装与调试](docs/install-and-debug.zh-CN.md)。示例放在examples，按需安装；内部EZapi.TestTools.dll不随玩家包发行。

开发包还包含[自动进入游戏与批量操作工具](docs/automation.zh-CN.md)，位于 `developer-tools/automation/`。启用调试后运行 `Enter-EZapiTestStore.ps1 -GameDir '<游戏目录>' -ExpectedSlot <测试槽号>`；输入助手和批量回归脚本一并提供，普通进入流程无需额外DLL。

当前验证基线是 Probably Stolen Demo050REV5-L、MelonLoader IL2CPP .NET6；离线工具使用.NET8，索引生成使用Python3。游戏程序集需来自自己的安装，不随包分发。

## 常用离线命令

在发布包根目录运行：

```powershell
dotnet tools/ezapi-validate/ezapi-validate.dll --help
dotnet tools/ezapi-validate/ezapi-validate.dll --pack Mods/EZapiPacks/author.example
dotnet tools/ezapi-validate/ezapi-validate.dll --packs Mods/EZapiPacks
dotnet tools/ezapi-validate/ezapi-validate.dll --requirements docs/examples/requirements.json
```

退出码0是通过，1是内容/文件问题，2是命令用法错误。离线通过不表示物品已经进入原生目录、当前游戏资格成立或存档恢复成功。编译样例在 `src/EZapi.Sample/`。

框架实现、离线测试和构建工程已按 MIT 开源，源码从仓库获取；二进制开发包保持精简。作者可引用安装好的 DLL 开发，最小独立工程可直接编译：

```powershell
dotnet build docs/examples/minimal-dll/SimpleMod.csproj -c Release -p:GameDir='<游戏目录>'
```

新增[自适应外出地点按钮](docs/departure-buttons.zh-CN.md)，源自用户搜打撤入口布局；仅提供按钮与回调分发，目的地逻辑由Mod实现。

## 当前边界

Forge参考功能覆盖为39/39组；这是功能对应清点，不承诺Forge二进制ABI兼容。EZapi使用自己的实现，并不依赖Forge DLL。

语言跟随游戏当前设置，CSV可保留更多语言支持。当前DLL完成59组离线回归、25项CLI检查和124项新增外出按钮检查；尚未进游戏验收。历史DLL的语言与保存结果不转移到当前版本。验证范围与哈希见发布附件的package-verification.json和包内verification.json。

源码与二进制采用 MIT 许可证，保留版权声明即可使用、修改与分发，见 LICENSE；第三方依赖说明见 THIRD-PARTY-NOTICES.md。

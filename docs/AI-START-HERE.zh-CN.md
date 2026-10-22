# EZapi：开发者与AI入口

当前预发布 **0.1.73-preview.2**，程序集版本0.1.73。公开发布仓库：[Commoner-ai/EZapi](https://github.com/Commoner-ai/EZapi)。框架、离线校验器、测试和构建工程已按 MIT 开源，见[源码构建](build-from-source.zh-CN.md)；开发普通 Mod 仍只需公开 DLL 接口。

## 从任务到示例

- [工作流索引](developer-workflows.index.json)：先按目标选择JSON内容包或DLL API。
- [全部公开签名](public-api.index.json)：当前DLL的类型、重载、参数、默认值和XML注释；不会包含方法实现。
- [常见API](API.index.zh-CN.json)、[领域索引](source.index.json)、[错误索引](errors.index.json)：选择调用阶段、失败处理和示例。仓库中的领域索引提供实现源码路径；二进制 SDK 中给出模块名与 GitHub 源码入口。
- [自适应外出按钮](departure-buttons.zh-CN.md)：在原生离店地图添加入口，自动寻找空位；目的地、次数消耗、库存和保存由调用Mod负责。
- [安装与调试](install-and-debug.zh-CN.md)、[自动进入游戏](automation.zh-CN.md)、[English entry](AI-START-HERE.en.md)。

## 先下载开发包

从[Releases](https://github.com/Commoner-ai/EZapi/releases)下载developer.zip并解压。该包包括同一份EZapi.dll、XML注释、离线校验器、schema、文档、调用示例和自动化工具。示例不会自动安装，调试默认关闭。玩家只需game.zip的一份Mods/EZapi.dll。

最小JSON包放在 `Mods/EZapiPacks/<packId>/`，至少含pack.json和items.json。看[内容包指南](content-packs.zh-CN.md)、[清单schema](../schemas/pack.schema.json)、[物品schema](../schemas/items.schema.json)及[样例](../src/EZapi.Sample/Content/items.json)。在解压的开发包根目录执行：

```powershell
dotnet tools/ezapi-validate/ezapi-validate.dll --pack Mods/EZapiPacks/author.example
dotnet tools/ezapi-validate/ezapi-validate.dll --packs Mods/EZapiPacks
dotnet tools/ezapi-validate/ezapi-validate.dll --help
```

离线校验不启动游戏，不生成物品，不证明原生目录已应用或存档恢复成功。

## 最小DLL调用方

复制[最小独立工程](examples/minimal-dll/SimpleMod.csproj)和[Mod.cs](examples/minimal-dll/Mod.cs)，改作者命名空间及稳定ID。在自己的项目中运行：

```powershell
dotnet build ./SimpleMod.csproj -c Release '-p:GameDir=<你的游戏目录>'
```

先安装EZapi与MelonLoader；只把生成的调用方DLL复制到游戏Mods，不再复制一份EZapi或MelonLoader。外出按钮也有[独立工程](examples/departure-dll/DepartureMod.csproj)，在开发包根目录编译，默认引用包内DLL。仓库网页和 preview.2 标签的 GitHub 自动源码 ZIP 提供框架及工具源码；可直接使用的二进制工具在 Releases 开发包中。

## 语言、生命周期与结果

语言跟随游戏当前设置。`GameLanguage.Current`读取语言，内联双语对象或CSV目录解析文字；CSV可支持更多语言并按声明回退。不要另设EZapi语言开关，不自动翻译作者字面量。见[语言指南](game-language.zh-CN.md)。

注册Accepted/Queued只表示声明被接受；还需检查Applied、具体操作结果及原生状态。查询回调必须只读。不要跨读档、场景或回调缓存原生对象/旧身份；不要自动重试不确定的支付、发物品和保存操作。示例业务数值由Mod作者决定。

## 当前证据

本DLL完成59组离线回归、25项独立CLI检查，新增外出按钮124项源码链接检查；653个直接游戏方法引用匹配，静态可达Unity包装方法未发现unstripping缺失。**本版本尚未进行实机验收**，历史语言/保存记录仅对应其当时DLL，不能作为当前二进制的实机通过证据。下载包内verification.json与package-verification.json记录范围和哈希。

Probably Stolen官方文档是优先参考：[probably-stolen-modding](https://github.com/dragonpan2/probably-stolen-modding)。具体游戏版本签名与接口自己的失败结果仍是准绳。EZapi不依赖Forge DLL，不承诺Forge二进制兼容。

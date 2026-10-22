# EZapi 实现来源与参考边界

EZapi 是独立框架。Forge 0.6.36 的公共功能清单用于发现常见需求、核对覆盖范围；不把 Forge 的内部实现、反编译方法体、注释、资源或标识作为 EZapi 的实现模板，不加载、转发或分发其程序集。公共功能和接口相似不能单独证明复制，运行不依赖也不能单独证明没有复制。

优先依据官方 `probably-stolen-modding` 指南、示例和当前游戏公开接口设计；使用中文注释、显式失败结果、JSON 和 AI 索引服务作者。来源版本与当前构建差异见 `official-sources.index.json` 和 `official-api-map.zh-CN.md`。第三方资料仍属于其原作者，新增外部代码或资源应先核对授权并保留所需署名；本记录不授予第三方材料任何新许可。

## 0.1.47 实际检查

- `PortraitSprites.cs` 由 EZapi 新增，使用当前游戏公开的 SpriteDict、字典和 GetSprite；键派生、冲突拒绝、读回、回滚与停止清理在此单点实现，未调用 Forge。
- 查看正式项目编译引用并核对 EZapi.dll 的程序集依赖，没有 Forge/Nicokobo 编译或程序集依赖；元数据证据见 `verification/assembly-references-0.1.47.json`。
- 源码中出现的 Forge 字样目前是范围或独立性说明；本检查没有对全部历史方法体做来源追溯或代码相似性鉴定，不能据此出具“绝无抄袭”或法律保证。
- 发布包只分发本项目产物；Forge DLL 和本机官方 Git 克隆不随包分发。功能覆盖表属于研究记录，不表示 EZapi 与 Forge 二进制兼容。

本轮来源与范围记录：`PortraitSprites.cs` 对应官方指南的自定义客户图像需求，具体适配入口通过本机构建签名核对，见 `verification/portrait-native-contract-0.1.47.txt`；离线故障分支以真实 EZapi 源码加原生替身验证，游戏内证据另行记录。

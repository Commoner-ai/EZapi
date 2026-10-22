> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi 公共帮助器

这些接口用于简化内容 Mod 的诊断、独立窗口输入和旧 ID 适配。实现独立，不依赖 Forge。完整可编译用法在 `src/EZapi.Sample/PublicHelperExamples.cs`。

```csharp
using EZapi;
var log = new ModLog("author.tools", LogLevel.Info);
log.Info("机器已注册 / Machine registered");
log.Write(LogLevel.Debug, () => "仅调试时计算的明细");
```

每个 `ModLog` 独立保存作者 ID、阈值与输出接收器。`Debug / Info / Warn / Error` 按阈值过滤，`Callback` 为 Debug 级回调跟踪，`Off` 关闭本实例输出。默认写入 MelonLoader 对应级别；需要文件或测试记录时，通过构造参数 `sink: entry => ...` 接收不可变 `LogEntry`。框架不替换游戏日志配置，不记录密码，不自动捕获玩法异常。

`LogLevels.TryParse` 支持英文级别和中文“调试/信息/警告/错误/关闭”。失败返回 `false`，输出值为 Info。不要忽略返回值。非法枚举抛错。惰性 `Write(level, Func<string>)` 只在该消息可见时计算；格式化器和接收器同步运行，其异常由调用者处理。

窗口使用每实例一个 `WindowInputCapture`。打开/关闭时分别传入当前 `Time.frameCount` 和鼠标是否按住；自己的输入路由用 `BlocksPointer(frame, pointerInside, mouseHeld)` 决定是否跳过底层指针事件。只在面板内开始的手势会持续捕获到释放帧，移动到外面或关闭窗口也不会让释放事件点击店铺。面板外开始的手势经过窗口不会被永久接管。重复 Close 不创建新捕获；同帧重复查询保留释放屏障。

这只是托管状态工具，不自动安装全局补丁、创建透明 UI 或截获键盘。路由方需要组合原生拒绝和多个窗口的 OR 结果。失焦、场景卸载和读档时对自己的实例调用 `Reset()`；不会销毁游戏或其他 Mod 的拖拽对象。帧号必须非负且不倒退，新时间线先 Reset。

```csharp
var id = ManufacturingTerminal.NormalizeItemId(
    incomingId, new[] { "author.tools.old_press" });
```

终端规范化去除 ID 首尾空白，保持未知有效 ID 不变。调用者显式传入的别名才映射为 `ManufacturingTerminal.ItemId`，区分大小写，最多 64 个，重复或无效别名整批拒绝。函数不保存别名、不接管其他框架 ID、不改写存档。历史物品的真实迁移仍由内容作者设计。

离线套件直接链接这些实现，覆盖级别矩阵、中文/英文解析、惰性格式化、接收器异常、拖动/关闭/释放/重置事件序列、多实例隔离和别名边界。MelonLogger 的离线接收器是显式替身，不能据此宣称窗口实机交互验证；实机通用注册按钮会补跑帮助器及真实日志后端烟雾检查。

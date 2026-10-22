> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi 只读存档回读

`SaveReadbackApi` 是 Forge SaveReadback 的独立 EZapi 实现。它只读取文本快照，不调用游戏保存、不写文件、不恢复原生物品，也不把 `Readable` 当成“存档结构正确”或“事务已完成”。需要写入或恢复物品图时，分别使用 `RunData`、`SaveGraphApi` 并遵守它们更严格的边界。

## 最短用法

```csharp
using var result = SaveReadbackApi.ReadFile(path);
if (result.Status != SaveReadStatus.Readable)
    throw new InvalidDataException(result.Reason ?? result.Status.ToString());
var store = SaveReadbackApi.RequireProperty(result.Root, "playerStore");
var value = SaveReadbackApi.RequireProperty(store, "value");
var slot = SaveReadbackApi.RequireProperty(value, "saveSlotId").GetInt32();
var runId = SaveReadbackApi.RequireProperty(value, "runID").GetString();
```

`Root` 只在 `SaveReadResult` 未释放时有效；需要跨 `using` 保存的子树必须显式调用 `Clone()`。`RequireProperty` 拒绝缺失和重复属性。`TryGetUniqueProperty` 对缺失属性返回 `true`、`present=false`，对重复或非对象返回 `false`。

## 状态与限制

| 状态 | 含义 |
| --- | --- |
| `Readable` | 有界 UTF-8 文本已解析；不代表游戏结构完整 |
| `Missing` | 文件不存在 |
| `TooLarge` | 超过 `maxBytes` |
| `ChangedDuringRead` | 读取前、读取中或解析前后文件长度/时间戳变化 |
| `Malformed` | UTF-8、JSON、深度或已知 ES3 文本格式失败 |
| `Unavailable` | 文件被锁、权限或系统读错误 |
| `Invalid` | 路径或限制参数无效 |

默认上限为16 MiB、嵌套256层。`ReadFile` 前后检查文件元数据，不能防止读取完成后才发生的下一次写入。Windows 下文件打开期间使用 `FileShare.Read`：允许其它读取，拒绝重叠写入/删除，因此应在保存完成后主动调用，避免高频轮询；它不会重试或保持文件句柄。`ReadBytes` 会复制调用者数组；修改原数组不会影响结果。`Parse` 是低层抛异常入口，调用者必须释放返回的 `JsonDocument`，不适合直接处理不可信大输入。

当前游戏存档是 JSON 文本，包含整数对象键和少量 ES3 风格的原始控制字符/非标准转义。EZapi 只规范化这些已知格式：整数对象键加引号、字符串控制字符转义、无效反斜杠按字面保留；随后仍由 `System.Text.Json` 严格解析。不会接受注释、尾逗号、`NaN` 或任意 JavaScript 语法。解析会保留重复属性；需要结构化读取时使用 `TryGetUniqueProperty` 或 `RequireProperty`，它们会拒绝重复键。属性名比较按解码后的大小写敏感 Ordinal 规则。

## 验证范围

真实存档只读测试、SHA 前后核对、临时文件锁/增长/截断/坏 UTF-8/深度/重复键测试在 net6.0 与 net8.0 各有 1,737 项通过，读取本机 534 个 `.es3` 文件并逐个核对 SHA 前后不变；游戏内按钮检查 9 项通过。命令行校验器支持 `--read-save <file.es3>`。它不能证明保存事务已提交、外部 Mod 数据可迁移或 SaveGraph 原生恢复入口可用。

## 命令行和能力声明

能力 ID 为 `save.readback`，与 `FrameworkInfo.GetCapabilities()` 一致。CLI 为独立 .NET 8 工具：

```powershell
.\tools\ezapi-validate\ezapi-validate.exe --read-save 'C:\path\save_532.es3'
```

输出只含 valid、kind、status、ByteCount、Reason、rootKind、scope 元数据；不输出存档全文。API 与可编译示例见 `API.index.zh-CN.json` 和 `../src/EZapi.Sample/SaveReadbackExamples.cs`。

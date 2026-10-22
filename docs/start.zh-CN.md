# Start API：开局身份映射

`StartApi` 为 Mod 提供一个带 owner 和稳定 ID 的登记表，将 Mod 的开局身份 ID 映射到游戏官方的 `Il2Cpp.NewGameData.StartType`。它对齐 Forge `ForgeStartApi.RegisterStart/Snapshot` 的公共注册能力。

**当前能力边界：这只是身份映射登记。** EZapi 不会因此创建新游戏菜单项、改写原版开局选项、发放开局物品、修改资金或改变任何开局效果。要扩展这些玩法，需要另行实现并验收相应的游戏接入能力。

## 注册

在 EZapi 初始化线程、开局注册窗口关闭前调用。建议使用游戏枚举重载，避免把数字写进 Mod：

```csharp
using EZapi;
using EZapi.Registration;
using Il2Cpp;

SubmitResult result = StartApi.RegisterStart(
    ownerId: "example.mod",
    startId: "example.mod.apprentice",
    nativeStartType: NewGameData.StartType.APPRENTICE);

if (!result.Accepted)
    LoggerInstance.Warning($"开局身份未登记：{result.Reason}");
```

也提供 `int` 重载，方便外部数据驱动的调用；它会用当前游戏程序集中的 `NewGameData.StartType` 校验值。当前本机参考程序集枚举为：

| 数值 | 官方枚举 | 数值 | 官方枚举 |
| ---: | --- | ---: | --- |
| 0 | `NONE` | 7 | `ORGAN_TRADER` |
| 1 | `WATER_MERCHANT` | 8 | `PHARMACIST` |
| 2 | `APPRENTICE` | 9 | `STREET_FOOD_VENDOR` |
| 3 | `SCAV` | 10 | `FARMER` |
| 4 | `FENCE` | 11 | `GUNSMITH` |
| 5 | `MOONSHINER` | 12 | `ROCK_BOTTOM` |
| 6 | `DRUG_DEALER` | 13 | `RANCHER` |

该列表来自当前游戏参考程序集的枚举检查，不应假设其他游戏版本永远使用相同数字。优先在编译时引用官方枚举。

## 返回值与查询

- `Accepted`：新映射已登记。
- `AlreadyPresent`：owner、ID 和原生类型都与已有登记相同；重复调用是幂等的。
- `Conflict`：同一 `startId` 已由不同 owner 或不同原生类型占用。
- `Invalid`：owner/ID 格式或归属错误、枚举值无效、调用线程错误，或注册窗口已关闭。检查 `Reason` 中的 `EZAPI_START_*` 代码。

`StartApi.Snapshot()` 在 EZapi 初始化线程返回按 owner 和 start ID 排序的只读快照。`StartRegistrationView.NativeStartType` 保存官方枚举的数值；它不代表游戏已经应用该映射。

完整示例见 [`StartExamples.cs`](../src/EZapi.Sample/StartExamples.cs)，机器可读契约见 [`API.index.zh-CN.json`](API.index.zh-CN.json) 与 [`forge-parity.index.json`](forge-parity.index.json)。

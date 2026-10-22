> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# 机器状态查询（）

`MachineRegistrationApi.Snapshot()` 仅在游戏主线程调用。返回的是调用时的托管快照；状态变化后重新查询。

| 字段 | 含义 | 不能据此推断 |
| --- | --- | --- |
| Status | 当前物品目录应用状态 | 已具备加工所需的材料、电量或空间 |
| RuntimeInstalled | 全局机器运行时及夜间生命周期就绪 | 该机器启用了夜间加工 |
| ProcessingAvailable | 目录 Applied，且配方可编辑、库存准入和冻结保护可用 | 此次加工必定成功 |
| NightlyInstalled | RuntimeInstalled 且定义 EnableNightly=true | 今晚有足够资源或必定加工 |

关闭夜间加工的机器可以同时具有 RuntimeInstalled=true、ProcessingAvailable=true、NightlyInstalled=false。手动加工不依赖夜间生命周期；运行时未就绪时，手动加工能力应单独判断。加工期间配方目录不可编辑，此时 ProcessingAvailable=false。

```csharp
// 在已初始化的游戏主线程，例如你的按钮回调中查询。
foreach (var view in MachineRegistrationApi.Snapshot())
    MelonLoader.MelonLogger.Msg($"{view.MachineId}: runtime={view.RuntimeInstalled}, manual={view.ProcessingAvailable}, nightly={view.NightlyInstalled}");
```

兼容性：保留原十参数构造函数和十字段解构。RuntimeInstalled 为新增 init 属性；旧代码手动构造的视图默认 false，不代表框架实际未就绪。使用 Snapshot 查询真实状态。

实际证据：四种目录状态、全局运行时与夜间开关组合、准入/冻结/配方编辑门控、快照不变性、旧构造/解构、JSON和线程边界，共168项离线检查。游戏内机器注册128项通过，七组总计867项通过；旧测试存档读取19项通过。完整公共行为审计仍在继续，不能据此宣布整组完成。

参见 [AI索引](machine-status.index.json)、[English](machine-status.en.md)、验证报告（历史标识 `verification/machine-status-native-0.1.63.json`；原件未公开）。

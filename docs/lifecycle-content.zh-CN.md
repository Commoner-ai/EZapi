> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

> 0.1.62：Items不再在订阅者间共享旧库存快照；同一回调内删物后仍需重新扫描。见[跨夜和重载](machine-persistence.zh-CN.md)。

# 生命周期与内容应用通知

EZapi 0.1.14 把 Forge 的 10 个生命周期阶段接到当前游戏已确认的 7 个原生方法。`GameEvents` 的 18 个官方 ModHook 事件仍然保留，不能用名称相近的事件替代：

| 阶段 | 原生入口 | 时机 |
| --- | --- | --- |
| BeforeLoad / AfterLoad | `PlayerStore.LoadGame()` | 前缀 / 原方法成功后的后缀 |
| BeforeNight / AfterNight / NightAttemptFinished | `PlayerStore.EndNight()` | 前缀 / 原方法成功后的后缀 / 后缀（原方法被跳过也执行，异常时不执行；上下文须有效） |
| BeforeNightServices | `ModHook.FireOnHandlingNightlyServicesEarly()` | 前缀 |
| AfterNightServices | `ModHook.FireOnHandlingNightlyServicesLate()` | 原方法成功后的后缀 |
| AfterSleep | `ModHook.FireOnGoingSleepLate()` | 原方法成功后的后缀 |
| AfterEndDay | `PlayerStore.EndDay()` | 原方法成功后的后缀 |
| AfterGameLoadedLate | `ModHook.FireOnGameLoadedLate()` | 原方法成功后的后缀 |

订阅必须在 EZapi 初始化主线程执行，`ownerId + callbackId` 全局唯一，优先级支持完整 `Int32` 范围，数值越小越早；相同优先级按 owner 和 ID 排序。缺少 7 个必需入口的任意一个时，`ezapi.lifecycle` 整组关闭。`GetRuntimeStatus().Ready` 只表示签名、Interop 元数据和本组 Harmony owner 完整，不代表游戏行为已经验证。

```csharp
using var lease = LifecycleApi.Subscribe(
    "my.mod", "after-night", LifecyclePhase.AfterNight,
    context => {
        if (!context.IsCurrent) return;
        var items = context.Items; // 有界、惰性、每个订阅者独立捕获；修改图后重新扫描
        Console.WriteLine($"day={context.Run.Day}, count={items.Count}");
    }, order: 100);
```

`LifecycleContext` 只在回调同步执行期间有效。`Store`、`Items` 和 `IsCurrent` 会在回调返回、读档、场景变化、周目/槽位变化、停用或回调使身份失效时关闭；不能保存原生对象、`await` 后使用或把它用于 `SaveGame`。`Run.Day` 使用 `StoreStation.GetDayCounter()`，不可用时明确为 `-1`。库存快照失败时返回空列表，`ItemsComplete=false` 并给出 `ItemsError`，不会把截断结果冒充完整。

`EndNight` 的 finalizer 只记录原生异常并原样返回；EZapi 不吞异常、不自动重试，也不会在异常时发布成功阶段。原方法被其它 Mod 跳过时，成功后缀不执行；`NightAttemptFinished` 只有在原方法没有抛出且上下文身份仍一致时才执行。

## 内容应用通知

```csharp
using var lease = ContentApi.Subscribe("my.mod", "content-log", view => {
    if (view.OwnerId != "my.content") return;
    Console.WriteLine($"{view.ContentId}: {view.Status} {view.Reason}");
});
```

通知是全局的，订阅参数中的 `ownerId` 只是订阅者身份，不能当作内容过滤器。物品声明整批提交完成后发布 `Staged`；原生目录 `Add` 成功发布 `Applied`，目录已有 ID 发布 `Conflict`，`Add` 返回失败或工厂异常发布 `Failed`。`Accepted`/`Queued` 不是 `Applied`。通知按快照分发，回调异常只移除该订阅，不回滚已经完成的目录写入。

通知支持同步重入，但只在有界队列内处理；超过 8192 条会清空剩余队列并记录错误。停用或重新初始化会使旧订阅失效；旧 lease 不能影响新一代相同 ID 的订阅。框架不补发历史通知，也不把通知当作保存完成或联机同步完成。

## 调试与验收

左上角 **EZapi 调试 → 开启阶段记录** 会订阅 10 个阶段和内容通知，最多保存 128 条托管快照；它不会触发读档、结算、保存、生成或物品移动。玩家按正常流程读档、睡觉、外出回店，再点击“导出诊断”，查看 `UserData/EZapi/diagnostics.json` 的 `forgeLifecycle`、`contentApplication` 和 `lifecycleTrace`。实际 IL2CPP detour、保存重载和跨场景行为仍需玩家日志确认。

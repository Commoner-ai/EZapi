# EZapi 0.1.4：游戏事件与周目数据

这两个入口供 C# 内容 Mod 使用。JSON 物品包仍可独立使用，不需要为了读取配置执行 C#。官方 ModHelper 的 SetModData/GetModData 系列在本机包装程序集中缺失；当前通过公开的 PlayerStore.modData 做单点兼容适配，不把不存在的方法标成已接入。

## 订阅官方游戏事件

```csharp
private IDisposable? loaded;

public override void OnInitializeMelon()
{
    loaded = GameEvents.Subscribe("author.mod", GameEvent.GameLoadedNormal, context => {
        if (!RunData.TryOpen("author.mod", out var data)) return;
        var delivered = data!.GetInt("deliveries", 0);
        MelonLoader.MelonLogger.Msg("已交付：" + delivered);
    });
}

public override void OnDeinitializeMelon() => loaded?.Dispose();
```

Mod 必须依赖 EZapi 并在其初始化后调用。Subscribe 返回 IDisposable，关闭内容 Mod 时释放。回调同步执行；不要用 async void，也不要在事件里循环等待。一个回调抛错只会移除这一个订阅，并记入诊断 lifecycle.errors；其他订阅仍可执行。相同阶段重入被忽略，避免自己触发自己造成递归。相同阶段按订阅顺序调用，但不承诺与直接订阅官方事件的其他 Mod 的全局顺序。

| GameEvent 枚举 | 官方 ModHook 阶段 | 用途 |
| --- | --- | --- |
| GameLoadedInit/Early/Normal/Late | OnGameLoaded 同名阶段 | 载入游戏后恢复自身状态 |
| ShutterOpenedEarly/Late | OnShutterOpened 同名阶段 | 卷帘门打开 |
| ShutterClosedEarly/Late | OnShutterClosed 同名阶段 | 卷帘门关闭 |
| GoingSleepEarly/Late | OnGoingSleep 同名阶段 | 入睡前后阶段 |
| WakingUpEarly/Late | OnWakingUp 同名阶段 | 醒来阶段 |
| LeavingStoreEarly/Late | OnLeavingStore 同名阶段 | 离开店铺 |
| ReturningStoreEarly/Late | OnReturningStore 同名阶段 | 回到店铺 |
| HandlingNightlyServicesEarly/Late | OnHandlingNightlyServices 同名阶段 | 夜间服务处理 |

这些事件与官方阶段一一对应，不自动合成为“每天只运行一次”，也不表示存档已成功写到磁盘。每日唯一奖励、计费和迁移必须由内容 Mod 在实际成功后记录身份并自行核对。不要在 Late 中假定保存已经发生。

GameEventContext 只含 Event、进程内 Sequence、可空 Run。Run 是 SaveSlotId/RunId/Epoch 的纯托管快照，不含玩家或库存；没有有效玩家时可以为空。Sequence 不保存，不能充当奖励幂等键。GetRecentEvents 返回最近最多 64 条事件，可用于诊断。

## 打开当前周目的数据

```csharp
var data = RunData.Open("author.mod");
data.SetInt("deliveries", 3);       // 只暂存，不是保存完成
data.SetBool("met_courier", true);
data.SetString("route", "north");
var count = data.GetInt("deliveries", 0);
```

在主菜单、游戏还未载入或不在主线程时，TryOpen 返回 false；Open 抛带错误码的异常。所有读写都要求游戏主线程。读取缺失键时返回调用者指定的回退值，读取不会创建原生字典或替换其它内容。

RunDataStore 支持 String、Int、Long、Float、Double、Bool 和版本化 JSON；浮点数要求有限值并按 InvariantCulture 格式化。GetKeys 只返回本 Mod 的键；Remove/Clear 只删除本 Mod 的记录。格式损坏的数值读取回退；不会自动修复或覆盖原值。

每个值最多 16 KiB UTF-8，每个 Mod 最多 128 个键、64 KiB 键和值合计。键仅允许 ASCII 字母、数字、下划线、点和连字符，长度 1..120。modId 是小写命名空间，不能带路径分隔符。写入预检失败保留旧数据。

框架使用 `EZapi.RunData/v1/<modId>/<key>` 键。此命名空间与其它数据隔离，不代表宿主进程内的安全沙箱；有代码权限的其他 Mod 仍可直接访问原生存档字典。这里不假定与缺失的官方 ModHelper 内部键格式相同，将来迁移需要显式转换，不能静默认领别人的数据。

## 句柄寿命与隔离

每个句柄绑定一次载入的 SaveSlotId/RunId/Epoch。Epoch 是进程内载入代号，不写入存档；同一存档再次载入也生成新代号。读档、换玩家、换槽、runID 改变或切场景后，旧句柄 IsCurrent=false，其读写抛 EZAPI_RUN_STALE。内容 Mod 应在当前事件内重新 Open，不把句柄当作跨场景的单例。

场景切换立即关闭数据入口，随后官方读档/玩法事件根据当前 PlayerStore 重建身份。如果当前玩家身份还不完整，入口保持不可用；不要通过缓存上一个 PlayerStore 绕过。框架不缓存原生玩家、库存或数据值，只有比较用的身份与指针数值。外出返回可重新打开当前句柄，但旧大厅/店铺句柄不能继续写入。

## 结构化记录与版本

```csharp
data.SetJson("quest", new QuestState { Progress = 2 }, version: 1);
if (data.TryGetJson<QuestState>("quest", expectedVersion: 1, out var state)) {
    // 使用 state；不要因为读取失败就立即覆盖成默认值。
}
```

SetJson 存储 `{"version":1,"data":...}`。最大嵌套深度 16；不接受空 data。TryGetJson 在缺键、损坏、版本不同或类型不兼容时返回 false，保留原文。旧版本迁移由内容 Mod 显式实现；框架不会把未来格式降级覆盖。

属于单件物品的耐久、品质等仍放在原生物品标签中；不要把它们移入周目字典。此 API 适合任务进度、计数与周目标记，不适合每帧日志、大图片或世界存储。

## 保存语义与玩家验证

Set 只改变 PlayerStore.modData 的当前内存。EZapi 不调用 SaveGame，不拦截保存，不创建存档旁路文件，也不把“暂存成功”报告成“落盘成功”。依据官方文档，游戏正常保存时负责序列化 modData；本构建的保存重载效果仍待玩家确认。

调试面板的“读取测试数”只读取 `ezapi.debug/manualCounter`；“测试数 +1”仅向这个测试命名空间暂存一次。按钮不主动保存。测试档中记下测试数后，正常游戏保存、重载，再读取核对；另一个槽或新周目应有各自的值。重载前没有保存的修改按游戏行为回滚。不要用正式档反复发奖来验证数据 API。

导出诊断后查看 lifecycle.recent 的事件顺序、run 的槽/周目身份及 runDataDebug。诊断不导出其它 Mod 的数据值。离线用例只覆盖门控、隔离、订阅与序列化逻辑，不证明 Unity/IL2CPP 和真实存档已经通过。

## 0.1.46：结构化冲突检查

需要先比较旧值再提交 JSON 时，使用 [RunDataApi 周目记录](run-data-records.zh-CN.md)。它提供独立完整键和八种状态；本页 RunData.Open 简单接口及旧格式保持不变。

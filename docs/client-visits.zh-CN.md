# ClientVisitApi：访客排队与状态

`ClientVisitApi` 使用官方 `ModHook.OnGenerateCustomerVeryLate` 生成钩子排入一位原生顾客，并在顾客到访时显示 EZapi 对话。官方指南说明客户生成钩子按 `VeryEarly → Early → Normal → Late → VeryLate` 顺序运行；EZapi 在 `VeryLate` 阶段通过游戏的 `StoreClientManager.AddNextClient(StoreClient)` 添加访客。

当前游戏构建还公开了 `StoreClientListDict.CreateStoreClient(string)`。因此 `actorId` 必须是该构建原生顾客字典可创建的身份。EZapi 复用该顾客蓝图的交易和行为，只覆盖顾客精灵图键并组合到访/离场回调；这不是通用的自定义交易行为注册器。若顾客 ID 无法由原生工厂创建，请求以 `Invalid` 结束。

```csharp
private const string Owner = "author.visit";
private const string Request = Owner + ".chapter_1";

private static readonly DialogueScript Visit = DialogueApi.Create(Owner, new(
    Owner + ".greeting", "修理师", "需要查看哪台设备？",
    new[]
    {
        new DialogueChoice(Owner + ".about", "介绍服务", "确认后显示服务说明。", _ => "我可以检查你的设备。")
    }));

public static PresentationResult Queue(LifecycleContext context, string actorId, string portraitKey)
{
    return ClientVisitApi.Queue(
        Owner, Request,
        context.Run.RunId, context.Run.SlotId,
        actorId, portraitKey, Visit);
}

public static PresentationResult Status() => ClientVisitApi.Status(Owner, Request);
```

`requestId` 必须属于 `ownerId` 且在当前进程内唯一。`Queue` 返回 `Queued` 表示请求已进入 EZapi 的内存等待队列；后续官方生成钩子一次最多处理一条。`StoreClientManager.AddNextClient` 返回后会核对 `clientStack`；无法确认写入时请求转为 `Indeterminate`，不会重复排入。游戏触发原生 `OnArrival` 后状态为 `Arrived`，离场时为 `Dismissed`。如果对话界面正忙，EZapi 每 400 毫秒重试一次，最多等待 30 秒；界面仍不可用时返回 `Indeterminate`。

请求和状态只保存在当前进程内，并绑定存档槽、周目 ID 与本次载入 epoch；读档、新局、场景卸载或模组停止会结束旧请求，不写存档。`IsAvailable` 只表示当前构建指纹、官方钩子和对话运行门控均已就绪，不证明顾客已经到访或 UI 效果已通过游戏内验收。

构建覆盖范围见 [官方 API 与 Forge 对照](FORGE-PARITY.zh-CN.md)，编译示例见 [ClientVisitExamples.cs](../src/EZapi.Sample/ClientVisitExamples.cs)。

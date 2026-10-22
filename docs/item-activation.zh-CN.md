> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# EZapi物品激活

新增两种入口：JSON 声明现成治疗行为，或 C# 登记自定义同步回调。它们都绑定到官方 GameItem 实例委托，不修补游戏激活流程。最低 EZapi 版本为 0.1.3；旧版本会拒绝包含 activation 的 JSON，不能当成已支持。JSON 协议仍为 0.1，旧内容省略新字段即可继续使用。

## JSON：一次性治疗物品

```json
[
  {
    "id": "author.med.dressing",
    "modId": "author.med",
    "name": "伤口敷料",
    "value": 40,
    "types": ["MEDICAL"],
    "activateDefault": true,
    "shortDescription": "有伤势时激活，治疗后消耗。",
    "activation": {"kind": "healWounds", "consumption": "destroy"}
  }
]
```

放在 pack.json.id=author.med 的内容包中。没有 PNG 时使用框架占位图。MEDICAL 仅是分类，实际行为来自 activation。使用游戏现有的物品激活动作；EZapi 不新增全局按键，不自动激活或自动制造伤势。

healWounds 直接按官方治疗示例将伤势设为 0、稳定伤口并清除新伤标记；不改变成瘾、属性或不存在的通用血量值。有伤势、伤口不稳定或新伤标记三者之一才可使用。健康时不消耗物品。

## JSON：按次数治疗

在物品上同时设置：

```json
"useCount": {"maximum": 3, "current": 3, "destroyOnEmpty": true},
"activation": {"kind": "healWounds", "consumption": "useCount", "charge": 1}
```

上面是物品对象内的字段片段，不是完整 JSON 文件。charge 默认 1，范围 1..9999，不大于 maximum；实际剩余次数不足时不治疗、不扣除。用尽由原生 UseCountHelper 按 destroyOnEmpty 处理。destroy 模式不允许附加 useCount，也不允许 charge!=1，避免静默丢弃剩余次数。任何 JSON 行为均不能和 forceDisableUse/forceDisableActivate=true 同时配置。

## C#：自定义行为

```csharp
var item = ItemRegistry.Register(new ItemDefinition {
    Id = "author.mod.marker", Name = "自定义标记", ActivateDefault = true
});
if (item.Accepted) {
    var behavior = ItemActivation.Register(item.ItemId,
        canActivate: context => true,
        activate: context => MelonLoader.MelonLogger.Msg("标记已激活"));
    // 必须检查 behavior.Accepted 和 Message。
}
```

先提交物品定义，再在同一初始化主线程登记行为；目录初始化后不允许新增或替换。一个物品 ID 只登记一次，不能同时填写 ItemDefinition.Activation。上下文提供当前 Item、Inventory、可空 Slot 和 Player；读取原生类型需引用本机 Assembly-CSharp 与 Il2CppInterop 等依赖。

canActivate 是无副作用的快速查询，游戏可能多次调用；不能在查询中扣钱、修改标签或消耗物品。activate 同步执行，内容作者自行处理自己的效果与消耗；EZapi 不会额外销毁、扣次数或保存。完整可选示例见 src/EZapi.Sample/ExampleMod.cs，不需要为了测试 JSON 再安装示例 DLL。

不要缓存上下文、物品、库存或玩家，不要将回调写成 async void、后台线程或延迟操作。框架在每次回调前读取当前玩家并检查物品 ID、单件数量、所有权、父库存和禁用标记；执行前重新查询条件，并阻止同一物品回调重入。

## 异常与持久化

查询或行动抛错会记录 EZAPI_ACTIVATION_FAILED，并将该物品 ID 的激活停用到重启，其他 ID 不受影响。此时诊断文件 activation.disabled 可查。框架不自动回滚内容回调已做的事，也不会重试部分完成的效果。

治疗先写原生伤势，再调用 Destroy 或 UseCountHelper.Use；若原生消耗异常，已有伤势变更不自动回滚，该 ID 停用并记录失败。没有自建存档文件、保存补丁或自动保存。保存重载/过夜效果依赖游戏原生流程，尚待玩家测试。

## 玩家测试

确认日志为 0.1.3，用现有调试面板生成“一次性伤口敷料”和“三次伤口敷料”。先在健康状态尝试激活，应不消耗。角色自然受伤后使用一次性敷料，确认伤势清除、物品消失；再在后续受伤时用三次敷料，确认只扣 1 次。不要为了测试修改正式存档的伤势。

普通工具包等原四个示例仍只展示属性。调试“校验原生属性”仅检查治疗实例是否绑定委托，不执行治疗或扣次数。图像/悬停/拖拽正常后，再由玩家测试剩余次数的保存重载与过夜。当前离线检查不证明原生 UI 或存档已通过。

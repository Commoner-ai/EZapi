# 物品来源、赃物与热度

订货、奖励或新物品工厂需要设置普通货物/赃物时，使用 `ItemOriginApi`。它调用原生 `StolenHelper`，并检查实际读回结果。当前 DLL 的验收范围见 [release-status.json](release-status.json)。

| 需求 | 调用 | 结果 |
| --- | --- | --- |
| 只读当前来源 | `Read(item)` | `Snapshot.IsStolen`，赃物的 `FoundChancePercent` |
| 固定 100% 热度赃物 | `StageStolen(item)` | 包括已经带赃物状态的工厂结果 |
| 固定 0..100% 热度 | `StageStolen(item, heatPercent)` | **0 就是固定 0%，不是随机** |
| 普通货物 | `StageClean(item)` | 调用原生移除；已是普通货物则 `Unchanged` |
| 游戏原生初始化 | `InitializeNative(item)` | 使用 `InitStolenItem(item,0)` 的原生规则，保留已有赃物热度 |

## 最短调用

```csharp
// 已载入周目的游戏主线程；item 为这次明确操作取得的活实例。
var result = ItemOriginApi.StageStolen(item, heatPercent: 100);
if (result.Status is ItemOriginStatus.StagedInMemory or ItemOriginStatus.Unchanged)
{
    // result.After 是已核对的内存状态；之后才能进入调用 Mod 自己的交付流程。
}
else
{
    // 停止后续交付/扣费。Indeterminate 表示可能已部分写入，先 Read 对账。
    // 不在这里盲目重试、自动发货或自动销毁已经进入库存的物品。
    Log(result.Reason);
}
```

可编译调用者：[Origin.cs](examples/item-origin-dll/Origin.cs)、[工程](examples/item-origin-dll/Origin.csproj)。在开发包根目录运行 `dotnet build docs/examples/item-origin-dll/Origin.csproj -c Release -p:GameDir=你的游戏目录`。这个接口传入原生 `GameItem`，因此示例需要本机游戏包装程序集；它们不会随包分发。可用 `-p:EZapiDll=框架DLL完整路径` 指定本地候选。

## 重要语义

- 热度使用**整数百分数**，不是 0..1 浮点数。读取直接使用 `IsStolenItem/GetFoundChance`；普通物品的 `FoundChancePercent` 为 null，不能把未知读取当成普通物品。不会调用可能消耗随机序列的 `IsFound` 判定。
- 原生 `InitStolenItem(...,0)` 根据游戏规则初始化，低价值物品可能仍为普通物品；对已有赃物不会覆盖热度。因此 `InitializeNative` 成功只保证原生流程完成并返回实际状态，**不保证 IsStolen=true**。已经是赃物时直接 `Unchanged`。不要把它当成“每次重新随机热度”。
- `StageStolen` 保证固定目标才报告成功。新物品先由原生初始化；固定 0% 时用正数初始化再原生 SetHeat 到 0，以免误入随机分支。已有赃物只在热度需要改变时 SetHeat。初始化/写入任一步之后的异常、读回不符或上下文变化均为 `Indeterminate`，不自动恢复或重试未知的原生字段。
- `StageClean` 调用原生 RemoveStolenStatus，保留其自身语义；它不是洗钱、洗赃服务或付款接口，不自动消除违禁品分类或 Mod 自己的来源记录。其他 Mod 的自定义来源仍由该 Mod 管理。
- 只在已初始化的游戏主线程、当前载入周目使用。写入要求已就绪的库存冻结门控、无活动拖拽、未冻结。库存物品须由玩家拥有；游离物品必须由调用者拥有处置权，例如刚创建且尚未交付的工厂结果。框架不能从一个裸包装对象证明其来源，调用者不得传入目录共享模板或别人的游离物品。
- 同步操作内核对周目 Epoch、实例、ID、堆叠数量、父库存、state/dictionary。回调中重载、移动、冻结或销毁会中止后续写入；不会把之前的原生包装对象缓存到下一场景。调用者同样不能跨载入缓存 `GameItem`。
- 写入对整件原生对象操作，不拆堆、不按堆叠数量缩放热度。成功后调用 `SyncModifiedState` 并核对来源；**不请求保存**。正常保存能否保留这些状态须按当前 DLL 的保存重载验收确认。
- 没有新 JSON 字段：这是对活实例的显式行为。内容声明的违禁品类别与赃物来源是不同概念，不要自创 `stolen` 内容字段。

## 语言、失败与验收

结果状态和数值不依赖游戏语言，沿用原生 tooltip 展示；调用者的提示应通过 `GameLanguage.Resolve` 提供中英文本。`Reason` 是供作者诊断的稳定错误码和英文说明，不直接当作玩家固定中文文案。

`Read` / `Unchanged` 没有写入；`StagedInMemory` 只有内存读回成功；`Frozen` / `Conflict` 等写前拒绝的 `NativeWriteAttempted=false`；`Indeterminate` 则为 true，After 不提供未经确认的状态。重新 `Read` 只帮助检查当前值，不能逆推出原生随机序列或其他 Mod 的全部副作用。

离线测试使用真实 `ItemOriginApi` 源码与可观察的原生边界替身，覆盖热度边界、已有赃物、冻结、拖拽、NPC 所有权、递归、写中换档和部分异常。它不能证明当前 DLL 的实机、夜间衰减、检测事件或磁盘保存重载；这些保留待验收。见 [机器索引](item-origin.index.json)、[错误索引](errors.index.json)。

> 当前 API 指南。见[统一版本与验收状态](current-status.zh-CN.md)：当前 DLL 尚未完成实机验收；旧测试数量仅适用于其历史 DLL。

# Workshop API：工坊页与进度链

`WorkshopApi` 注册由内容 Mod 提供快照和解锁回调的工坊页。EZapi 渲染独立工坊窗口，支持多个页签、进度图、节点详情、条件/奖励展示和关闭释放。公开 C# DTO 与方法对齐 Forge `ForgeWorkshopApi`；EZapi 使用自有 UI，不依赖 Forge。

**资源事务归内容 Mod 所有。** `Credits`、`Conditions.Satisfied`、`Consume` 和 `Rewards` 是快照展示数据。EZapi 不读取或扣除资金/物品，不交付奖励，也不自动保存。`Unlock` 回调必须在调用时重新检查资格、执行支付和奖励交付，并自行处理回滚与持久幂等。

## 注册一个页签

```csharp
using EZapi;
using EZapi.Registration;

const string owner = "author.example";
const string tabId = owner + ".workshop";
const string entryId = owner + ".workshop.field_kit";

SubmitResult result = WorkshopApi.RegisterTab(
    owner, tabId, "示例工坊", "Example Workshop", Capture, Unlock);
if (!result.Accepted)
    LoggerInstance.Warning($"工坊页未登记：{result.Reason}");

bool opened = WorkshopApi.OpenTab(tabId);
```

`RegisterChain` 与 `RegisterTab` 使用相同的快照和回调形状，但以独立进度链身份打开。ID 必须是小写 owner 命名空间 ID；重复的同一声明返回 `AlreadyPresent`，冲突声明返回 `Conflict`。注册、打开和关闭都在 EZapi 初始化线程/游戏主线程调用。

## 提供快照

```csharp
WorkshopSnapshot Capture()
{
    bool unlocked = ReadMyModState();
    var entry = new WorkshopEntry(
        entryId, "野外工具包", "进度节点", "制作一个野外工具包。",
        "需要 100 资金", "100", "1 节点", unlocked ? "已解锁" : "未解锁",
        unlocked ? "完成" : "解锁", unlocked, true, !unlocked)
    {
        Conditions = new[]
        {
            new WorkshopRequirement(WorkshopResourceKind.Credits, "credits", 100, Consume: true, Satisfied: HasCredits(100))
            { DisplayName = "资金" }
        },
        Rewards = new[]
        {
            new WorkshopReward(WorkshopResourceKind.Item, "author.example.field_kit", 1)
            { DisplayName = "野外工具包" }
        },
        Dependencies = new[] { owner + ".workshop.root" },
        VisualLinks = new[] { owner + ".workshop.root" },
        GraphX = 1,
        GraphY = 0,
        Completed = unlocked
    };
    return new WorkshopSnapshot(ReadCredits(), English: false,
        new[] { new WorkshopGroup("工具", "Tools", 1) }, new[] { entry }, "作者提供的说明");
}

void Unlock(string selectedEntryId)
{
    if (selectedEntryId != entryId) return;
    // 再次检查并执行你自己的支付、制作/交付和存档逻辑。
}
```

EZapi 接受快照时会深复制各集合并做有界校验。`ActionEnabled`、`Unlocked` 和 `PrerequisiteMet` 控制按钮是否可用；打开详情或点击前会读取新快照。`Conditions.Satisfied`、`Dependencies`、`VisualLinks`、`Completed`、`GraphX/GraphY` 不会替作者计算。进度图优先按 GraphX/GraphY 排布，用 VisualLinks 和 Dependencies 绘制当前页内连线；没有坐标的节点使用稳定网格布局。列表和图谱都可分页。

`WorkshopRequirement` 与 `WorkshopReward` 支持 `Credits` 和 `Item`。资源 ID 接受游戏原生键（包括大小写混合）与命名空间 ID。数量、条目、分组、文本与链接都有上限，非有限图坐标会被拒绝。

## 回调与生命周期

- 窗口显示的是隔离的只读快照；点“刷新”重新调用 `Func<WorkshopSnapshot>`。
- 点击动作前先重新读取快照；仅 `ActionEnabled && PrerequisiteMet && !Unlocked` 时调用 `Action<string>`。
- 每个节点在当前窗口生命周期最多调用一次；回调异常时记录日志，不自动重放。关闭并重开后可以再次尝试，因此支付/交付仍须由作者业务键做持久幂等。
- 场景卸载和 `CloseWindow()` 会释放窗口快照、滚动状态与回调引用；注册目录保留到 EZapi 进程关闭。
- `IsVisible` 仅表示 EZapi 工坊窗口打开，不表示游戏已扣款、解锁或保存。

示例夹具见 [`WorkshopExamples.cs`](../src/EZapi.Sample/WorkshopExamples.cs)；调试面板的“工坊预览”只切换内存状态，不扣款、不发放物品、不写存档。该预览可用于检查列表、图谱、快照刷新和回调路径。

## 0.1.48：当前 IL2CPP 构建的绘制修复

列表详情保持连续滚动：鼠标悬停详情区可用滚轮，也可点击右侧上下箭头；长文本在卡片内裁剪，位置有上下界。滚轮只在 Update 消费一次，Layout/Repaint 不重复滚动。进度图保留节点连线与点击进入详情。关闭、换页签和场景卸载清空滚动状态。

当前 Unity 包装的 BeginScrollView、EndScrollView 和 DrawTexture 可到达 Method unstripping failed 路径，导致原先工坊列表/图谱不能可靠绘制。实现改为当前公开的 BeginGroup/EndGroup 裁剪和 GUI.Box 色块绘线；不调用原生地址，不重写游戏全局 UI。静态递归检查与本版游戏实测分别记录在 verification.json，不能用编译通过代替实际打开窗口。

调试“工坊预览”提供 24 行长文本、末尾标记 EZAPI-WORKSHOP-END、两个相连节点和内存动作计数，可合并验证列表、滚动、进度图、回调、关闭和重开。回调不扣资金、不发奖励、不保存。

> 历史验收记录（0.1.48），不作为当前 DLL 通过依据。
> 0.1.48 已在仅加载 EZapi 的实际游戏进程中通过自动化 23 项检查，含长文本箭头/滚轮/反向滚动、图连线、节点详情、一次动作与关闭清理。过程未逐步截图，使用本次关闭视图诊断核对，另留一张图谱视觉验收图。535 个 ES3 文件均未改变。原生进度链打开、真实支付/奖励与保存重载未测试。证据见 verification/native-test-0.1.48/。固定进入测试档和批量验收方法见 [自动化指南](native-test-automation.zh-CN.md)。

## 0.1.72：按游戏语言显示

新代码使用 `WorkshopSnapshot.ForGameLanguage(credits, groups, entries)`，框架在每次打开/刷新时读取一次当前语言，内置按钮和自定义双语字段统一解析。`zh*` 使用中文，其它语言使用英文，缺少一侧时用已有的一侧。例子：

```csharp
var entry = new WorkshopEntry(id, "", "", "", "", "", "", "", "", false, true, true)
{
    LocalizedTitle = new("野外工具包", "Field kit"),
    LocalizedDescription = new("仅展示内容", "Display only"),
    LocalizedActionText = new("解锁", "Unlock")
};
return WorkshopSnapshot.ForGameLanguage(credits, groups, new[] { entry })
    with { LocalizedMessage = new("当前快照", "Current snapshot") };
```

双语字段覆盖节点标题、副标题、说明、要求、花费、节点花费、状态、动作、条件/奖励名称和页消息。未提供双语字段的旧字面量保留。既有构造器的 `English` 标志继续控制固定语言，只有 `FollowGameLanguage=true` 才按游戏语言解析；便捷工厂已设置此标志。切换语言后点击刷新或重新打开。当前快照不会在绘制时混用两种语言。独立工坊声明不接受隐式 CSV Key；需要 CSV 时在 provider 中显式解析。

验证：链接真实 WorkshopApi/Definition 的离线注册、快照及动作测试；真实当前DLL的英文/中文窗口待验收。可编译示例见 `../src/EZapi.Sample/WorkshopExamples.cs`。

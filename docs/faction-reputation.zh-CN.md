# 阵营声望界面与扩展

自 0.1.78 起，EZapi 在原生纸张窗口上提供左侧阵营列表、搜索与纵向滚动，右侧提供阵营名称、关系、真实声望刻度与悬停权益详情。玩家只需同一份 `Mods/EZapi.dll`，不要求打开调试工具。

## 优先读取原生数据

窗口每 0.25 秒读取当前 `PlayerStore.storeReputations`，不固定阵营数量。使用原生 `StoreReputation` 接入游戏的 Mod，其新增阵营会自动出现；对应 `perks` 的名称、说明、风味文本、阈值和激活状态均从当前原生对象读取。本界面不增减声望，不改变权益激活、不支付，也不调用保存。名称使用原生本地化函数，表情来自原生 PerkElement.Init；悬停调用原生 UpdateToolTipContent 并读取格式化结果，保留其他 Mod 在该方法追加的说明。原版纸张、字体、关闭及信息按钮保留；右上两控件和命中区域为 44×44 逻辑像素。原生范围默认 -200 到 200；扩展阈值或当前声望超出时自动扩大展示范围。

UI 读到条目不代表游戏的交易、阵营事件或存档支持该阵营。原生阵营注册及持久化由内容 Mod 自行负责，本接口不把一个展示条目假装成完整原生阵营。

## 自有数据接入：一个登记调用

自有 ModData 或其他数据源无需修改原生阵营列表。初始化后在游戏主线程注册定义及只读回调：

```csharp
FactionUiApi.Register("author.guild", new FactionUiDefinition(
    "author.guild.free_traders", new("自由商会", "Free traders")) {
    Color = "#71546D", Minimum = -200, Maximum = 200
}, context => {
    // 每次重新取得当前周目数据，不缓存原生对象或 RunDataStore。
    var data = RunData.Open("author.guild");
    var reputation = data.GetDouble("reputation", 0);
    return new FactionReputationView(reputation) {
        Perks = new[] {
            new FactionPerkView("author.guild.discount",
                new("商会优惠", "Guild discount"),
                new("商会商人给予交易优惠。", "Guild traders offer a discount."),
                60, data.GetBool("discount-active")) { Icon = "star" }
        }
    };
});
```

`Queued` 表示登记接受，不表示窗口已打开或已持久化。`FactionUiApi.Open()` 打开原版声望窗口；`Snapshot()` 只读返回原生与登记阵营的不可变快照；`Unregister(ownerId, factionId)` 只撤销自有展示，不删除数据。

提供者可返回 `null` 隐藏尚未发现的阵营，抛出异常只会让自身条目暂时不显示。禁止在提供者内注册/注销、保存、发物品、扣费或更改声望。它可能每秒执行四次。上下文含当前周目及语言；读档或场景变更期间返回的过期结果被丢弃。

## JSON 定义

使用 [schema](../schemas/faction-ui.schema.json) 与 [示例](examples/faction-ui/free-traders.json)。`FactionUiJson.Parse(stream)` 可离线校验；`FactionUiApi.RegisterJson(ownerId, stream, readState)` 登记，运行数据仍由 C# 只读提供者返回。JSON 不编码回调、自动奖励或保存规则，也不会被自动装入物品内容包。

字段仅允许 `id/name/color/order/minimum/maximum`，`name` 包含 `chinese/english`，至少一侧有可读文本。遵循游戏语言，缺少一侧回退另一侧。上限 16 KiB，拒绝未知字段和忽略大小写后的重复字段。阵营 ID 必须位于 owner 命名空间下，色值为 `#RRGGBB`，范围跨越零。最多 256 个登记阵营，每个最多 256 个权益。

权益的 `Active` 来自调用 Mod 的真实判定，本界面不会仅凭数值触发效果。阈值符号原样保留，不把负声望改写成正数。`AlwaysActive` 用于始终生效的权益。可选图标为 `star/happy/smile/sad/angry/skull`。图标按真实阈值映射至横条。相同或相近阈值在固定宽度下合并为带数量的入口，保留所有效果；悬停显示第一项，点击图标或下方左右按钮逐项切换，细刻度指向当前效果真实阈值。底部只显示当前权益，无卡片列表、进度条或滚动条；离开图标后说明保留，切换阵营清空旧说明。超长说明使用原生 TMP 分页并出现文字翻页按钮，保持字号和窗口大小。

## 生命周期与兼容边界

精准钩子组 `ezapi.presentation.faction-browser` 在 `ReputationUIManager.OpenUI()` 返回前绑定，保留原方法及普通 Mod 的后置初始化。关闭时停止刷新，原生关闭完成后释放自有对象和委托；重新打开、换场景和退出均恢复原窗口状态。不复用先显示原版再在下一帧改布局的方式。

原生列表内容优先，若登记 ID 与现有原生阵营重复，显示原生条目。私有 Canvas、完全替换声望窗口的 Mod 或不进入原生列表的对象不会凭名字自动合并；后者应使用提供者接口。

可选调试面板含“阵营声望”和“阵营扩展检查”。扩展检查通过相同公开接口临时加入 24 个阵营，每个 28 个权益；关闭窗口清除，不写原生列表或存档。验收状态和准确 DLL 哈希统一见 [release-status.json](release-status.json)。

## 展示顺序与限制

左侧保留原生阵营列表顺序，登记阵营随后按 Order、ID 排序。权益对应原版纵向刻度从高到低，同阈值保留来源顺序；横条从负到正排列，不用固定五张表情示意。仅显示型提供者须设置覆盖自身阈值的 Minimum/Maximum，超范围图标会夹在端点。原生发现上限 512 个阵营，每阵营 256 个权益；登记上限 256 个阵营，每阵营 256 个权益。单个提供者或原生文本读取失败只影响对应内容，并进入诊断，不应隐藏其他阵营。

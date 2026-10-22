namespace EZapi;

/// <summary>声明式物品定义。分类、状态和显示分别定义；设置种类不会自动创建使用行为。</summary>
public sealed class ItemDefinition
{
    /// <summary>夜间商店供应：none/repeatable/unique；unique表示当前玩家未持有时供应。</summary>
    public string NightShop { get; init; } = "none";
    /// <summary>NPC分类候选池声明；先查看NpcStockApi与原版自动路由的能力边界。</summary>
    public NpcTradeStockOptions? NpcTrade { get; init; }
    /// <summary>唯一小写 ID，例如 author.mod.item。保存重载靠它重新创建物品。</summary>
    public string Id { get; init; } = "";
    /// <summary>显示名称，不代表自定义的使用行为已经实现。</summary>
    public string Name { get; init; } = "";
    /// <summary>物品说明；作为 localizedShortDescription 的旧版回退。</summary>
    public string? Description { get; init; }
    /// <summary>当前游戏语言的名称；内联中英或所属包的 CSV Key。省略保留 name。</summary>
    public LocalizedItemText? LocalizedName { get; init; }
    public LocalizedItemText? LocalizedDescription { get; init; }
    public LocalizedItemText? LocalizedShortDescription { get; init; }
    public LocalizedItemText? LocalizedLongDescription { get; init; }
    public LocalizedItemText? LocalizedFlavorText { get; init; }
    public LocalizedItemText? LocalizedCustomText { get; init; }
    /// <summary>PNG 文件名，不含扩展名/目录；相对于所属内容包 Textures/Items。</summary>
    public string? Sprite { get; init; }
    /// <summary>所属 EZapi 内容包 ID，必须与 pack.json 一致。</summary>
    public string? ModId { get; init; }
    /// <summary>矩形占格宽度，1—8；省略时为 1。</summary>
    public int? Width { get; init; }
    /// <summary>矩形占格高度，1—8；省略时为 1。</summary>
    public int? Height { get; init; }
    /// <summary>基础货币价值，0—1000000000；不是最终成交价。</summary>
    public long? Value { get; init; }
    /// <summary>原生短简介；与旧 description 二选一。</summary>
    public string? ShortDescription { get; init; }
    /// <summary>原生长简介；native 模式下显示位置由游戏的详细说明规则决定。</summary>
    public string? LongDescription { get; init; }
    /// <summary>风味文本；early/late 模式使用灰色斜体。</summary>
    public string? FlavorText { get; init; }
    /// <summary>原生自定义文本；需要固定显示时选 early/late。</summary>
    public string? CustomText { get; init; }
    /// <summary>native / early / late。native 交给游戏；early/late 使用官方 tooltip 事件。</summary>
    public string DescriptionPlacement { get; init; } = "native";
    /// <summary>分类字符串；原生键保持大小写，自定义键使用命名空间。</summary>
    public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();
    /// <summary>自定义分类的显示名。每个小写命名空间分类必须声明，原生大写分类由游戏翻译。</summary>
    public IReadOnlyList<ItemTypeNameDefinition> TypeNames { get; init; } = Array.Empty<ItemTypeNameDefinition>();
    /// <summary>本物品实例独立的标签默认值；读取有效状态使用 GetTagReadonly。</summary>
    public IReadOnlyList<ItemTagDefinition> Tags { get; init; } = Array.Empty<ItemTagDefinition>();
    /// <summary>可选矩形 mask；每行只允许 0/1，必须匹配 width/height，至少一个 1。</summary>
    public IReadOnlyList<string>? ShapeRows { get; init; }
    /// <summary>当前只支持单件，必须保持 1。</summary>
    public int InitialCount { get; init; } = 1;
    /// <summary>保留字段；当前仅允许 1。多件堆叠在原生拆分/合并验证前明确拒绝。</summary>
    public int MaxStackSize { get; init; } = 1;
    /// <summary>原生使用门控；false 不会自动添加使用行为。</summary>
    public bool? ForceDisableUse { get; init; }
    /// <summary>原生激活门控；false 不会自动添加激活行为。</summary>
    public bool? ForceDisableActivate { get; init; }
    /// <summary>原生默认激活标记；需要内容代码实现实际行为。</summary>
    public bool? ActivateDefault { get; init; }
    /// <summary>原生额外命中整数值，-100..100；不创建战斗能力。</summary>
    public int? BonusAccuracy { get; init; }
    /// <summary>原生监视触发标记；不创建战斗能力。</summary>
    public bool? TriggerOverwatch { get; init; }
    /// <summary>原生战斗外使用标记；不创建使用行为。</summary>
    public bool? CanUseOutsideCombat { get; init; }
    /// <summary>原生违禁品等级 0..4，调用官方辅助方法。</summary>
    public int ContrabandLevel { get; init; }
    public UseCountDefinition? UseCount { get; init; }
    /// <summary>官方 FoodItemHelper 初始化。不会凭分类自动创建食用回调。</summary>
    public FoodDefinition? Food { get; init; }
    /// <summary>官方 LiquidContainerHelper 初始化。不会替换游戏的饮用/倒液流程。</summary>
    public LiquidContainerDefinition? LiquidContainer { get; init; }
    /// <summary>原生储物网格和窗口。每次工厂调用单独创建，拖放与嵌套保留游戏规则。</summary>
    public ContainerDefinition? Container { get; init; }
    /// <summary>原生模块的类型、三个基础属性与等级；使用官方ModuleHelper初始化。</summary>
    public ModuleDefinition? Module { get; init; }
    /// <summary>原生节点初始化；不自动添加具体效果，效果通过EffectApi/原生效果目录配置。</summary>
    public bool Node { get; init; }
    /// <summary>可选的声明式激活行为；当前支持官方示例的 healWounds。分类本身不添加行为。</summary>
    public ItemActivationDefinition? Activation { get; init; }
    public ItemSoundDefinition? Sounds { get; init; }
    public ItemTooltipDefinition Tooltip { get; init; } = new();
}

/// <summary>字段原样传给当前官方八参数InitModuleItem；属性单位与语义沿用游戏。</summary>
public sealed class ModuleDefinition
{
    public string Type { get; init; } = "MODULE_TYPE_UNIVERSAL";
    public int Performance { get; init; }
    public int Efficiency { get; init; }
    public int Quality { get; init; }
    public bool IsShipModule { get; init; } = true;
    public string AdditionalEffectDisplay { get; init; } = "";
    /// <summary>本地化附加说明由官方 Tooltip 回调实时追加；不写入 helper 固定说明，避免保存创建时语言。</summary>
    public LocalizedItemText? LocalizedAdditionalEffectDisplay { get; init; }
    public int Tier { get; init; } = 1;
}

/// <summary>储物容器，不是液体容器。准入列表只选一种，原样传给原生 helper。</summary>
public sealed class ContainerDefinition
{
    /// <summary>内部格数，宽高各1..12；必填，与物品外部width/height独立。</summary>
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>可选内部0/1网格；不影响物品外部占格。</summary>
    public IReadOnlyList<string>? ShapeRows { get; init; }
    /// <summary>窗口标题，省略使用物品名。</summary>
    public string? Title { get; init; }
    /// <summary>当前语言的窗口标题；省略时依次使用 title、已翻译物品名。</summary>
    public LocalizedItemText? LocalizedTitle { get; init; }
    public bool Draggable { get; init; } = true;
    /// <summary>原生准入标签候选列表；空表示不传此过滤器。不能同时填写AllowedIds。</summary>
    public IReadOnlyList<string> AllowedTags { get; init; } = Array.Empty<string>();
    /// <summary>原生准入物品ID候选列表；空表示不传此过滤器。</summary>
    public IReadOnlyList<string> AllowedIds { get; init; } = Array.Empty<string>();
}

public sealed class FoodDefinition
{
    /// <summary>交给 FoodItemHelper 的热量值，1..100000。</summary>
    public int Calories { get; init; }
    /// <summary>交给原生 helper 的罐装标记。</summary>
    public bool Canned { get; init; }
    /// <summary>原生 isMeat 标记，默认 false；使用此字段的内容包要求 EZapi 0.1.16。</summary>
    public bool Meat { get; init; }
    /// <summary>可选新鲜度，当前构建限制为 0..100；省略则不初始化新鲜度。</summary>
    public int? Freshness { get; init; }
}

public sealed class LiquidContainerDefinition
{
    /// <summary>交给 LiquidContainerHelper 的容量，单位毫升，1..100000。</summary>
    public int Capacity { get; init; } = 500;
    /// <summary>创建时是否装满；游戏仍负责实际饮用、转移和保存。</summary>
    public bool StartFull { get; init; } = true;
}

public sealed class ItemTypeNameDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public LocalizedItemText? LocalizedName { get; init; }
}

/// <summary>原生 TagState 的独立数据字段。Enabled 与 BoolValue 是不同字段；不自动创造行为。</summary>
public sealed class ItemTagDefinition
{
    public string Id { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public string? StringValue { get; init; }
    public int? IntValue { get; init; }
    public long? LongValue { get; init; }
    public float? FloatValue { get; init; }
    public double? DoubleValue { get; init; }
    public bool? BoolValue { get; init; }
}

public sealed class UseCountDefinition
{
    public int Maximum { get; init; }
    public int? Current { get; init; }
    public bool Transferable { get; init; } = true;
    public bool DestroyOnEmpty { get; init; } = true;
    public bool AutoValue { get; init; }
    public int BaseValue { get; init; }
    public int ValuePerUse { get; init; }
}

/// <summary>原生槽位激活行为。默认治疗后销毁；useCount 模式通过原生 helper 消耗次数。</summary>
public sealed class ItemActivationDefinition
{
    public string Kind { get; init; } = "";
    public string Consumption { get; init; } = "destroy";
    public int Charge { get; init; } = 1;
}

/// <summary>已有游戏声音 ID，不是磁盘路径；新增音频注册尚未实现。</summary>
public sealed class ItemSoundDefinition
{
    public string? DragStart { get; init; }
    public string? DragEnd { get; init; }
    public string? ContainerOpen { get; init; }
    public string? ContainerClose { get; init; }
    public string? Select { get; init; }
    public string? ExamineOpen { get; init; }
    public string? Toggle { get; init; }
    public string? Activate { get; init; }
    public string? Use { get; init; }
}

/// <summary>扩展显示，不清空游戏原 tooltip。来源行使用内容包显示名称。</summary>
public sealed class ItemTooltipDefinition
{
    public bool ShowSource { get; init; } = true;
    public string? SourceName { get; init; }
    public LocalizedItemText? LocalizedSourceName { get; init; }
    public IReadOnlyList<TooltipLabelDefinition> Labels { get; init; } = Array.Empty<TooltipLabelDefinition>();
    public IReadOnlyList<TooltipLineDefinition> Lines { get; init; } = Array.Empty<TooltipLineDefinition>();
}

public sealed class TooltipLabelDefinition
{
    public string Text { get; init; } = "";
    public LocalizedItemText? LocalizedText { get; init; }
    /// <summary>绑定实时原生标签；省略则是纯显示。禁用标签不显示，ValueKind 选择其当前值。</summary>
    public string? TagId { get; init; }
    public string ValueKind { get; init; } = "none";
    public string Color { get; init; } = "LightTurquoise";
    public bool Bold { get; init; }
    public bool Italic { get; init; }
}

public sealed class TooltipLineDefinition
{
    public string Text { get; init; } = "";
    public LocalizedItemText? LocalizedText { get; init; }
    public string Position { get; init; } = "late";
    public string Color { get; init; } = "None";
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strikethrough { get; init; }
}

/// <summary>声明接收结果。Accepted 只代表进入待注册表，不代表游戏内创建或存档测试通过。</summary>
public sealed class ItemRegistrationResult
{
    internal ItemRegistrationResult(string id, bool accepted, string message)
        => (ItemId, Accepted, Message) = (id, accepted, message);
    public string ItemId { get; }
    public bool Accepted { get; }
    public string Message { get; }
}

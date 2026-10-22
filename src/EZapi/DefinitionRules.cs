using System.Text.RegularExpressions;
namespace EZapi;

/// <summary>运行时与 CLI 共用的纯托管规则，避免不同校验器口径不一致。</summary>
public static class DefinitionRules
{
    /// <summary>无效字段抛出带错误码的 ArgumentException；不触碰原生对象。</summary>
    public static void Validate(ItemDefinition item)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach(var binding in ItemText.Bindings(item)) {
            try { binding.Text.Validate(binding.Limit,allowCsv:true); }
            catch(ArgumentException error) { throw new ArgumentException(item.Id+"."+binding.Path+": "+error.Message,error); }
            if(binding.Text.Key is not null&&item.ModId is null)
                Fail("LOCALIZATION",binding.Path+" CSV key requires modId and a registered content-pack catalog");
        }
        if(item.NpcTrade is {} npc)NpcStockPlanner.Validate(npc);
        if(item.NightShop is not ("none" or "repeatable" or "unique"))throw new ArgumentException("EZAPI_STOCK_POLICY: nightShop must be none/repeatable/unique.");
        if (item.Id is null || item.Id.Length>160 || !Regex.IsMatch(item.Id,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)+$"))
            throw new ArgumentException("EZAPI_ITEM_ID: use a lowercase namespaced ID (author.mod.item).");
        if ((string.IsNullOrWhiteSpace(item.Name) && item.LocalizedName is null) || item.Name?.Length>200)
            throw new ArgumentException("EZAPI_ITEM_NAME: expected non-blank name, at most 200 characters.");
        if (item.Description?.Length>4000) throw new ArgumentException("EZAPI_ITEM_DESCRIPTION: max 4000 characters.");
        if (item.Value is <0 or >1000000000) throw new ArgumentException("EZAPI_ITEM_VALUE: expected 0..1000000000.");
        if (item.Width is <1 or >8 || item.Height is <1 or >8 || item.Width.HasValue!=item.Height.HasValue)
            throw new ArgumentException("EZAPI_ITEM_SHAPE: supply both width/height in 1..8, or omit both.");
        if(item.ModId is not null&&!Regex.IsMatch(item.ModId,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*$"))
            throw new ArgumentException("EZAPI_MOD_ID: invalid pack ID.");
        if(item.Sprite is not null && (item.Sprite.Length>80||!Regex.IsMatch(item.Sprite,@"^[a-z0-9_-]+$")||item.ModId is null))
            throw new ArgumentException("EZAPI_ITEM_SPRITE: PNG key without extension/path and modId required.");
        if(item.LocalizedDescription is not null&&item.LocalizedShortDescription is not null)Fail("DESCRIPTION","use localizedDescription or localizedShortDescription, not both");
        if (item.Description is not null && item.ShortDescription is not null) Fail("DESCRIPTION", "use description or shortDescription, not both");
        if (item.ShortDescription?.Length>4000 || item.LongDescription?.Length>8000 || item.FlavorText?.Length>4000 || item.CustomText?.Length>4000) Fail("TEXT", "text too long");
        if (item.DescriptionPlacement is not ("native" or "early" or "late")) Fail("PLACEMENT", "native/early/late required");
        if (item.MaxStackSize is <1 or >999 || item.InitialCount<1 || item.InitialCount>item.MaxStackSize) Fail("STACK", "1 <= initialCount <= maxStackSize <= 999");
        if (item.MaxStackSize!=1 || item.InitialCount!=1) Fail("STACK_UNSUPPORTED", "this build only supports single items; initialCount/maxStackSize must be 1");
        if (item.MaxStackSize>1 && (item.UseCount is not null || item.ContrabandLevel>0)) Fail("STACK", "use-count/contraband items must have maxStackSize=1");
        if (item.ContrabandLevel is <0 or >4) Fail("CONTRABAND", "0..4 required");
        if (item.BonusAccuracy is <-100 or >100) Fail("ACCURACY", "-100..100 required");
        if(item.Module is { } module) {
            if(string.IsNullOrEmpty(module.Type)||!ValidKey(module.Type)||module.Performance is <-100000 or >100000||
                module.Efficiency is <-100000 or >100000||module.Quality is <-100000 or >100000||module.Tier is <1 or >100||
                module.AdditionalEffectDisplay is null||module.AdditionalEffectDisplay.Length>4000)
                Fail("MODULE","valid native/custom type, finite bounded integer stats, tier1..100, display<=4000 required");
        }
        if(item.Module is not null||item.Node) {
            if(item.Module is not null&&item.Node)Fail("MODULE","module and node are separate native initializers; choose one");
            if(item.Food is not null||item.LiquidContainer is not null||item.Container is not null||item.Activation is not null||item.UseCount is not null)
                Fail("MODULE","module/node cannot combine with consumable/container/activation/useCount");
            if(item.Tags is not null&&item.Tags.Any(t=>t is not null&&(t.Id.StartsWith("MODULE_",StringComparison.Ordinal)||t.Id.StartsWith("NODE_",StringComparison.Ordinal))))
                Fail("MODULE","module/node helper-owned tags cannot be overridden in tags");
        }
        if (item.Types is null || item.Types.Count>32 || item.Types.Any(t=>t is null || !ValidKey(t)) || item.Types.Distinct(StringComparer.Ordinal).Count()!=item.Types.Count) Fail("TYPE", "max 32 unique native keys or namespaced custom IDs");
        if(item.TypeNames is null||item.TypeNames.Count>32)Fail("TYPE", "max 32 typeNames");
        var typeNames=new HashSet<string>(StringComparer.Ordinal);
        foreach(var type in item.TypeNames!) {
            if(type is null||type.Id is null||!ValidKey(type.Id)||!type.Id.Contains('.')||!item.Types!.Contains(type.Id)||!typeNames.Add(type.Id)||
                (string.IsNullOrWhiteSpace(type.Name)&&type.LocalizedName is null)||type.Name?.Length>200)Fail("TYPE", "typeNames must provide unique readable names for declared custom types");
        }
        if(item.Types!.Any(t=>t.Contains('.')&&!typeNames.Contains(t)))Fail("TYPE", "custom types require typeNames to avoid missing translations");
        if (item.Tags is null || item.Tags.Count>64) Fail("TAG", "max 64 tags");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in item.Tags!) { ValidateTag(tag); if (!ids.Add(tag.Id)) Fail("TAG", "duplicate "+tag.Id); }
        if (item.ShapeRows is not null)
        {
            if (!item.Width.HasValue || item.ShapeRows.Count!=(item.Height??1) || item.ShapeRows.Any(r=>r is null || r.Length!=item.Width || r.Any(c=>c is not ('0' or '1'))) || !item.ShapeRows.Any(r=>r.Contains('1')))
                Fail("SHAPE", "shapeRows must match width/height, contain 0/1, and have a filled cell");
        }
        if (item.UseCount is { } use)
        {
            if (use.Maximum is <1 or >9999 || use.Current is <1 || use.Current>use.Maximum || use.BaseValue<0 || use.ValuePerUse<0 || (long)use.BaseValue+(long)use.ValuePerUse*use.Maximum>1000000000) Fail("USE_COUNT", "invalid count or total price");
            if (!use.AutoValue && (use.BaseValue!=0 || use.ValuePerUse!=0)) Fail("USE_COUNT", "pricing requires autoValue=true");
        }
        if (item.Food is { } food)
        {
            if (food.Calories is <1 or >100000) Fail("FOOD", "calories must be 1..100000");
            if (food.Freshness is <0 or >100) Fail("FOOD", "freshness must be 0..100");
        }
        if (item.LiquidContainer is { } liquid && liquid.Capacity is <1 or >100000)
            Fail("LIQUID", "capacity must be 1..100000 ml");
        if (item.Container is { } container) {
            if(container.Width is <1 or >12 || container.Height is <1 or >12)
                Fail("CONTAINER", "internal width/height must be 1..12 cells");
            if(container.Title is not null && (string.IsNullOrWhiteSpace(container.Title)||container.Title.Length>200||container.Title.Any(char.IsControl)))
                Fail("CONTAINER", "title must be readable <=200 characters without controls");
            if(container.ShapeRows is { } rows && (rows.Count!=container.Height||rows.Any(r=>r is null||r.Length!=container.Width||r.Any(c=>c is not ('0' or '1')))||!rows.Any(r=>r.Contains('1'))))
                Fail("CONTAINER", "internal shapeRows must match width/height and contain filled cells");
            foreach(var list in new[]{container.AllowedTags,container.AllowedIds})
                if(list is null||list.Count>64||list.Any(id=>id is null||id.Length>160||!Regex.IsMatch(id,@"^[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*\z"))||list.Distinct(StringComparer.Ordinal).Count()!=list.Count)
                    Fail("CONTAINER", "filters require <=64 unique native keys or item IDs, each <=160 characters");
            if(container.AllowedTags.Count>0&&container.AllowedIds.Count>0)
                Fail("CONTAINER", "choose allowedTags or allowedIds, not both");
            if(item.Food is not null||item.LiquidContainer is not null||item.Activation is not null||item.UseCount is not null)
                Fail("CONTAINER", "container cannot combine with food/liquidContainer/activation/useCount");
        }
        if (item.Food is not null && item.LiquidContainer is not null)
            Fail("CONSUMABLE", "food and liquidContainer cannot be combined on one item");
        if ((item.Food is not null || item.LiquidContainer is not null) && (item.Activation is not null || item.UseCount is not null))
            Fail("CONSUMABLE", "native food/liquid behavior cannot be combined with activation/useCount");
        if (item.Activation is { } activation)
        {
            if (activation.Kind!="healWounds" || activation.Consumption is not ("destroy" or "useCount"))
                Fail("ACTIVATION", "kind=healWounds; consumption=destroy/useCount required");
            if (activation.Charge is <1 or >9999) Fail("ACTIVATION", "charge must be 1..9999");
            if (activation.Consumption=="destroy" && (item.UseCount is not null || activation.Charge!=1))
                Fail("ACTIVATION", "destroy requires charge=1 and no useCount; use consumption=useCount for rechargeable items");
            if (activation.Consumption=="useCount" && (item.UseCount is null || activation.Charge>item.UseCount.Maximum))
                Fail("ACTIVATION", "useCount consumption requires useCount with maximum >= charge");
            if (item.ForceDisableActivate==true || item.ForceDisableUse==true)
                Fail("ACTIVATION", "configured behavior conflicts with forceDisableActivate/forceDisableUse=true");
        }
        if (item.Sounds is { } sound) foreach (var text in new[]{sound.DragStart,sound.DragEnd,sound.ContainerOpen,sound.ContainerClose,sound.Select,sound.ExamineOpen,sound.Toggle,sound.Activate,sound.Use})
            if (text is not null && (string.IsNullOrWhiteSpace(text) || text.Length>160 || text.Any(char.IsControl))) Fail("SOUND", "expected existing game sound ID <=160 characters");
        if (item.Tooltip is null || item.Tooltip.Labels is null || item.Tooltip.Lines is null) Fail("TOOLTIP", "null is not allowed");
        var tip=item.Tooltip!;
        if (tip.SourceName is not null && (string.IsNullOrWhiteSpace(tip.SourceName) || tip.SourceName.Length>200)) Fail("TOOLTIP", "sourceName must be readable <=200 characters");
        if (tip.Labels.Count>32 || tip.Lines.Count>32) Fail("TOOLTIP", "max 32 labels/lines");
        foreach (var label in tip.Labels)
        {
            if (label is null || (string.IsNullOrWhiteSpace(label.Text)&&label.LocalizedText is null) || label.Text?.Length>200) Fail("TOOLTIP", "label text or localizedText required <=200 characters");
            if (label!.ValueKind is not ("none" or "string" or "int" or "long" or "float" or "double" or "bool") || (label.ValueKind!="none" && label.TagId is null)) Fail("TOOLTIP", "valueKind needs a bound tagId");
            if (label.TagId is not null && !ids.Contains(label.TagId)) Fail("TOOLTIP", "tagId must be declared in tags");
            ValidateColor(label.Color);
        }
        foreach (var line in tip.Lines)
        {
            if (line is null || (string.IsNullOrWhiteSpace(line.Text)&&line.LocalizedText is null) || line.Text?.Length>2000 || line.Position is not ("early" or "late")) Fail("TOOLTIP", "line text or localizedText <=2000, position early/late required");
            ValidateColor(line!.Color);
        }
    }

    /// <summary>大小写敏感：原生键如 TOOL，自定义键如 author.mod.level。</summary>
    public static bool ValidKey(string key)=>key.Length<=160 && (Regex.IsMatch(key,@"^[A-Z][A-Z0-9_]*$") || Regex.IsMatch(key,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)+$"));
    public static void ValidateTag(ItemTagDefinition tag)
    {
        if (tag is null || tag.Id is null || !ValidKey(tag.Id)) Fail("TAG", "expected native UPPER_CASE key or lowercase namespaced ID");
        if (tag!.StringValue?.Length>4000 || (tag.FloatValue.HasValue && !float.IsFinite(tag.FloatValue.Value)) || (tag.DoubleValue.HasValue && !double.IsFinite(tag.DoubleValue.Value))) Fail("TAG", "finite numbers and string <=4000 required");
    }
    internal static readonly string[] Colors = {"None","White","VeryLightGray","LightGray","Gray","DarkGray","VeryDarkGray","Black","LightPurple","Purple","DarkPurple","VeryLightRed","LightRed","Red","DarkRed","VeryDarkRed","LightOrange","Orange","DarkOrange","LightYellow","Yellow","DarkYellow","VeryLightGreen","LightGreen","Green","DarkGreen","VeryDarkGreen","LightTurquoise","Turquoise","DarkTurquoise","VeryLightCyan","LightCyan","Cyan","DarkCyan","VeryDarkCyan","LightBlue","Blue","DarkBlue","EGreen","ERed"};
    private static void ValidateColor(string color) { if (!Colors.Contains(color, StringComparer.Ordinal)) Fail("COLOR", "use palette name from item-properties.index.json"); }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code, string message)=>throw new ArgumentException("EZAPI_ITEM_"+code+": "+message);
}

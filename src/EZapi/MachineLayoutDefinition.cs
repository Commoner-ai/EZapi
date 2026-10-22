using System.Text.Json;
using System.Text.RegularExpressions;
namespace EZapi;

/// <summary>内部格数，与机器物品的外部占格独立。宽高各1..12。</summary>
public sealed record MachineGrid(int Width, int Height);
/// <summary>单件容器槽。过滤按ID或标签二选一；空列表保留原生液体容器限制。</summary>
public sealed partial class MachineContainerSlot
{
    public string SlotId { get; init; } = "";
    public MachineGrid Size { get; init; } = new(2, 2);
    public string Label { get; init; } = "";
    /// <summary>内联双语槽标签，优先于Label。独立布局没有内容包目录，不接受CSV Key。</summary>
    public LocalizedItemText? LocalizedLabel { get; init; }
    public IReadOnlyList<string> AllowedIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllowedTags { get; init; } = Array.Empty<string>();
}
/// <summary>Required=true时加工必须有有效电源，即使配方电耗为零；false允许零耗电时空槽，正电耗仍需足够能源。</summary>
public sealed record MachineBatteryTemplate(MachineGrid Size, bool Required = true);
public sealed class MachineModuleTemplate
{
    public MachineGrid Size { get; init; } = new(4, 4);
    /// <summary>原样传给官方SetupModuleBay；默认只允许通用模块，不把空值解释成任意模块。</summary>
    public IReadOnlyList<string> AllowedTypes { get; init; } = new[] { "MODULE_TYPE_UNIVERSAL" };
}
public sealed partial class MachineOutputTemplate
{
    /// <summary>items=物品网格，container=单件液体容器槽。</summary>
    public string Kind { get; init; } = "items";
    public MachineGrid Size { get; init; } = new(4, 4);
    /// <summary>液体产物容器槽的双语标签；省略时按游戏语言显示框架默认文案。items网格不显示此标签。</summary>
    public LocalizedItemText? LocalizedLabel { get; init; }
}
/// <summary>原生机器窗口与槽位声明。仅建立布局；不注册配方、不调度生产、不扣电。</summary>
public sealed class MachineLayoutDefinition
{
    public string? Title { get; init; }
    /// <summary>内联双语窗口标题，优先于Title。省略两者时使用当前物品名称。</summary>
    public LocalizedItemText? LocalizedTitle { get; init; }
    public bool Draggable { get; init; } = true;
    public MachineGrid ItemInput { get; init; } = new(4, 4);
    public MachineOutputTemplate Output { get; init; } = new();
    public IReadOnlyList<MachineContainerSlot> LiquidInputs { get; init; } = Array.Empty<MachineContainerSlot>();
    public MachineBatteryTemplate? Battery { get; init; }
    public MachineModuleTemplate? Modules { get; init; }
    public bool ManualSlot { get; init; }
    /// <summary>可选电池槽双语标签；省略时中文“电池”、其它语言“Battery”。</summary>
    public LocalizedItemText? LocalizedBatteryLabel { get; init; }
    /// <summary>可选说明书槽双语标签；省略时中文“说明书”、其它语言“Manual”。</summary>
    public LocalizedItemText? LocalizedManualLabel { get; init; }
}

/// <summary>与CLI共用的纯托管规则；未知字段、重复字段、null及超限在原生分配前拒绝。</summary>
public static partial class JsonMachineLayout
{
    private static readonly JsonSerializerOptions Options=new() {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull};
    /// <summary>输出可再次严格Parse的JSON；可选字段省略null。验证完成后才序列化。</summary>
    public static string Serialize(MachineLayoutDefinition definition){Validate(definition);return JsonSerializer.Serialize(definition,Options);}
    public static MachineLayoutDefinition Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = json.Read(buffer, 0, buffer.Length)) > 0) {
            if (bytes.Length + count > 65536) throw new InvalidDataException("EZAPI_MACHINE_LAYOUT_LIMIT: max65536 bytes.");
            bytes.Write(buffer, 0, count);
        }
        using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
        JsonItems.ValidateObject(document.RootElement, typeof(MachineLayoutDefinition), "machineLayout");
        var value = JsonSerializer.Deserialize<MachineLayoutDefinition>(document.RootElement.GetRawText(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        Validate(value); return value;
    }
    public static void Validate(MachineLayoutDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Grid(definition.ItemInput); Grid(definition.Output?.Size);
        if (definition.Output!.Kind is not ("items" or "container")) Fail("output.kind must be items/container");
        if (definition.Title is not null && (string.IsNullOrWhiteSpace(definition.Title) || definition.Title.Length > 200 || definition.Title.Any(char.IsControl))) Fail("readable title<=200 required");
        Text(definition.LocalizedTitle);Text(definition.Output.LocalizedLabel);
        Text(definition.LocalizedBatteryLabel);Text(definition.LocalizedManualLabel);
        if (definition.Battery is { } battery) Grid(battery.Size);
        if (definition.Modules is { } modules) { Grid(modules.Size); Keys(modules.AllowedTypes); }
        if (definition.LiquidInputs is null || definition.LiquidInputs.Count > 8) Fail("max8 liquid inputs");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in definition.LiquidInputs!) {
            if (slot is null || slot.SlotId is null || !Regex.IsMatch(slot.SlotId, @"^[a-z][a-z0-9_-]{0,39}\z") || !ids.Add(slot.SlotId)) Fail("unique lowercase liquid slot IDs<=40 required");
            Grid(slot!.Size); Keys(slot.AllowedIds); Keys(slot.AllowedTags);
            if (slot.AllowedIds.Count > 0 && slot.AllowedTags.Count > 0) Fail("choose allowedIds or allowedTags");
            if (slot.Label is null || slot.Label.Length > 200 || slot.Label.Any(char.IsControl)) Fail("liquid label<=200 without controls required");
            Text(slot.LocalizedLabel);
        }
        ValidateRuntimeConditions(definition);
        if(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(definition,Options))>65536)Fail("serialized layout must fit65536 UTF-8 bytes");
    }
    // 制作独立快照，避免作者在工厂创建后修改集合，使恢复布局与声明不一致。
    internal static MachineLayoutDefinition Copy(MachineLayoutDefinition d)
    {
        Validate(d);
        var result=new MachineLayoutDefinition { Title=d.Title, LocalizedTitle=d.LocalizedTitle, Draggable=d.Draggable, ItemInput=d.ItemInput with {},
            Output=new() {Kind=d.Output.Kind, Size=d.Output.Size with {},LocalizedLabel=d.Output.LocalizedLabel}, Battery=d.Battery is null?null:d.Battery with {Size=d.Battery.Size with {}},
            Modules=d.Modules is null?null:new() {Size=d.Modules.Size with {}, AllowedTypes=Array.AsReadOnly(d.Modules.AllowedTypes.ToArray())}, ManualSlot=d.ManualSlot,
            LocalizedBatteryLabel=d.LocalizedBatteryLabel,LocalizedManualLabel=d.LocalizedManualLabel,
            LiquidInputs=Array.AsReadOnly(d.LiquidInputs.Select(s=>new MachineContainerSlot {SlotId=s.SlotId,Size=s.Size with {},Label=s.Label,LocalizedLabel=s.LocalizedLabel,
                AllowedIds=Array.AsReadOnly(s.AllowedIds.ToArray()),AllowedTags=Array.AsReadOnly(s.AllowedTags.ToArray())}).ToArray()) };
        CopyRuntimeConditions(d,result);return result;
    }
    // CLI只处理声明式JSON；游戏DLL在独立partial文件中复制/校验不可序列化的C#委托。
    static partial void ValidateRuntimeConditions(MachineLayoutDefinition definition);
    static partial void CopyRuntimeConditions(MachineLayoutDefinition source,MachineLayoutDefinition target);
    private static void Text(LocalizedItemText? text)
    {
        if(text is null)return;
        text.Validate(200);
        if(new[]{text.Chinese,text.English}.Any(s=>s is not null&&s.Any(char.IsControl)))Fail("localized layout title/labels must not contain controls");
    }
    private static void Grid(MachineGrid? grid) { if(grid is null || grid.Width is <1 or >12 || grid.Height is <1 or >12) Fail("internal grid dimensions1..12 required"); }
    private static void Keys(IReadOnlyList<string>? keys)
    {
        if(keys is null || keys.Count>64 || keys.Any(s=>s is null || s.Length>160 || !Regex.IsMatch(s,@"^[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*\z")) || keys.Distinct(StringComparer.Ordinal).Count()!=keys.Count) Fail("max64 unique native/custom keys required");
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string text)=>throw new ArgumentException("EZAPI_MACHINE_LAYOUT: "+text);
}

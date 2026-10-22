using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Il2Cpp;
namespace EZapi;

/// <summary>单次同步加工借用槽位；不保存实例。核对真实父图，独立复制映射，回调后重查身份与模板。</summary>
internal sealed class MachineInventoryBinding
{
    internal MachineInventory Slots {get;}
    private readonly MachineLayoutDefinition? _template;
    private readonly bool _canonical;
    private readonly IntPtr _window;
    private MachineInventoryBinding(MachineInventory slots,MachineLayoutDefinition? template,bool canonical,IntPtr window)
        =>(Slots,_template,_canonical,_window)=(slots,template,canonical,window);

    internal static MachineInventoryBinding? Resolve(GameItem machine,MachineInventory? supplied,MachineLayoutDefinition? template)
    {
        var canonical=MachineLayoutApi.TryGetInventory(machine,out var native);
        var source=supplied??(canonical?native:null);
        if(source is null||source.LiquidInputs is null||source.LiquidInputs.Count>8)return null;
        var liquids=new Dictionary<string,GameSlotInventory>(StringComparer.Ordinal);
        foreach(var entry in source.LiquidInputs) {
            if(entry.Key is null||!Regex.IsMatch(entry.Key,@"^[a-z][a-z0-9_-]{0,39}\z")||entry.Value is null||!liquids.TryAdd(entry.Key,entry.Value))return null;
        }
        var snapshot=source with {LiquidInputs=new ReadOnlyDictionary<string,GameSlotInventory>(liquids.OrderBy(e=>e.Key,StringComparer.Ordinal).ToDictionary(e=>e.Key,e=>e.Value,StringComparer.Ordinal)),
            RequiresBattery=source.RequiresBattery||template?.Battery?.Required==true};
        // 自有布局不可通过自造view绕过Required、交换角色或省略槽位；原版/自定义窗口允许显式借用。
        if(canonical&&!SameSlots(source,native))return null;
        var binding=new MachineInventoryBinding(snapshot,template,canonical,machine.contentWindow?.Pointer??IntPtr.Zero);
        return binding.Current(machine)?binding:null;
    }

    internal bool Current(GameItem machine)
    {
        var slots=Inventories(Slots).ToArray();
        if(_window==IntPtr.Zero||machine.contentWindow?.Pointer!=_window||Slots.ItemInput is null||Slots.Output is null||
            slots.Any(s=>s is null||s.Pointer==IntPtr.Zero||s.GetParentItem()?.Pointer!=machine.Pointer)||
            slots.Select(s=>s.Pointer).Distinct().Count()!=slots.Length)return false;
        if(_canonical&&(!MachineLayoutApi.TryGetInventory(machine,out var now)||!SameSlots(Slots,now,compareRequired:false)||now.RequiresBattery&&!Slots.RequiresBattery))return false;
        if(!_canonical&&MachineLayoutApi.TryGetInventory(machine,out _))return false;
        return _template is null||MatchesTemplate(Slots,_template);
    }

    internal bool AllowsOutput(GameItem item)=>_template?.Output.ContainerCondition?.Invoke(item)??true;
    internal bool AllowsLiquid(string id,GameItem item)
    {
        if(_template is null)return true;
        var spec=_template.LiquidInputs.Single(s=>s.SlotId==id);
        return (spec.AllowedIds.Count==0||spec.AllowedIds.Contains(item.identifier,StringComparer.Ordinal))&&
            (spec.AllowedTags.Count==0||spec.AllowedTags.Any(item.IsTag))&&(spec.Condition?.Invoke(item)??true);
    }
    private static bool SameSlots(MachineInventory a,MachineInventory b,bool compareRequired=true)=>
        a.ItemInput?.Pointer==b.ItemInput?.Pointer&&a.Output?.Pointer==b.Output?.Pointer&&a.Battery?.Pointer==b.Battery?.Pointer&&
        a.Modules?.Pointer==b.Modules?.Pointer&&a.Manual?.Pointer==b.Manual?.Pointer&&(!compareRequired||a.RequiresBattery==b.RequiresBattery)&&
        a.LiquidInputs.Count==b.LiquidInputs.Count&&a.LiquidInputs.All(e=>b.LiquidInputs.TryGetValue(e.Key,out var other)&&e.Value?.Pointer==other?.Pointer);
    private static bool MatchesTemplate(MachineInventory s,MachineLayoutDefinition t)=>
        Grid(s.ItemInput,t.ItemInput)&&
        (t.Output.Kind=="items"?Grid(s.Output,t.Output.Size):Slot(s.Output,t.Output.Size))&&
        (t.Battery is null?s.Battery is null:s.Battery is not null&&Slot(s.Battery,t.Battery.Size))&&
        (t.Modules is null?s.Modules is null:s.Modules is not null&&Grid(s.Modules,t.Modules.Size))&&
        (t.ManualSlot?s.Manual is not null&&Slot(s.Manual,new(1,1)):s.Manual is null)&&
        s.LiquidInputs.Count==t.LiquidInputs.Count&&t.LiquidInputs.All(l=>s.LiquidInputs.TryGetValue(l.SlotId,out var actual)&&Slot(actual,l.Size));
    private static bool Grid(GameInventory i,MachineGrid size)=>i.TryCast<GameGridInventory>() is {} grid&&
        grid.inventoryShape.globalWidth==size.Width&&grid.inventoryShape.globalHeight==size.Height;
    private static bool Slot(GameInventory i,MachineGrid size)=>i.TryCast<GameSlotInventory>() is {} slot&&
        slot.defaultGridWidth==size.Width&&slot.defaultGridHeight==size.Height;
    private static IEnumerable<GameInventory> Inventories(MachineInventory s)
    {
        yield return s.ItemInput;yield return s.Output;if(s.Battery is {} b)yield return b;if(s.Modules is {} m)yield return m;
        if(s.Manual is {} n)yield return n;foreach(var v in s.LiquidInputs.Values)yield return v;
    }
}

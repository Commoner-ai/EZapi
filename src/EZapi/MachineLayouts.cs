using System.Collections.ObjectModel;
using System.Text.Json;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
namespace EZapi;

/// <summary>当前同步调用的原生槽位引用。不可跨场景/读档缓存；每次重新TryGetInventory。</summary>
public sealed record MachineInventory(GameSlotInventory? Battery, GameInventory? Modules, GameInventory ItemInput,
    GameInventory Output, IReadOnlyDictionary<string, GameSlotInventory> LiquidInputs, GameSlotInventory? Manual)
{
    /// <summary>当前布局要求电源在场；即使配方零电耗也需原生认可的可用电源。独立构造的借用视图默认false。</summary>
    public bool RequiresBattery {get;init;}
}

/// <summary>
/// 在每次物品目录工厂中创建独立窗口。电池(0,1)/模块(1,1)遵循当前官方MachineHelper布局。
/// 只按本框架精确标记查找；不扫描、更换或修补原版机器窗口，不持有原生实例缓存。
/// </summary>
public static class MachineLayoutApi
{
    internal const string LayoutTag="EZAPI_MACHINE_LAYOUT_V1";
    private static bool _started;
    private static long _created,_admissionErrors;
    internal static void Start(){_started=true;MachineContainerConditionApi.Start();}
    internal static void Stop(){_started=false;MachineContainerConditionApi.Stop();}
    internal static object Report()=>new{ready=_started,created=_created,admissionErrors=_admissionErrors,holdsNativeInstances=false,processingImplemented=false};
    public static MachineInventory Create(GameItem machine, MachineLayoutDefinition definition)
    {
        GameSession.RequireThread();
        if(!_started)throw new InvalidOperationException("EZAPI_MACHINE_LAYOUT_NOT_READY: initialize EZapi first.");
        var spec=JsonMachineLayout.Copy(definition);
        if(machine is null || machine.Pointer==IntPtr.Zero || machine.unitCount!=1 || machine.parentInventory is not null || machine.contentWindow is not null)
            throw new ArgumentException("EZAPI_MACHINE_LAYOUT_OWNER: detached single item without a content window required.");
        if(string.IsNullOrWhiteSpace(machine.identifier) || machine.identifier.Length>160 || machine.identifier.Any(char.IsControl))
            throw new ArgumentException("EZAPI_MACHINE_LAYOUT_ID: stable item ID<=160 required.");
        var stored=Stored(machine);
        var serialized=JsonMachineLayout.Serialize(spec);
        MachineContainerConditionApi.RequireRegistered(machine.identifier,spec);
        if(stored is not null && stored!=serialized)throw new InvalidOperationException("EZAPI_MACHINE_LAYOUT_REVISION: saved layout differs; explicit migration required.");
        // 先接到owner图；之后出错由物品工厂销毁整个未交付实例，不遗留游离UI节点。
        var locale=NativeItemLocalization.CurrentLocaleCode;
        var title=Text(spec.LocalizedTitle,spec.Title??machine.name,locale);
        var window=new PixelWindow(spec.Draggable,title);
        if(!NativeWindowTitles.Matches(window,title))NativeWindowTitles.Set(window,title);
        machine.SetContentWindow(window);
        var root=new GridPixelElement(2,2,false).SetSpacing(4,4);
        if(!window.Attach(root.Cast<PixelElement>()))throw new InvalidOperationException("EZAPI_MACHINE_LAYOUT_ATTACH: root refused.");
        var input=Grid(spec.ItemInput,Key(machine,"input")); Attach(root,input.Cast<PixelElement>(),0,0);
        // 右上区域固定为竖排：产物、液体输入、说明书。增加槽位不改变电池/模块官方位置。
        var right=new GridPixelElement(1,1+spec.LiquidInputs.Count+(spec.ManualSlot?1:0),false).SetSpacing(4,4);
        Attach(root,right.Cast<PixelElement>(),1,0);
        GameInventory output=spec.Output.Kind=="items"?Grid(spec.Output.Size,Key(machine,"output")):Slot(spec.Output.Size,Key(machine,"output"),Text(spec.Output.LocalizedLabel,Default(locale,"产物容器","Output container"),locale));
        Attach(right,output.Cast<PixelElement>(),0,0);
        GameSlotInventory? battery=null,manual=null; GameGridInventory? modules=null;
        if(spec.Battery is { } batterySpec) {
            battery=Slot(batterySpec.Size,Key(machine,"battery"),Text(spec.LocalizedBatteryLabel,Default(locale,"电池","Battery"),locale)); Attach(root,battery.Cast<PixelElement>(),0,1);
            MachineHelper.SetupBatterySlot(battery,machine);
        }
        if(spec.Modules is { } moduleSpec) {
            modules=Grid(moduleSpec.Size,Key(machine,"modules")); Attach(root,modules.Cast<PixelElement>(),1,1);
            MachineHelper.SetupModuleBay(modules,machine,null,new Il2CppStringArray(moduleSpec.AllowedTypes.ToArray()));
        }
        var liquids=new Dictionary<string,GameSlotInventory>(StringComparer.Ordinal);
        for(var i=0;i<spec.LiquidInputs.Count;i++) {
            var s=spec.LiquidInputs[i]; var slot=Slot(s.Size,Key(machine,"liquid."+s.SlotId),Text(s.LocalizedLabel,s.Label,locale));
            Attach(right,slot.Cast<PixelElement>(),0,i+1); liquids.Add(s.SlotId,slot);
        }
        if(spec.ManualSlot) {
            manual=Slot(new(1,1),Key(machine,"manual"),Text(spec.LocalizedManualLabel,Default(locale,"说明书","Manual"),locale)); Attach(right,manual.Cast<PixelElement>(),0,1+spec.LiquidInputs.Count);
            MachineHelper.SetupManualSlot(manual);
        }
        ContainerHelper.InitContainerItemTagOnly(machine);
        if(stored is null) {
            machine.state??=new TagSystem(); machine.state.GetTag(LayoutTag).Enable().SetString(serialized);machine.SyncModifiedState();
            if(Stored(machine)!=serialized)throw new InvalidOperationException("EZAPI_MACHINE_LAYOUT_STATE: saved marker readback differs.");
        }
        var inventory=new MachineInventory(battery,modules,input,output,new ReadOnlyDictionary<string,GameSlotInventory>(liquids),manual){RequiresBattery=spec.Battery?.Required==true};
        VerifyParents(machine,inventory);
        ApplyText(machine,inventory,spec,locale);
        _created++;
        return inventory;
    }

    /// <summary>读取原生存储图并核对完整布局与父物品。普通物品返回false；损坏标记/缺槽明确抛错。</summary>
    public static bool TryGetInventory(GameItem machine,out MachineInventory inventory)
    {
        GameSession.RequireThread();inventory=null!;
        if(machine is null || machine.Pointer==IntPtr.Zero)return false;
        var json=Stored(machine);if(json is null)return false;
        using var bytes=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));var spec=JsonMachineLayout.Parse(bytes);
        inventory=ReadInventory(machine,spec);return true;
    }

    /// <summary>
    /// 按当前游戏语言刷新本框架机器窗口标题和槽标签。显示/重新打开时自动调用；自有常开窗口可主动调用。
    /// 普通物品或框架停用返回false；先核对完整槽图再更新文字，不重建窗口、不写布局标记、库存或存档。
    /// </summary>
    public static bool RefreshLocalization(GameItem machine)
    {
        GameSession.RequireThread();
        if(!_started||machine is null||machine.Pointer==IntPtr.Zero)return false;
        var json=Stored(machine);if(json is null)return false;
        using var bytes=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));var spec=JsonMachineLayout.Parse(bytes);
        var inventory=ReadInventory(machine,spec);
        ApplyText(machine,inventory,spec,NativeItemLocalization.CurrentLocaleCode);return true;
    }

    private static MachineInventory ReadInventory(GameItem machine,MachineLayoutDefinition spec)
    {
        var root=machine.contentWindow?.childElement?.TryCast<GridPixelElement>();
        if(root is null || root.gridWidth!=2 || root.gridHeight!=2)throw Broken("root");
        var right=root.GetElement(1,0)?.TryCast<GridPixelElement>();
        if(right is null || right.gridWidth!=1 || right.gridHeight!=1+spec.LiquidInputs.Count+(spec.ManualSlot?1:0))throw Broken("right column");
        var input=RequireGrid(root.GetElement(0,0),machine,"input",spec.ItemInput);
        GameInventory output=spec.Output.Kind=="items"?RequireGrid(right.GetElement(0,0),machine,"output",spec.Output.Size):RequireSlot(right.GetElement(0,0),machine,"output",spec.Output.Size);
        var battery=spec.Battery is null?null:RequireSlot(root.GetElement(0,1),machine,"battery",spec.Battery.Size);
        var modules=spec.Modules is null?null:RequireGrid(root.GetElement(1,1),machine,"modules",spec.Modules.Size);
        var liquids=new Dictionary<string,GameSlotInventory>(StringComparer.Ordinal);
        for(var i=0;i<spec.LiquidInputs.Count;i++) {var s=spec.LiquidInputs[i];liquids.Add(s.SlotId,RequireSlot(right.GetElement(0,i+1),machine,"liquid."+s.SlotId,s.Size));}
        var manual=spec.ManualSlot?RequireSlot(right.GetElement(0,1+spec.LiquidInputs.Count),machine,"manual",new(1,1)):null;
        var inventory=new MachineInventory(battery,modules,input,output,new ReadOnlyDictionary<string,GameSlotInventory>(liquids),manual){RequiresBattery=spec.Battery?.Required==true};
        VerifyParents(machine,inventory);return inventory;
    }

    // 单次刷新只观察一次语言；保留官方helper设置的过滤/模块回调，仅更新可见文字。
    private static void ApplyText(GameItem machine,MachineInventory inventory,MachineLayoutDefinition spec,string locale)
    {
        var title=Text(spec.LocalizedTitle,spec.Title??machine.name,locale);
        var window=machine.contentWindow??throw Broken("window");
        if(!NativeWindowTitles.Matches(window,title))NativeWindowTitles.Set(window,title);
        if(spec.Output.Kind=="container")inventory.Output.Cast<GameSlotInventory>().SetTooltipName(Text(spec.Output.LocalizedLabel,Default(locale,"产物容器","Output container"),locale));
        inventory.Battery?.SetTooltipName(Text(spec.LocalizedBatteryLabel,Default(locale,"电池","Battery"),locale));
        inventory.Manual?.SetTooltipName(Text(spec.LocalizedManualLabel,Default(locale,"说明书","Manual"),locale));
        foreach(var slot in spec.LiquidInputs)inventory.LiquidInputs[slot.SlotId].SetTooltipName(Text(slot.LocalizedLabel,slot.Label,locale));
    }
    private static string Text(LocalizedItemText? text,string fallback,string locale)=>text?.ForLocale(locale)??fallback;
    private static string Default(string locale,string chinese,string english)=>locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?chinese:english;
    // 原生委托接收器按当前父图路由；只有EZapi创建的精确子库存被限制。无闭包和跨场景缓存。
    internal static bool Allows(GameItem item,GameInventory inventory)
    {
        try {
            if(!_started)return true;
            if(!GameSession.OnThread)return false;
            var owner=inventory.GetParentItem();if(owner is null || Stored(owner) is not { } json)return true;
            if(inventory.identifier==Key(owner,"output"))return inventory.TryCast<GameSlotInventory>() is null ||
                IsLiquid(item)&&MachineContainerConditionApi.Allows(item,inventory,owner,json,"output");
            var prefix=Key(owner,"liquid.");if(!inventory.identifier.StartsWith(prefix,StringComparison.Ordinal))return true;
            if(!IsLiquid(item))return false;
            using var bytes=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));var spec=JsonMachineLayout.Parse(bytes);
            var s=spec.LiquidInputs.SingleOrDefault(s=>Key(owner,"liquid."+s.SlotId)==inventory.identifier);
            return s is not null && (s.AllowedIds.Count==0||s.AllowedIds.Contains(item.identifier,StringComparer.Ordinal)) &&
                (s.AllowedTags.Count==0||s.AllowedTags.Any(t=>item.GetTagReadonly(t)?.IsEnabled()==true))&&
                MachineContainerConditionApi.Allows(item,inventory,owner,json,"liquid."+s.SlotId);
        } catch(Exception e) {_admissionErrors++;if(_admissionErrors==1||_admissionErrors%100==0)MelonLoader.MelonLogger.Warning("[EZapi] EZAPI_MACHINE_LAYOUT_ADMISSION: "+e.Message);return false;}
    }
    private static bool IsLiquid(GameItem item)=>item is not null && item.Pointer!=IntPtr.Zero && item.unitCount==1 && item.IsTag("LIQUID_CONTAINER_TAG") &&
        (long)WaterHelper.GetFreeCapacity(item)+WaterHelper.GetTotalVolume(item)>0;
    private static GameGridInventory Grid(MachineGrid size,string id)=>new(size.Width,size.Height) {identifier=id};
    private static GameSlotInventory Slot(MachineGrid size,string id,string label)
    {
        var slot=new GameSlotInventory(size.Width,size.Height,false,false) {identifier=id};slot.SetTooltipName(label);
        slot.mayInventoryAddItemFunc=NativeDelegateBridge.MachineSlot();return slot;
    }
    private static string Key(GameItem machine,string suffix)=>machine.identifier+".ezapi.machine."+suffix;
    private static void Attach(GridPixelElement parent,PixelElement element,int x,int y)
    {if(!parent.AttachPos(element,x,y))throw Broken("attachment");}
    private static string? Stored(GameItem item)
    {
        if(item.state?.dict is not { } dict || !dict.TryGetValue(LayoutTag,out var tag))return null;
        if(tag is null || string.IsNullOrEmpty(tag.valueString) || tag.valueString.Length>65536)throw Broken("saved marker");return tag.valueString;
    }
    private static GameGridInventory RequireGrid(PixelElement? element,GameItem machine,string suffix,MachineGrid size)
    {
        var grid=element?.TryCast<GameGridInventory>();
        if(grid is null || grid.identifier!=Key(machine,suffix) || grid.inventoryShape.globalWidth!=size.Width || grid.inventoryShape.globalHeight!=size.Height)throw Broken(suffix);
        return grid;
    }
    private static GameSlotInventory RequireSlot(PixelElement? element,GameItem machine,string suffix,MachineGrid size)
    {
        var slot=element?.TryCast<GameSlotInventory>();
        if(slot is null || slot.identifier!=Key(machine,suffix) || slot.defaultGridWidth!=size.Width || slot.defaultGridHeight!=size.Height)throw Broken(suffix);
        return slot;
    }
    private static void VerifyParents(GameItem machine,MachineInventory inventory)
    {
        var slots=new List<GameInventory> {inventory.ItemInput,inventory.Output};
        if(inventory.Battery is { } b)slots.Add(b);if(inventory.Modules is { } m)slots.Add(m);if(inventory.Manual is { } manual)slots.Add(manual);slots.AddRange(inventory.LiquidInputs.Values);
        if(slots.Any(i=>i.GetParentItem()?.Pointer!=machine.Pointer) || slots.Select(i=>i.Pointer).Distinct().Count()!=slots.Count)throw Broken("parent graph or repeated slot");
    }
    private static InvalidOperationException Broken(string part)=>new("EZAPI_MACHINE_LAYOUT_GRAPH: invalid "+part+"; no repair or inventory write attempted.");
}

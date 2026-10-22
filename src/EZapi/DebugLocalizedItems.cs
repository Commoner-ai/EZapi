using Il2Cpp;
using System.Text.RegularExpressions;
namespace EZapi;

/// <summary>Retains only explicitly created, detached fixtures during one disposable-run language test.</summary>
internal static class DebugLocalizedItems
{
    private static readonly List<GameItem> Items=new();
    private static GameItem? _box,_renamed,_machine;
    private static string? _layoutMarker;
    private static IntPtr _window,_input;
    private static GameRunIdentity? _run;

    internal static object Verify(string locale,Action<bool,string> check)
    {
        var run=NativeAccess.RequireRun();
        if(_run is not null&&_run!=run)throw new InvalidOperationException("localized item fixture run expired");
        _run=run;
        var english=locale=="en";
        if(Items.Count==0) {
            foreach(var id in new[]{"ezapi.sample.field_kit","ezapi.sample.early_shape","ezapi.sample.late_uses"})Items.Add(ItemApi.Create(id,false));
            _box=ItemApi.Create("ezapi.sample.storage_box",false);Items.Add(_box);
            _renamed=ItemApi.Create("ezapi.sample.field_kit",false);Items.Add(_renamed);_renamed.SetName("Player test name / 玩家自定义名");
            _machine=ItemApi.Create("ezapi.debug.machine_liquid_probe",false);Items.Add(_machine);
            if(!MachineLayoutApi.TryGetInventory(_machine,out var slots))throw new InvalidOperationException("localized machine graph missing");
            _window=_machine.contentWindow.Pointer;_input=slots.ItemInput.Pointer;
            _layoutMarker=_machine.GetTagReadonly(MachineLayoutApi.LayoutTag)?.valueString;
        }
        var kit=Items[0];
        // The patched native display call is the path used for previously created or loaded items.
        var display=kit.GetDisplayName(false);
        check(kit.name==(english?"EZapi Item Property Demo":"EZapi 属性示例工具包")&&display.Contains(kit.name,StringComparison.Ordinal),locale+": native display refreshes an existing JSON item name");
        check(kit.shortDescription==(english?"Short description: Checks item properties and tags; grants no healing effect.":"短简介：验证物品属性和标签，不提供治疗效果。"),locale+": native short description follows current language");
        var tooltip=Regex.Replace(kit.Cast<GameItemElement>().GetTooltip().nakedText,"<[^>]*>","");
        check(tooltip.Contains(english?"Quality":"品质",StringComparison.Ordinal)&&tooltip.Contains(english?"Owner":"作者",StringComparison.Ordinal),locale+": actual native tooltip labels resolve CSV text");
        check(tooltip.Contains(english?"Early hint:":"前置提示：",StringComparison.Ordinal)&&tooltip.Contains(english?"Late hint:":"后置提示：",StringComparison.Ordinal),locale+": early and late native tooltip lines follow language");
        var early=Regex.Replace(Items[1].Cast<GameItemElement>().GetTooltip().nakedText,"<[^>]*>","");
        var late=Regex.Replace(Items[2].Cast<GameItemElement>().GetTooltip().nakedText,"<[^>]*>","");
        check(early.Contains(english?"Early short description:":"前置短简介：",StringComparison.Ordinal)&&late.Contains(english?"Late short description:":"后置短简介：",StringComparison.Ordinal),locale+": both description placement modes resolve current language");
        _box!.GetDisplayName(false);var boxTitleBeforeRestore=_box.contentWindow.titleString;
        var boxWindow=_box.contentWindow.Pointer;var boxWidth=_box.contentWindow.widthPixels;
        var expectedBoxTitle=english?"EZapi Storage Box":"EZapi 储物箱";
        check(boxTitleBeforeRestore==expectedBoxTitle&&_box.contentWindow.titleText.text.text==expectedBoxTitle,locale+": native container display shows the complete localized title");
        _box.RestoreContentWindowPosition();
        check(_box.contentWindow.titleString==expectedBoxTitle&&_box.contentWindow.titleText.text.text==expectedBoxTitle&&_box.contentWindow.Pointer==boxWindow&&
            _box.contentWindow.widthPixels>=boxWidth,locale+": native container reopen preserves complete title and its window");
        _renamed!.GetDisplayName(false);
        check(_renamed.name=="Player test name / 玩家自定义名",locale+": language refresh preserves arbitrary player name");
        _machine!.GetDisplayName(false);_machine.RestoreContentWindowPosition();
        check(MachineLayoutApi.TryGetInventory(_machine,out var current)&&_machine.contentWindow.Pointer==_window&&current.ItemInput.Pointer==_input,locale+": machine refresh preserves native window and inventories");
        check(_machine.contentWindow.titleString==(english?"EZapi temporary machine check":"EZapi临时机器检查")&&
            current.LiquidInputs["water"].tooltipName==(english?"Water input":"原料水容器")&&
            current.Battery!.tooltipName==(english?"Battery slot":"电池槽")&&
            current.Manual!.tooltipName==(english?"Instructions slot":"说明书槽")&&
            current.Output.Cast<GameSlotInventory>().tooltipName==(english?"Output container":"产物容器"),locale+": native display/reopen refreshes all stored machine labels");
        check(_layoutMarker==_machine.GetTagReadonly(MachineLayoutApi.LayoutTag)?.valueString,locale+": localized refresh leaves saved layout declaration byte-identical");
        check(Items.All(item=>item.parentInventory is null),locale+": all language fixtures remain detached from player inventories");
        return new{locale,display,tooltip,early,late,boxTitleBeforeRestore,boxTitle=_box.contentWindow.titleString,
            boxWidth=_box.contentWindow.widthPixels,boxTitleText=_box.contentWindow.titleText.text.text,
            boxTitleFontSize=_box.contentWindow.titleText.text.fontSize,boxTitleAutoSizing=_box.contentWindow.titleText.text.enableAutoSizing,customName=_renamed.name,
            machineTitle=_machine.contentWindow.titleString,water=current.LiquidInputs["water"].tooltipName,
            battery=current.Battery!.tooltipName,manual=current.Manual!.tooltipName,output=current.Output.Cast<GameSlotInventory>().tooltipName,
            scope="real factories and native display/tooltip/reopen paths; same detached items across en/zh; no disk save/reload claim"};
    }

    internal static void Clear()
    {
        try {
            foreach(var item in Items.AsEnumerable().Reverse()) {
                // A run change invalidates native handles. Do not access old-run instances.
                if(_run is not null&&NativeAccess.SameRun(_run)&&item.parentInventory is null)item.Destroy();
            }
        }finally{Items.Clear();_box=_renamed=_machine=null;_run=null;_layoutMarker=null;_window=_input=IntPtr.Zero;}
    }
}

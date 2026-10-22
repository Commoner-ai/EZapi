using Il2Cpp;
namespace EZapi;

/// <summary>初始化阶段声明自有终端；所有原生分配在官方目录工厂内，复用已有布局/保存/夜间事务。</summary>
internal static class ManufacturingTerminalRuntime
{
    internal static void Register()
    {
        var definition=new ItemDefinition {
            Id=ManufacturingTerminal.ItemId,ModId=ManufacturingTerminal.OwnerId,Name=ManufacturingTerminal.ChineseName,
            Width=ManufacturingTerminal.Width,Height=ManufacturingTerminal.Height,Value=ManufacturingTerminal.BaseValue,
            Types=new[]{"MACHINE"},ShortDescription="跨 Mod 共享配方；每台每晚至多尝试一批。需要材料、电池和产物空间。",
            FlavorText="EZapi · 给创作者留一张工作台。"
        };
        using(var input=typeof(ManufacturingTerminalRuntime).Assembly.GetManifestResourceStream("EZapi.Assets.manufacturing-terminal.png")
            ??throw new InvalidOperationException("EZAPI_TERMINAL_ASSET: embedded icon missing.")) {
            using var bytes=new MemoryStream();input.CopyTo(bytes);LegacySpriteAdapter.CommitEmbedded(definition,bytes.ToArray());
        }
        var result=MachineRegistrationApi.RegisterMachine(ManufacturingTerminal.OwnerId,new(ManufacturingTerminal.ItemId,new(){
            Title=ManufacturingTerminal.ChineseName,ItemInput=new(ManufacturingTerminal.InputWidth,ManufacturingTerminal.InputHeight),
            Output=new(){Size=new(ManufacturingTerminal.OutputWidth,ManufacturingTerminal.OutputHeight)},
            Battery=new(new(2,2)),Modules=new(){Size=new(ManufacturingTerminal.ModuleWidth,ManufacturingTerminal.ModuleHeight),AllowedTypes=new[]{"MODULE_TYPE_FURNACE","MODULE_TYPE_UNIVERSAL"}}
        },Array.Empty<MachineRecipe>()) {
            CreateItem=()=>Create(definition),ProductionMarkupPercent=ManufacturingTerminal.DefaultProductionMarkupPercent,
            Power=new(0,c=>Power.GetMachineCost(c.Machine)),
            ItemOptions=new(definition.Name,definition.ShortDescription,definition.FlavorText,"EZapi"),Guide=new("synthesis"),
            // 固定每晚一批；原生效率只影响电耗，不把性能/品质解释为额外产量。
            EnableNightly=true
        });
        if(!result.Accepted)throw new InvalidOperationException(result.Reason);
    }
    private static GameItem Create(ItemDefinition definition)
    {
        var item=NativeItemFactory.Create(definition);
        try {
            MachineryHelper.InitMachinery(item,ManufacturingTerminal.BasePowerCost,false);
            ModuleHelper.ApplyModifiedPower(item);
            if(item.GetTagReadonly("MACHINERY_DRAWN_POWER_BASE")?.GetInt()!=ManufacturingTerminal.BasePowerCost||
                MachineryHelper.GetMachinePowerUsage(item)!=ManufacturingTerminal.BasePowerCost||!item.IsGameItemType("MACHINE"))
                throw new InvalidOperationException("EZAPI_TERMINAL_NATIVE: base power/type readback differs.");
            return item;
        }catch{item.Destroy();throw;}
    }
}

namespace EZapi;

/// <summary>共享终端的托管声明/配置状态；Registered不等于原生Applied，后者查询MachineRegistrationApi.Snapshot。</summary>
public sealed record ManufacturingTerminalView(bool Registered,string? MarkupOwner,int ProductionMarkupPercent,int RecipeCount);

/// <summary>
/// EZapi自有共享制造终端。内容作者追加自己的配方，所有实例共用目录；库存/窗口/夜次按实例隔离。
/// 不占用或迁移其他框架的ID。使用RegisterAdditionalRecipes(owner,ItemId,recipes)；不内置测试配方。
/// </summary>
public static class ManufacturingTerminal
{
    public const string OwnerId="ezapi.machines";
    public const string ItemId=OwnerId+".universal_manufacturing_terminal";
    public const string ChineseName="EZapi 全域制造终端";
    public const string EnglishName="EZapi Universal Manufacturing Terminal";
    public const int Width=3,Height=3,BaseValue=400;
    public const int InputWidth=9,InputHeight=6,OutputWidth=9,OutputHeight=6,ModuleWidth=7,ModuleHeight=5;
    public const int BasePowerCost=25,DefaultProductionMarkupPercent=15;
    public static ManufacturingTerminalView Snapshot()=>MachineRegistrationApi.TerminalSnapshot();
    /// <summary>
    /// 纯托管ID规范化；首尾空白去除，已知别名严格区分大小写。
    /// 原样保留其他有效物品ID；历史ID只能由调用者通过aliases明确列出，框架不接管其他Mod或改写存档。
    /// aliases最多64条，只在本次调用生效，不建立全局映射。
    /// </summary>
    public static string NormalizeItemId(string itemId,System.Collections.Generic.IReadOnlyList<string>? aliases=null)
    {
        string Read(string value)
        {
            if(value is null)throw new System.ArgumentNullException(nameof(itemId));
            var id=value.Trim();
            if(id.Length is <1 or >160||System.Linq.Enumerable.Any(id,char.IsWhiteSpace)||System.Linq.Enumerable.Any(id,char.IsControl))
                throw new System.ArgumentException("EZAPI_TERMINAL_ID: nonempty stable ID<=160 without whitespace/controls required.",nameof(itemId));
            return id;
        }
        var result=Read(itemId);
        if(aliases is null)return result;
        if(aliases.Count>64)throw new System.ArgumentException("EZAPI_TERMINAL_ALIASES: max64 aliases.",nameof(aliases));
        var keys=new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach(var alias in aliases)if(!keys.Add(Read(alias)))throw new System.ArgumentException("EZAPI_TERMINAL_ALIASES: duplicate alias.",nameof(aliases));
        return keys.Contains(result)?ItemId:result;
    }
}

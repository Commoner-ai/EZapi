using System.Reflection;
using Il2Cpp;
namespace EZapi;

/// <summary>
/// 当前050REV5-L的独立物品读档后处理，顺序来自该游戏PlayerStore.DecodeSaveItem。
/// 只调用托管包装；不使用地址/偏移，不沿用已删除的旧水培机方法。
/// </summary>
internal static class SaveGraphLoader
{
    private static readonly Lazy<string?> SignatureError=new(()=>CheckSignatures());
    internal static bool Available=>GameBuildFingerprint.IsKnown&&SignatureError.Value is null;
    internal static string? UnavailableReason=>!GameBuildFingerprint.IsKnown
        ?"EZAPI_SAVEGRAPH_UNSUPPORTED: current native load sequence is not verified for this game build."
        :SignatureError.Value;
    private static string? CheckSignatures()
    {
        try{
            foreach(var entry in new (Type Type,string Method)[]{
                (typeof(MachineHelper),"LoadUpgrade"),(typeof(SpriteHelper),"UpdateWaterContainerSprite"),
                (typeof(GeneralHelper),"LoadLockItem"),(typeof(PlantTraitList),"LoadEdibleProduce"),
                (typeof(GeneralHelper),"LoadNonOwnedToolTag"),(typeof(GeneralHelper),"LoadHazardousWasteTag"),
                (typeof(LockHelper),"LoadLockedContainer"),(typeof(MachineBrokenHelper),"LoadBrokenMachine"),
                (typeof(MachineHelper),"OnLoad"),(typeof(MachineTurboBoosterAdv),"LoadTurboBoostEligibleTag"),
                (typeof(CurrentConverterHelper),"OnLoad")})Resolve(entry.Type,entry.Method);
            return null;
        }catch(Exception ex){return "EZAPI_SAVEGRAPH_UNSUPPORTED: "+ex.Message;}
    }
    internal static MethodInfo Resolve(Type type,string name)
    {
        var method=type.GetMethod(name,BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(GameItem)},null);
        if(method is null||method.ReturnType!=typeof(void)||method.ContainsGenericParameters)
            throw new InvalidOperationException("EZAPI_SAVEGRAPH_UNSUPPORTED: exact static void "+type.FullName+"."+name+"(GameItem) required.");
        return method;
    }
    internal static void Apply(GameItem item)
    {
        // 调用前CanDecode已核对整个序列，避免创建图后才发现缺少后处理方法。
        MachineHelper.LoadUpgrade(item);
        SpriteHelper.UpdateWaterContainerSprite(item);
        GeneralHelper.LoadLockItem(item);
        PlantTraitList.LoadEdibleProduce(item);
        GeneralHelper.LoadNonOwnedToolTag(item);
        GeneralHelper.LoadHazardousWasteTag(item);
        LockHelper.LoadLockedContainer(item);
        MachineBrokenHelper.LoadBrokenMachine(item);
        MachineHelper.OnLoad(item);
        MachineTurboBoosterAdv.LoadTurboBoostEligibleTag(item);
        CurrentConverterHelper.OnLoad(item);
        item.onLoaded?.Invoke(item);
    }
}

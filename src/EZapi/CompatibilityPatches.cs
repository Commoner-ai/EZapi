using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
namespace EZapi;

/// <summary>
/// 官方事件无法覆盖的两件事：自定义分类名称和调试区输入遮挡。
/// 目标与用途显式列出。物品目录、图片、堆叠和 tooltip 都不使用 Harmony。
/// 没有带 HarmonyPatch 的自动安装类，避免宿主 PatchAll 和手动安装重叠。
/// </summary>
internal static class CompatibilityPatches
{
    private static HarmonyLib.Harmony? _owner;
    internal static void Start()
    {
        if(_owner is not null)return;
        var owner=new HarmonyLib.Harmony("ezapi.compatibility");
        try {
            var typeName=AccessTools.DeclaredMethod(typeof(TypeHelper),nameof(TypeHelper.GetTypeDisplayName),new[]{typeof(string)})
                ??throw new MissingMethodException("TypeHelper.GetTypeDisplayName(string)");
            owner.Patch(typeName,prefix:new HarmonyMethod(typeof(CompatibilityPatches),nameof(TypeName)));
            foreach(var target in DebugInventoryPressGuard.TargetMethods())
                owner.Patch(target,prefix:new HarmonyMethod(typeof(DebugInventoryPressGuard),nameof(DebugInventoryPressGuard.Prefix)));
            _owner=owner;
            MelonLogger.Msg("[EZapi] Compatibility patches: custom type name + 3 debug input handlers. No tooltip/sprite/stack detours.");
        } catch { owner.UnpatchSelf(); throw; }
    }
    internal static void Stop() { _owner?.UnpatchSelf(); _owner=null; }
    private static bool TypeName(string typeConst,ref string __result)
    {
        if(!ItemRegistry.TryTypeName(typeConst,out var name))return true;
        __result=name; return false;
    }
}

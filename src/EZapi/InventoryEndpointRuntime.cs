using Il2Cpp;
using MelonLoader;
using NativeItemsList=Il2CppSystem.Collections.Generic.List<Il2Cpp.GameItem>;
namespace EZapi;

internal static class InventoryEndpointRuntime
{
    private static readonly NativeHookGroup Hooks=new("ezapi.inventory.endpoints");
    internal static bool Ready=>Hooks.Snapshot().Ready;
    internal static NativeHookStatus Status()=>Hooks.Snapshot();
    internal static void DirectoryReady()=>Hooks.Install(typeof(InventoryEndpointRuntime),
        new(typeof(PlayerStore),nameof(PlayerStore.SaveGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeSave)),
        new(typeof(PlayerStore),nameof(PlayerStore.LoadGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeLoad)),
        new(typeof(PlayerStore),nameof(PlayerStore.StartNewGame),typeof(void),Type.EmptyTypes,Prefix:nameof(BeforeLoad)),
        new(typeof(PlayerStore),nameof(PlayerStore.FindAllItem),typeof(NativeItemsList),new[]{typeof(bool)},Postfix:nameof(Find)),
        new(typeof(PowerHelper),nameof(PowerHelper.GetAllPowerSourceFourniture),typeof(NativeItemsList),new[]{typeof(bool)},Postfix:nameof(Power)),
        new(typeof(PowerHelper),nameof(PowerHelper.ChargeAllRechargable),typeof(void),new[]{typeof(NativeItemsList)},Prefix:nameof(Charge)));
    private static bool BeforeSave(PlayerStore __instance)
    {
        try{InventoryEndpointApi.StageSave(__instance);return true;}
        catch(Exception e){
            InventoryEndpointApi.SaveFailed(e);
            MelonLogger.Error("[EZapi] EZAPI_ENDPOINT_SAVE_BLOCKED: 原生保存未执行，请勿退出；重新载入前先核对库存。 "+e);
            return false;
        }
    }
    private static void BeforeLoad()=>InventoryEndpointApi.BeforeLoad();
    private static void Find(PlayerStore __instance,bool __0,ref NativeItemsList? __result)
    {
        if(!Ready||__result is null)return;
        try {
            var items=InventoryEndpointApi.RuntimeItems(__instance,ownedOnly:__0);if(items.Count==0)return;
            var copy=new NativeItemsList();var seen=new HashSet<IntPtr>();
            foreach(var i in __result){if(i is null)copy.Add(null!);else{copy.Add(i);seen.Add(i.Pointer);}}
            foreach(var i in items)if(seen.Add(i.Pointer))copy.Add(i);
            __result=copy; // 不修改原版/其他Mod拥有的列表。
        }catch(Exception e){InventoryEndpointApi.RuntimeFault(e);MelonLogger.Error("[EZapi] EZAPI_ENDPOINT_ENUMERATION: "+e);}
    }
    private static void Power(ref NativeItemsList? __result)
    {
        if(!Ready||__result is null||!GameSession.TryResolve(null,out var store))return;
        try {
            var hidden=InventoryEndpointApi.RuntimeItems(store,chargingOnly:true).Select(i=>i.Pointer).ToHashSet();if(hidden.Count==0)return;
            var copy=new NativeItemsList();foreach(var i in __result)if(i is null)copy.Add(null!);else if(!hidden.Contains(i.Pointer))copy.Add(i);__result=copy;
        }catch(Exception e){InventoryEndpointApi.RuntimeFault(e);MelonLogger.Error("[EZapi] EZAPI_ENDPOINT_POWER_ENUMERATION: "+e);}
    }
    private static void Charge(ref NativeItemsList? __0)
    {
        if(!Ready||!GameSession.TryResolve(null,out var store))return;
        try {
            var extra=InventoryEndpointApi.RuntimeItems(store,chargingOnly:true,ownedOnly:true).Where(i=>i.IsTag("power_source_item")).ToArray();if(extra.Length==0)return;
            var copy=new NativeItemsList();var seen=new HashSet<IntPtr>();
            if(__0 is not null)foreach(var i in __0){if(i is null)copy.Add(null!);else{copy.Add(i);seen.Add(i.Pointer);}}
            foreach(var i in extra)if(seen.Add(i.Pointer))copy.Add(i);__0=copy;
        }catch(Exception e){InventoryEndpointApi.RuntimeFault(e);MelonLogger.Error("[EZapi] EZAPI_ENDPOINT_CHARGE_ENUMERATION: "+e);}
    }
    internal static void Stop(){Hooks.Stop();InventoryEndpointApi.Stop();}
}

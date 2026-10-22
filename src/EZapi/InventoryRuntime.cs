using Il2Cpp;
using MelonLoader;
using UnityEngine;
namespace EZapi;

/// <summary>原生库存能力的安装、门控和诊断；不把签名存在等同于钩子已生效。</summary>
internal static class InventoryRuntime
{
    private static readonly NativeHookGroup Freeze=new("ezapi.inventory.freeze");
    private static readonly NativeHookGroup Admission=new("ezapi.inventory.admission");
    private static readonly NativeHookGroup Lists=new("ezapi.inventory.lists");
    private static readonly Queue<string> Errors=new();
    internal static bool Started {get;private set;}
    internal static bool CatalogReady {get;private set;}
    internal static bool FreezeReady=>Started&&Freeze.Ready;
    internal static bool AdmissionReady=>Started&&Admission.Ready;
    internal static bool ListsReady=>Started&&Lists.Ready;
    internal static bool TransferReady=>Started&&CatalogReady&&VerifyFreeze()&&VerifyAdmission()&&VerifyLists();
    internal static void Start(){Started=true;CatalogReady=false;}
    internal static void DirectoryReady()
    {
        if(!Started||CatalogReady)return;
        GameSession.RequireThread();CatalogReady=true;
        Freeze.Install(typeof(InventoryRuntime),
            new(typeof(GameItem),nameof(GameItem.MayRemove),typeof(bool),Type.EmptyTypes,Prefix:nameof(MayRemove)),
            new(typeof(GameItem),nameof(GameItem.MaxNumRemove),typeof(int),Type.EmptyTypes,Postfix:nameof(MaxRemove)),
            new(typeof(GameGridInventory),nameof(GameGridInventory.Expel),typeof(bool),new[]{typeof(GameItem)},Prefix:nameof(Expel)),
            new(typeof(GameGridScrollableInventory),nameof(GameGridScrollableInventory.Expel),typeof(bool),new[]{typeof(GameItem)},Prefix:nameof(Expel)),
            new(typeof(GameSlotInventory),nameof(GameSlotInventory.Expel),typeof(bool),new[]{typeof(GameItem)},Prefix:nameof(Expel)),
            new(typeof(SlotMarker),nameof(SlotMarker.TryAcceptOnce),typeof(int),new[]{typeof(int)},Prefix:nameof(FrozenAccept)));
        Admission.Install(typeof(InventoryRuntime),
            new(typeof(GameGridInventory),nameof(GameGridInventory.MayHaveValidInventorySlot),typeof(bool),new[]{typeof(GameItem)},Postfix:nameof(Admit)),
            new(typeof(GameGridInventory),nameof(GameGridInventory.CheckInventorySlotStillValid),typeof(bool),new[]{typeof(GameItem),typeof(GridShape),typeof(TagSystem)},Postfix:nameof(Admit)),
            new(typeof(GameGridInventory),nameof(GameGridInventory.TryInventorySlot),typeof(SlotMarker),new[]{typeof(GameItem),typeof(int),typeof(Vector2),typeof(GridShape),typeof(TagSystem)},Postfix:nameof(AdmitSlot)),
            new(typeof(SlotMarker),nameof(SlotMarker.TryAcceptOnce),typeof(int),new[]{typeof(int)},Prefix:nameof(AdmitAccept)));
        Lists.Install(typeof(InventoryRuntime),
            new NativeHookSpec(typeof(GameGridInventory),nameof(GameGridInventory.UncheckedAccept),typeof(bool),new[]{typeof(GameItem)},Prefix:nameof(RestoreLists)));
    }
    internal static bool VerifyFreeze()=>Freeze.Snapshot().Ready&&Started;
    internal static bool VerifyAdmission()=>Admission.Snapshot().Ready&&Started;
    internal static bool VerifyLists()=>Lists.Snapshot().Ready&&Started;
    internal static IReadOnlyList<NativeHookStatus> Status()=>Array.AsReadOnly(new[]{Freeze.Snapshot(),Admission.Snapshot(),Lists.Snapshot()});
    internal static object Report()=>new{catalogReady=CatalogReady,groups=Status(),freeze=InventoryFreezeApi.Report(),admission=InventoryAdmissionApi.Report(),errors=Errors.ToArray()};
    internal static void SceneChanged()=>InventoryFreezeApi.Reset();
    internal static void Stop()
    {
        Started=false;CatalogReady=false;InventoryFreezeApi.Reset();InventoryAdmissionApi.Stop();
        Freeze.Stop();Admission.Stop();Lists.Stop();Errors.Clear();
    }
    private static bool InRun()=>Started&&GameSession.OnThread&&GameSession.TryResolve(null,out _);
    private static void Record(Exception e)
    {
        var error=e.GetType().Name+": "+e.Message;
        if(Errors.Contains(error))return;
        Errors.Enqueue(error);if(Errors.Count>32)Errors.Dequeue();
        MelonLogger.Error("[EZapi] EZAPI_INVENTORY_GUARD: "+error);
    }
    private static bool MayRemove(GameItem __instance,ref bool __result)
    {
        if(!FreezeReady||!InRun())return true;
        try {if(!InventoryFreezeApi.Matches(__instance)||InventoryFreezeApi.AllowsQuery(__instance))return true;}catch(Exception e){Record(e);}
        __result=false;return false;
    }
    private static void MaxRemove(GameItem __instance,ref int __result)
    {
        if(!FreezeReady||!InRun())return;
        try {if(InventoryFreezeApi.Matches(__instance)&&!InventoryFreezeApi.AllowsQuery(__instance))__result=0;}catch(Exception e){Record(e);__result=0;}
    }
    private static bool Expel(GameInventory __instance,GameItem __0,ref bool __result)
    {
        if(!FreezeReady||!InRun())return true;
        try {if(!InventoryFreezeApi.Matches(__0)||InventoryFreezeApi.AllowsExpel(__instance,__0))return true;}catch(Exception e){Record(e);}
        __result=false;return false;
    }
    private static bool FrozenAccept(SlotMarker __instance,int __0,ref int __result)
    {
        if(!FreezeReady||!InRun())return true;
        try {
            if(InventoryFreezeApi.AllowsAccept(__instance,__0))return true;
            if(!InventoryFreezeApi.Matches(__instance.item)&&!InventoryFreezeApi.Matches(__instance.targetItem)&&
                !InventoryFreezeApi.Matches(__instance.inventory?.GetParentItem()))return true;
        }catch(Exception e){Record(e);}
        __result=0;return false;
    }
    private static bool Allows(GameItem item,GameGridInventory target)
    {
        try{return InventoryAdmissionApi.Allows(item,target);}catch(Exception e){Record(e);return false;}
    }
    private static void Admit(GameGridInventory __instance,GameItem __0,ref bool __result)
    {if(__result&&AdmissionReady&&InRun())__result=Allows(__0,__instance);}
    private static void AdmitSlot(GameGridInventory __instance,GameItem __0,ref SlotMarker? __result)
    {if(__result is not null&&AdmissionReady&&InRun()&&!Allows(__0,__instance))__result=null;}
    private static bool AdmitAccept(SlotMarker __instance,ref int __result)
    {
        if(!AdmissionReady||!InRun())return true;
        try {
            var grid=__instance.inventory?.TryCast<GameGridInventory>();
            if(grid is null||Allows(__instance.item,grid))return true;
        }catch(Exception e){Record(e);}
        __result=0;return false;
    }
    private static bool RestoreLists(GameGridInventory __instance,GameItem __0,ref bool __result)
    {
        if(!ListsReady||!CatalogReady||!GameSession.OnThread)return true;
        try {
            ListInventoryApi.RestoreDimensions(__instance);
            var inner=__0?.contentWindow?.childElement?.TryCast<GameGridInventory>();
            if(inner is not null)ListInventoryApi.RestoreDimensions(inner);
            return true;
        }catch(Exception e){Record(e);__result=false;return false;}
    }
}

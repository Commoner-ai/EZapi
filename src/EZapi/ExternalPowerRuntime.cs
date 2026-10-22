using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>原生电源查询与防充电边界。全部完整签名安装成功才开放提供方；不拦截未登记物品。</summary>
internal static class ExternalPowerRuntime
{
    private static readonly NativeHookGroup Hooks=new("ezapi.power.external");
    private static readonly Queue<string> Errors=new();
    private static IntPtr _initializing;
    internal static void Initialize(GameItem item)
    {
        if(_initializing!=IntPtr.Zero)throw ExternalPowerApi.Error("REENTRANT","connector initialization already active");
        _initializing=item.Pointer;
        try{PowerHelper.InitPowerSourceItem(item,1,1);PowerHelper.SetPowerSourceAt(item,0);}
        finally{_initializing=IntPtr.Zero;}
    }
    internal static bool VerifyReady()=>Hooks.Snapshot().Ready;
    internal static NativeHookStatus Status()=>Hooks.Snapshot();
    internal static void DirectoryReady()=>Hooks.Install(typeof(ExternalPowerRuntime),
        new(typeof(PowerHelper),nameof(PowerHelper.GetAvailableEnergyFromItem),typeof(int),new[]{typeof(GameItem),typeof(bool)},Prefix:nameof(Energy)),
        new(typeof(PowerHelper),nameof(PowerHelper.CanDrawPowerSource),typeof(bool),new[]{typeof(GameItem),typeof(int)},Prefix:nameof(CanDraw)),
        new(typeof(PowerHelper),nameof(PowerHelper.DrawPowerSource),typeof(bool),new[]{typeof(GameItem),typeof(int)},Prefix:nameof(Draw)),
        new(typeof(PowerHelper),nameof(PowerHelper.ChargeBattery),typeof(void),new[]{typeof(GameItem)},Prefix:nameof(NoCharge)),
        new(typeof(PowerHelper),nameof(PowerHelper.ChargeBattery),typeof(void),new[]{typeof(GameItem),typeof(int),typeof(bool)},Prefix:nameof(NoCharge)),
        new(typeof(PowerHelper),nameof(PowerHelper.SetPowerSourceAt),typeof(void),new[]{typeof(GameItem),typeof(int)},Prefix:nameof(NoWrite)),
        new(typeof(PowerHelper),nameof(PowerHelper.InitPowerSourceItem),typeof(void),new[]{typeof(GameItem),typeof(int),typeof(int)},Prefix:nameof(NoWrite)),
        new(typeof(PowerHelper),nameof(PowerHelper.IsPowerSourceFull),typeof(bool),new[]{typeof(GameItem)},Prefix:nameof(Full)),
        new(typeof(PowerHelper),nameof(PowerHelper.TryRemoveEnergy),typeof(int),new[]{typeof(GameItem),typeof(int),typeof(bool)},Prefix:nameof(NoRemove)),
        new(typeof(PowerHelper),nameof(PowerHelper.CreatePowerSourceItemTooltip),typeof(void),new[]{typeof(RichTextBuilder),typeof(GameItem)},Prefix:nameof(Tooltip)),
        new(typeof(MachineFurnace.__c__DisplayClass0_0),"_Furnace_b__5",typeof(void),new[]{typeof(GameItem),typeof(GameInventory),typeof(SlotMarker)},Prefix:nameof(BeforeFurnace),Postfix:nameof(AfterFurnace),Finalizer:nameof(FinishFurnace)),
        new(typeof(GameItemElement),nameof(GameItemElement.Destroy),typeof(void),Type.EmptyTypes,Prefix:nameof(DeferDestroy)));
    private static bool BeforeFurnace(MachineFurnace.__c__DisplayClass0_0 __instance,out NativeFurnacePower.Batch? __state)
        =>NativeFurnacePower.Before(__instance,out __state);
    private static void AfterFurnace(NativeFurnacePower.Batch? __state,bool __runOriginal)=>__state?.Finish(!__runOriginal);
    private static Exception? FinishFurnace(Exception? __exception,NativeFurnacePower.Batch? __state)
    {__state?.Finish(__exception is not null);return __exception;}
    private static bool DeferDestroy(GameItemElement __instance)=>NativeFurnacePower.Destroy(__instance);
    internal static void Stop(){NativeFurnacePower.Stop();Hooks.Stop();Errors.Clear();}
    internal static void Record(string stage,Exception error)
    {
        var message=stage+": "+error.GetType().Name+": "+error.Message;
        // 同一异常不在每帧tooltip中刷屏。诊断仍保留最近16种错误。
        if(Errors.Contains(message))return;Errors.Enqueue(message);while(Errors.Count>16)Errors.Dequeue();
        MelonLogger.Warning("[EZapi] EZAPI_EXTERNAL_POWER_CALLBACK "+message);
    }
    private static bool Managed(GameItem item)=>GameSession.OnThread&&ExternalPowerApi.Find(item) is not null;
    private static bool Energy(GameItem __0,ref int __result)
    {if(!Managed(__0))return true;__result=Math.Max(0,ExternalPowerApi.Read(ExternalPowerApi.Find(__0),__0));return false;}
    private static bool CanDraw(GameItem __0,int __1,ref bool __result)
    {if(!Managed(__0))return true;var energy=ExternalPowerApi.Read(ExternalPowerApi.Find(__0),__0);__result=__1>=0&&energy>=__1;return false;}
    private static bool Draw(GameItem __0,int __1,ref bool __result)
    {
        if(!Managed(__0))return true;
        __result=NativeFurnacePower.Draw(__0,__1);return false;
    }
    private static bool NoCharge(GameItem __0)=>!Managed(__0)&&!InventoryFreezeApi.Matches(__0);
    private static bool NoWrite(GameItem __0)=>!Managed(__0)||__0.Pointer==_initializing;
    private static bool Full(GameItem __0,ref bool __result){if(!Managed(__0))return true;__result=true;return false;}
    private static bool NoRemove(GameItem __0,ref int __result){if(!Managed(__0))return true;__result=0;return false;}
    private static bool Tooltip(RichTextBuilder __0,GameItem __1)
    {
        if(!Managed(__1))return true;
        var energy=ExternalPowerApi.Read(ExternalPowerApi.Find(__1),__1);
        __0.AddLine(energy<0?"外部电源：未连接":"外部电源：已连接（"+energy+"）",true,RenderHandler.ColorPalette.None,false,false,false,false);
        __0.AddLine("连接器不储电，不能充电。",true,RenderHandler.ColorPalette.LightGray,false,false,false,false);return false;
    }
}

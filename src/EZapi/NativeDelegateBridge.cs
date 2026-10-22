using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
namespace EZapi;

/// <summary>
/// 当前原生 InvokeAllReduce 通过反射调用委托。DelegateSupport 的托管闭包目标不属于原生
/// 方法声明类型，会抛 TargetException。用标准 ClassInjector 注册真实接收器，再用游戏
/// Delegate.CreateDelegate 绑定其原生方法；不修补 Delegate/DynamicInvoke，不改变其它 Mod。
/// 接收器无玩家、库存或物品字段；行为按当前物品 ID 同步路由。
/// </summary>
internal static class NativeDelegateBridge
{
    private static NativeItemCallbackTarget? _target;
    private static Il2CppSystem.Func<GameItem,GameItem,bool>? _stack;
    private static Il2CppSystem.Func<GameItem,GameInventory,SlotMarker,bool>? _can;
    private static Il2CppSystem.Action<GameItem,GameInventory,SlotMarker>? _activate;
    private static Il2CppSystem.Func<GameItem,GameInventory,bool>? _machineSlot;
    private static bool _registered;
    private static Il2CppSystem.Func<GameItem,GameInventory,SlotMarker,int>? _probeAmount;
    private static Il2CppSystem.Func<GameItem,GameItem,bool>? _probeStack;
    internal static Il2CppSystem.Func<GameItem,GameInventory,SlotMarker,int> ProbeAmount()=>_probeAmount??=Bind<Il2CppSystem.Func<GameItem,GameInventory,SlotMarker,int>>(nameof(NativeItemCallbackTarget.ProbeStackAmount));
    internal static Il2CppSystem.Func<GameItem,GameItem,bool> ProbeStack()=>_probeStack??=Bind<Il2CppSystem.Func<GameItem,GameItem,bool>>(nameof(NativeItemCallbackTarget.ProbeStackAllows));
    internal static void Start()
    {
        if(!_registered){ClassInjector.RegisterTypeInIl2Cpp<NativeItemCallbackTarget>();_registered=true;}
        _target??=new NativeItemCallbackTarget();
    }
    private static T Bind<T>(string method) where T:Il2CppSystem.Delegate
    {
        if(_target is null)throw new InvalidOperationException("EZAPI_DELEGATE_BRIDGE: initialize EZapi first.");
        return Il2CppSystem.Delegate.CreateDelegate(Il2CppType.Of<T>(),_target,method).Cast<T>();
    }
    internal static Il2CppSystem.Func<GameItem,GameItem,bool> Stack()=>_stack??=Bind<Il2CppSystem.Func<GameItem,GameItem,bool>>(nameof(NativeItemCallbackTarget.NeverStack));
    internal static Il2CppSystem.Func<GameItem,GameInventory,SlotMarker,bool> Can()=>_can??=Bind<Il2CppSystem.Func<GameItem,GameInventory,SlotMarker,bool>>(nameof(NativeItemCallbackTarget.CanActivate));
    internal static Il2CppSystem.Action<GameItem,GameInventory,SlotMarker> Activate()=>_activate??=Bind<Il2CppSystem.Action<GameItem,GameInventory,SlotMarker>>(nameof(NativeItemCallbackTarget.Activate));
    internal static Il2CppSystem.Func<GameItem,GameInventory,bool> MachineSlot()=>_machineSlot??=Bind<Il2CppSystem.Func<GameItem,GameInventory,bool>>(nameof(NativeItemCallbackTarget.MachineSlotAllows));
    internal static object Report()=>new{ready=_target is not null,strategy="ClassInjector receiver + native Delegate.CreateDelegate",
        stackBound=_stack is not null,activationBound=_can is not null&&_activate is not null,coreDelegatePatches=0};
    internal static void Stop(){_stack=null;_can=null;_activate=null;_machineSlot=null;_probeAmount=null;_probeStack=null;_target=null;}
}

/// <summary>仅供 IL2CPP 接收器注入；内容 Mod 不应构造或调用此宿主适配类型。</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class NativeItemCallbackTarget:Il2CppSystem.Object
{
    public NativeItemCallbackTarget(IntPtr pointer):base(pointer){}
    public NativeItemCallbackTarget():base(ClassInjector.DerivedConstructorPointer<NativeItemCallbackTarget>())
        =>ClassInjector.DerivedConstructorBody(this);
    public bool NeverStack(GameItem item,GameItem other)=>false;
    public int ProbeStackAmount(GameItem item,GameInventory inventory,SlotMarker slot)=>item.identifier=="ezapi.debug.material_stack"?99:1;
    public bool ProbeStackAllows(GameItem item,GameItem other)=>item.identifier=="ezapi.debug.material_stack"&&other.identifier==item.identifier;
    public bool CanActivate(GameItem item,GameInventory inventory,SlotMarker slot)=>ItemActivation.Can(item.identifier,item,inventory,slot);
    public void Activate(GameItem item,GameInventory inventory,SlotMarker slot)=>ItemActivation.Activate(item.identifier,item,inventory,slot);
    public bool MachineSlotAllows(GameItem item,GameInventory inventory)=>MachineLayoutApi.Allows(item,inventory);
}

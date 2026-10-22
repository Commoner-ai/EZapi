using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
namespace EZapi;

/// <summary>
/// 本次原生回调的上下文，仅在同步回调内使用；不要缓存到字段、异步任务或下次读档。
/// 框架在进入回调前检查当前玩家、物品所有权及所属库存；内容作者负责自己的副作用与消耗。
/// </summary>
public sealed class ItemActivationContext
{
    internal ItemActivationContext(GameItem item,GameInventory inventory,SlotMarker? slot,PlayerStore player)
        => (Item,Inventory,Slot,Player)=(item,inventory,slot,player);
    public GameItem Item { get; }
    public GameInventory Inventory { get; }
    public SlotMarker? Slot { get; }
    public PlayerStore Player { get; }
}

/// <summary>
/// 实例激活行为扩展，不修补 GameItem。先 Register 物品，再在同一初始化主线程登记回调。
/// canActivate 必须是无副作用、快速返回的查询；activate 同步执行一次，不自动扣款、销毁或保存。
/// </summary>
public static class ItemActivation
{
    private sealed record Handler(Func<ItemActivationContext,bool> Can,Action<ItemActivationContext> Activate);
    private sealed record NativeBinding(
        Il2CppSystem.Func<GameItem,GameInventory,SlotMarker,bool> Can,
        Il2CppSystem.Action<GameItem,GameInventory,SlotMarker> Activate);
    private static readonly Dictionary<string,Handler> Handlers=new(StringComparer.Ordinal);
    // 只保留无实例状态的原生委托。物品/玩家/库存引用不会放进缓存。
    private static readonly Dictionary<string,NativeBinding> Bindings=new(StringComparer.Ordinal);
    private static readonly HashSet<IntPtr> Executing=new();
    private static readonly HashSet<string> Disabled=new(StringComparer.Ordinal);

    public static ItemRegistrationResult Register(string itemId,Func<ItemActivationContext,bool> canActivate,
        Action<ItemActivationContext> activate)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(canActivate);
        ArgumentNullException.ThrowIfNull(activate);
        var error=ItemRegistry.BehaviorRegistrationError(itemId);
        if(error is not null)return new(itemId,false,error);
        if(!Handlers.TryAdd(itemId,new(canActivate,activate)))
            return new(itemId,false,"EZAPI_ACTIVATION_DUPLICATE: behavior already registered.");
        return new(itemId,true,"Queued");
    }

    internal static void Bind(GameItem item,ItemDefinition definition)
    {
        var id=definition.Id;
        if(!Handlers.ContainsKey(id) && definition.Activation is { } settings)
            Handlers.Add(id,new(c=>CanHeal(c,settings),c=>Heal(c,settings)));
        if(!Handlers.ContainsKey(id))return;
        if(!Bindings.TryGetValue(id,out var binding)) {
            binding=new(NativeDelegateBridge.Can(),NativeDelegateBridge.Activate()); Bindings.Add(id,binding);
        }
        item.mayActivateSlotItemFunc=binding.Can;
        item.canActivateSlotItemFunc=binding.Can;
        item.onActivateSlotItemFunc=binding.Activate;
    }

    private static ItemActivationContext? Context(string id,GameItem item,GameInventory inventory,SlotMarker? slot)
    {
        if(Disabled.Contains(id)||!Handlers.ContainsKey(id)||ItemRegistry.GetStatus(id)!="Applied")return null;
        if(item is null||item.Pointer==IntPtr.Zero||inventory is null||inventory.Pointer==IntPtr.Zero||
            item.identifier!=id||item.unitCount!=1||item.forceDisableActivate||item.forceDisableUse||
            item.parentInventory?.Pointer!=inventory.Pointer||!GeneralHelper.IsItemOwned(item))return null;
        var player=PlayerStore.instance;
        if(player is null||player.saveSlotId<0||string.IsNullOrEmpty(player.runID))return null;
        return new(item,inventory,slot,player);
    }
    internal static bool Can(string id,GameItem item,GameInventory inventory,SlotMarker? slot)
    {
        if(item is null||!Executing.Add(item.Pointer))return false;
        var pointer=item.Pointer;
        try {
            var context=Context(id,item,inventory,slot);
            return context is not null&&Handlers[id].Can(context);
        } catch(Exception e) { Disable(id,"predicate",e); return false; }
        finally { Executing.Remove(pointer); }
    }
    internal static void Activate(string id,GameItem item,GameInventory inventory,SlotMarker? slot)
    {
        if(item is null||!Executing.Add(item.Pointer))return;
        var pointer=item.Pointer;
        try {
            var context=Context(id,item,inventory,slot);
            // 在真正写入时重做门控；不能信任之前的鼠标悬停或 CanActivate 结果。
            if(context is null||!Handlers[id].Can(context))return;
            Handlers[id].Activate(context);
        } catch(Exception e) { Disable(id,"action",e); }
        finally { Executing.Remove(pointer); }
    }
    private static void Disable(string id,string stage,Exception e)
    {
        Disabled.Add(id);
        MelonLogger.Error("[EZapi] EZAPI_ACTIVATION_FAILED "+id+" "+stage+": "+e+
            "; disabled for this process; side effects are not automatically rolled back or retried.");
        EZapiBootstrap.WriteDiagnostics();
    }

    private static bool CanHeal(ItemActivationContext c,ItemActivationDefinition settings)
    {
        var health=c.Player.healthData;
        return health is not null&&(health.woundState>0||!health.isWoundStable||health.isWoundedFresh)&&
            (settings.Consumption!="useCount"||UseCountHelper.CanUse(c.Item,settings.Charge));
    }
    private static void Heal(ItemActivationContext c,ItemActivationDefinition settings)
    {
        // 与官方 LearnerMod/Item.cs 示例一致；不创建自己的健康系统，不自动制造伤势供测试。
        var health=c.Player.healthData??throw new InvalidOperationException("Current health data is no longer available.");
        health.woundState=0;
        health.isWoundStable=true;
        health.isWoundedFresh=false;
        if(settings.Consumption=="useCount")UseCountHelper.Use(c.Item,settings.Charge);
        else c.Item.Destroy();
    }

    internal static object Report()=>new {registered=Handlers.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray(),
        disabled=Disabled.OrderBy(x=>x,StringComparer.Ordinal).ToArray()};
    internal static void Clear() { Handlers.Clear();Bindings.Clear();Executing.Clear();Disabled.Clear(); }
}

// Native event/delegate boundary only. The subscription, dispatch, ordering and
// cleanup under test are linked from the production OfficialHookSubscriptions.
namespace Il2CppSystem
{
    public interface ICallback { IntPtr Pointer { get; } }
    public sealed class Action<T> : ICallback
    {
        private static int _next;
        private readonly System.Action<T> _call;
        public IntPtr Pointer { get; } = new(++_next);
        public Action(Delegate callback) => _call=(System.Action<T>)callback;
        public void Invoke(T value) => _call(value);
        public IEnumerable<ICallback> GetInvocationList() => new ICallback[]{this};
    }
    public sealed class Action<T,U> : ICallback
    {
        private static int _next;
        private readonly System.Action<T,U> _call;
        public IntPtr Pointer { get; } = new(++_next);
        public Action(Delegate callback) => _call=(System.Action<T,U>)callback;
        public void Invoke(T first,U second) => _call(first,second);
    }
}
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T? ConvertDelegate<T>(Delegate callback) where T:class
            => (T?)Activator.CreateInstance(typeof(T),callback);
    }
}
namespace Il2Cpp
{
    public sealed class ModItemDirectory {}
    public sealed class StoreClientManager {}
    public sealed class RichTextBuilder {}
    public sealed class GameItem {}
    public static class ModHook
    {
        private static readonly List<Il2CppSystem.Action<ModItemDirectory>> Directories=new();
        private static readonly List<Il2CppSystem.Action<RichTextBuilder,GameItem>> EarlyHandlers=new(),LateHandlers=new();
        private static readonly List<Il2CppSystem.Action<StoreClientManager>> CustomerHandlers=new();
        public static Il2CppSystem.Action<StoreClientManager>? OnGenerateCustomerVeryLate=>CustomerHandlers.FirstOrDefault();
        public static bool RefuseCustomerHook;
        public static void add_OnModItemDirectoryInit(Il2CppSystem.Action<ModItemDirectory> value)=>Directories.Add(value);
        public static void remove_OnModItemDirectoryInit(Il2CppSystem.Action<ModItemDirectory> value)=>Directories.RemoveAll(item=>item.Pointer==value.Pointer);
        public static void add_OnCreateTooltipEarly(Il2CppSystem.Action<RichTextBuilder,GameItem> value)=>EarlyHandlers.Add(value);
        public static void remove_OnCreateTooltipEarly(Il2CppSystem.Action<RichTextBuilder,GameItem> value)=>EarlyHandlers.RemoveAll(item=>item.Pointer==value.Pointer);
        public static void add_OnCreateTooltipLate(Il2CppSystem.Action<RichTextBuilder,GameItem> value)=>LateHandlers.Add(value);
        public static void remove_OnCreateTooltipLate(Il2CppSystem.Action<RichTextBuilder,GameItem> value)=>LateHandlers.RemoveAll(item=>item.Pointer==value.Pointer);
        public static void add_OnGenerateCustomerVeryLate(Il2CppSystem.Action<StoreClientManager> value)
        {if(RefuseCustomerHook)throw new InvalidOperationException("optional event unavailable");CustomerHandlers.Add(value);}
        public static void remove_OnGenerateCustomerVeryLate(Il2CppSystem.Action<StoreClientManager> value)
            =>CustomerHandlers.RemoveAll(item=>item.Pointer==value.Pointer);
        public static void FireDirectory(){foreach(var handler in Directories.ToArray())handler.Invoke(new());}
        public static void FireEarly(){foreach(var handler in EarlyHandlers.ToArray())handler.Invoke(new(),new());}
        public static void FireLate(){foreach(var handler in LateHandlers.ToArray())handler.Invoke(new(),new());}
    }
}
namespace MelonLoader
{
    public static class MelonLogger
    {
        public static readonly List<string> Errors=new(),Warnings=new();
        public static void Error(string text)=>Errors.Add(text);
        public static void Warning(string text)=>Warnings.Add(text);
    }
}
namespace EZapi
{
    internal static class Calls
    {
        internal static readonly List<string> Trace=new();
        internal static bool ThrowOnEarly;
        internal static void Add(string name)=>Trace.Add(name);
    }
    internal static class InventoryRuntime {internal static void DirectoryReady()=>Calls.Add("inventory");}
    internal static class InventoryEndpointRuntime {internal static void DirectoryReady()=>Calls.Add("endpoint");}
    internal static class ExternalPowerRuntime {internal static void DirectoryReady()=>Calls.Add("power");}
    internal static class LifecycleApi {internal static void DirectoryReady()=>Calls.Add("lifecycle");}
    internal static class ReadingApi {internal static void DirectoryReady()=>Calls.Add("reading");}
    internal static class DialogueApi {internal static void DirectoryReady()=>Calls.Add("dialogue");}
    internal static class PhoneApi {internal static void DirectoryReady()=>Calls.Add("phone");}
    internal static class ModuleApi {internal static void DirectoryReady()=>Calls.Add("module");}
    internal static class ItemRegistry {internal static void Apply(Il2Cpp.ModItemDirectory value)=>Calls.Add("items");}
    internal static class DeclarativeItemLocalization {internal static void DirectoryReady()=>Calls.Add("item-locale");}
    internal static class EffectApi {internal static void Apply()=>Calls.Add("effects");}
    internal static class PresentationApi {internal static void DirectoryReady()=>Calls.Add("presentation");}
    internal static class ItemStockRuntime {internal static void DirectoryReady()=>Calls.Add("item-stock");}
    internal static class NpcStockRuntime {internal static void DirectoryReady()=>Calls.Add("npc-stock");}
    internal static class ClientVisitApi {internal static void OnGenerateCustomer(Il2Cpp.StoreClientManager value)=>Calls.Add("visit");}
    internal static class ItemPresentation
    {
        internal static void Early(Il2Cpp.RichTextBuilder builder,Il2Cpp.GameItem item)
        {if(Calls.ThrowOnEarly)throw new InvalidOperationException("intentional tooltip failure");Calls.Add("early");}
        internal static void Late(Il2Cpp.RichTextBuilder builder,Il2Cpp.GameItem item)=>Calls.Add("late");
    }
}

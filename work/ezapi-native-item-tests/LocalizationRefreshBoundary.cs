// Only native windows, hook installation and machine layouts are replaced here.
// The factory registry, option validation, item lookup, inline routing and refresh run production sources.
namespace Il2Cpp { public sealed class PixelWindow { public string Title=""; } }
namespace EZapi {
    internal sealed record NativeHookStatus(bool Ready);
    internal sealed record NativeHookSpec(Type Type,string Name,Type Return,Type[] Arguments,string? Prefix=null,string? Postfix=null);
    internal sealed class NativeHookGroup {
        private static Type? _callbacks;private static NativeHookSpec[] _specs=Array.Empty<NativeHookSpec>();
        internal NativeHookGroup(string owner){} internal bool Ready;
        internal void Install(Type type,params NativeHookSpec[] specs){_callbacks=type;_specs=specs;Ready=true;}
        internal static void Invoke(Il2Cpp.GameItem item,bool restore) {
            var spec=_specs.SingleOrDefault(s=>s.Name==(restore?nameof(Il2Cpp.GameItem.RestoreContentWindowPosition):nameof(Il2Cpp.GameItem.GetDisplayName)));
            var callback=restore?spec?.Postfix:spec?.Prefix;
            if(callback is not null)_callbacks!.GetMethod(callback,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null,restore?new object[]{item,true}:new object[]{item});
        }
        internal NativeHookStatus Snapshot()=>new(Ready);internal void Stop()=>Ready=false;
    }
    internal static class MachineLayoutApi { internal static void RefreshLocalization(Il2Cpp.GameItem item){} }
    internal static class NativeWindowTitles {
        internal static bool Matches(Il2Cpp.PixelWindow window,string title)=>window.Title==title;
        internal static void Set(Il2Cpp.PixelWindow window,string title)=>window.Title=title;
        internal static void Clear(){}
    }
}

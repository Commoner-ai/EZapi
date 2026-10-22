using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.Runtime;
using MelonLoader;
namespace EZapi;

public sealed record NativeHookStatus(string OwnerId, bool Ready, IReadOnlyList<string> Targets, string? Error);

/// <summary>检查当前IL2CPP包装方法是否有可执行入口；只检查，不安装补丁，不声明与其它Mod无冲突。</summary>
public static class HookApi
{
    /// <summary>只能在原生目录就绪后的游戏主线程调用，读取生成元数据可能触发包装类型初始化。</summary>
    public static bool TryValidateNativeTarget(MethodInfo? method, out string? reason)
    {
        if (!GameSession.OnThread) { reason="EZAPI_HOOK_THREAD: initialized main thread required."; return false; }
        if (!InventoryRuntime.CatalogReady) { reason="EZAPI_HOOK_NOT_READY: wait for native item directory."; return false; }
        return TryEntry(method, out _, out reason);
    }
    public static IReadOnlyList<NativeHookStatus> GetRuntimeStatus() => Array.AsReadOnly(
        InventoryRuntime.Status().Concat(new[]{InventoryEndpointRuntime.Status(),ExternalPowerRuntime.Status(),DeclarativeItemLocalization.Status()}).ToArray());

    // 仅通过Interop官方版本适配器读取方法元数据，不手算内存偏移，不执行/导出原生地址。
    internal static unsafe bool TryEntry(MethodInfo? method, out IntPtr entry, out string? reason)
    {
        entry=IntPtr.Zero; reason=null;
        try {
            if (method is null || method.IsAbstract || method.ContainsGenericParameters || method.DeclaringType is null)
                throw new ArgumentException("concrete, closed generated method required.");
            var field=Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
            if (field is null || !field.IsStatic || field.FieldType!=typeof(IntPtr) || field.Name.Contains("_Abstract_",StringComparison.Ordinal))
                throw new InvalidOperationException("method has no concrete generated native metadata.");
            if (field.GetValue(null) is not IntPtr metadata || metadata==IntPtr.Zero)
                throw new InvalidOperationException("native method metadata is not initialized.");
            var info=UnityVersionHandler.Wrap((Il2CppMethodInfo*)metadata);
            if (info is null || info.MethodPointer==IntPtr.Zero) throw new InvalidOperationException("native executable entry unavailable.");
            entry=info.MethodPointer;return true;
        } catch(Exception e) { reason="EZAPI_HOOK_TARGET: "+e.GetType().Name+": "+e.Message; return false; }
    }
}

internal sealed record NativeHookSpec(Type Type,string Name,Type Return,Type[] Arguments,string? Prefix=null,string? Postfix=null,string? Finalizer=null);
internal sealed class NativeHookGroup
{
    private readonly string _id;
    private HarmonyLib.Harmony? _harmony;
    private MethodInfo[] _targets=Array.Empty<MethodInfo>();
    private string? _error;
    internal bool Ready {get;private set;}
    internal NativeHookGroup(string id)=>_id=id;
    internal void Install(Type callbacks,params NativeHookSpec[] specs)
    {
        if(Ready)return;
        Ready=false;
        try {
            // 失去部分owner后重试前，先清除本组残留，避免其余目标重复安装同一owner。
            _harmony?.UnpatchSelf();_harmony=null;
            var signatures=new List<MethodInfo>();var entries=new HashSet<IntPtr>();
            foreach(var spec in specs) {
                var method=spec.Type.GetMethod(spec.Name,BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly,
                    null,spec.Arguments,null)??throw new MissingMethodException(spec.Type.FullName,spec.Name);
                if(method.ReturnType!=spec.Return)throw new InvalidOperationException("return type mismatch: "+method);
                if(!HookApi.TryEntry(method,out var entry,out var reason))throw new InvalidOperationException(reason);
                if(!entries.Add(entry))throw new InvalidOperationException("two required targets share one native entry; group disabled.");
                signatures.Add(method);
            }
            _targets=signatures.ToArray();_harmony=new HarmonyLib.Harmony(_id);
            for(var i=0;i<specs.Length;i++) {
                var spec=specs[i];
                HarmonyMethod? Callback(string? name) {
                    if(name is null)return null;
                    var method=callbacks.GetMethod(name,BindingFlags.NonPublic|BindingFlags.Static)
                        ??throw new MissingMethodException(callbacks.FullName,name);
                    return new HarmonyMethod(method){priority=Priority.Last};
                }
                _harmony.Patch(_targets[i],prefix:Callback(spec.Prefix),postfix:Callback(spec.Postfix),finalizer:Callback(spec.Finalizer));
                if(HarmonyLib.Harmony.GetPatchInfo(_targets[i])?.Owners.Contains(_id)!=true)
                    throw new InvalidOperationException("patch owner missing after installation: "+_targets[i]);
            }
            Ready=true;_error=null;
            MelonLogger.Msg("[EZapi] Native hook group ready: "+_id+" ("+_targets.Length+")");
        } catch(Exception e) {
            _error=e.GetType().Name+": "+e.Message;
            // 先关闭门控，即使宿主无法撤销部分补丁，已装回调也保持惰性。
            Ready=false;
            try {_harmony?.UnpatchSelf();_harmony=null;} catch(Exception cleanup) {_error+="; unpatch: "+cleanup.Message;}
            MelonLogger.Error("[EZapi] EZAPI_HOOK_GROUP_DISABLED "+_id+": "+_error);
        }
    }
    internal NativeHookStatus Snapshot()
    {
        // 就绪状态按当前所有者复核；其他Mod卸掉本组某个钩子后，不继续宣称能力完整。
        if(Ready && _targets.Any(m=>HarmonyLib.Harmony.GetPatchInfo(m)?.Owners.Contains(_id)!=true)) {
            Ready=false;_error="EZAPI_HOOK_LOST: a required patch owner is missing.";
        }
        return new(_id,Ready,Array.AsReadOnly(_targets.Select(m=>(m.DeclaringType?.FullName??"?")+"::"+m).ToArray()),_error);
    }
    internal void Stop()
    {
        Ready=false;
        try {_harmony?.UnpatchSelf();} catch(Exception e){MelonLogger.Error("[EZapi] EZAPI_HOOK_UNPATCH: "+_id+": "+e.Message);}
        _harmony=null;_targets=Array.Empty<MethodInfo>();_error=null;
    }
}

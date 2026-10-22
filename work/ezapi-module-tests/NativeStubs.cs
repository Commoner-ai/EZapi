using System.Reflection;
using HarmonyLib;
namespace Il2Cpp {
    public class NativeObject {
        private static int _next;
        public IntPtr Pointer { get; set; } = new(Interlocked.Increment(ref _next));
    }
    public class GameItem : NativeObject {
        private static int _next;
        public int uniqueId = Interlocked.Increment(ref _next), unitCount = 1;
        public string identifier = "test.mod.module";
        public GameGridInventory? ModuleInventory;
        public string? ModuleType;
        public int LearningValue;
        public TagState GetTagReadonly(string key) => new() { valueString = ModuleType };
    }
    public class TagState { public string? valueString; }
    public class GameInventory : NativeObject { }
    public class GameGridInventory : GameInventory { public Il2CppSystem.Collections.Generic.List<GameItem> items = new(); }
    public class PlayerStore : NativeObject { public static PlayerStore? instance = new(); public int saveSlotId = 1; public string runID = "test"; }
    public static class MachineHelper {
        public static string[]? LastTypes;
        public static GameGridInventory? GetModuleInv(GameItem machine) => machine.ModuleInventory;
        public static void SetupModuleBay(GameInventory grid, GameItem machine, Il2CppSystem.Action? changed, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray? types)
            => TestNative.Call(typeof(MachineHelper), nameof(SetupModuleBay), new object?[] { grid, machine, changed, types }, args => LastTypes = ((Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray?)args[3])?.ToArray());
    }
    public static class ModuleEffectHelper {
        public static void ComputeAllModuleTempStat(Il2CppSystem.Collections.Generic.List<GameItem> modules)
            => TestNative.Call(typeof(ModuleEffectHelper), nameof(ComputeAllModuleTempStat), new object?[] { modules }, _ => TestNative.Trace.Add("temp"));
        public static void OnAddedModuleToGrid(GameItem module, GameItem machine, Il2CppSystem.Collections.Generic.List<GameItem> modules)
            => TestNative.Call(typeof(ModuleEffectHelper), nameof(OnAddedModuleToGrid), new object?[] { module, machine, modules }, _ => TestNative.Trace.Add("added"));
        public static void OnRemovedModuleFromGrid(GameItem module, GameItem machine, Il2CppSystem.Collections.Generic.List<GameItem> modules)
            => TestNative.Call(typeof(ModuleEffectHelper), nameof(OnRemovedModuleFromGrid), new object?[] { module, machine, modules }, _ => TestNative.Trace.Add("removed"));
        public static void ActivateOnUseAction(GameItem module, GameInventory inventory, GameItem machine)
            => TestNative.Call(typeof(ModuleEffectHelper), nameof(ActivateOnUseAction), new object?[] { module, inventory, machine }, _ => TestNative.Trace.Add("action"));
        public static void OnUsedLearningAlgo(GameItem module)
            => TestNative.Call(typeof(ModuleEffectHelper), nameof(OnUsedLearningAlgo), new object?[] { module }, _ => { TestNative.LearningOriginal?.Invoke(module); module.LearningValue++; });
    }
    public static class ModuleHelper {
        public static void ComputeModuleEffect(Il2CppSystem.Collections.Generic.List<GameItem> modules, GameItem machine)
            => TestNative.Call(typeof(ModuleHelper), nameof(ComputeModuleEffect), new object?[] { modules, machine }, _ => TestNative.Trace.Add("aggregate"), typeof(Il2CppSystem.Collections.Generic.List<GameItem>), typeof(GameItem));
        public static void ComputeModuleEffect(GameInventory inventory, GameItem? item, GameItem machine)
            => ComputeModuleEffect(((GameGridInventory)inventory).items, machine);
        public static void ApplyBasicModuleEffect(GameInventory inventory, GameItem? item, GameItem machine) => TestNative.Trace.Add("basic");
    }
}
namespace Il2CppSystem { public class Action { } }
namespace Il2CppSystem.Collections.Generic {
    public class List<T> : System.Collections.Generic.List<T> {
        private static int _next;
        public IntPtr Pointer = new(Interlocked.Increment(ref _next));
    }
}
namespace Il2CppInterop.Runtime.InteropTypes.Arrays {
    public class Il2CppStringArray : System.Collections.Generic.List<string> { public Il2CppStringArray(string[] values) : base(values) { } }
}
namespace EZapi {
    public sealed record ContentSubmissionResult(bool Accepted, string? Reason);
    internal static class RunData { internal static void ValidateModId(string value) { if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new ArgumentException("owner"); } }
    public enum GameEvent { GameLoadedInit, GameLoadedNormal }
    internal static class InventoryRuntime { internal static bool CatalogReady = true; internal static IReadOnlyList<NativeHookStatus> Status() => Array.Empty<NativeHookStatus>(); }
}
// 只模拟Harmony托管调用约定，不能作为IL2CPP detour或原生算法证据。
public static class TestNative {
    public static readonly System.Collections.Generic.List<string> Trace = new();
    public static readonly HashSet<string> Skip = new();
    public static string? Fail;
    public static System.Action<Il2Cpp.GameItem>? LearningOriginal;
    public static void Call(Type type, string name, object?[] args, System.Action<object?[]> original, params Type[] signature) {
        var method = signature.Length > 0 ? type.GetMethod(name, signature)! : type.GetMethods().Single(m => m.Name == name);
        var patches = Harmony.Installed.GetValueOrDefault(method)?.ToArray() ?? Array.Empty<Patch>();
        var states = new Dictionary<string, object?>(); var run = !Skip.Contains(name); Exception? exception = null;
        object? Invoke(Patch patch, HarmonyMethod callback) {
            var parameters = callback.Method.GetParameters();
            var input = parameters.Select(p => p.Name switch {
                "__runOriginal" => (object)run, "__exception" => exception, "__state" => states.GetValueOrDefault(patch.Owner),
                _ when p.Name!.StartsWith("__") && int.TryParse(p.Name[2..], out var n) => args[n],
                _ => throw new Exception("unsupported parameter: " + p.Name)
            }).ToArray();
            object? result;
            try { result = callback.Method.Invoke(null, input); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
            for (var i = 0; i < input.Length; i++) {
                var p = parameters[i];
                if (p.Name == "__state") states[patch.Owner] = input[i];
                else if (p.ParameterType.IsByRef && int.TryParse(p.Name![2..], out var n)) args[n] = input[i];
            }
            return result;
        }
        try {
            foreach (var p in patches) if (p.Prefix is { } pre && Invoke(p, pre) is false) run = false;
            if (run) { if (Fail == name) throw Failure; original(args); }
            foreach (var p in patches) if (p.Postfix is { } post) Invoke(p, post);
        } catch (Exception e) { exception = e; }
        foreach (var p in patches) if (p.Finalizer is { } finalizer) exception = (Exception?)Invoke(p, finalizer);
        if (exception is not null) throw exception;
    }
    public static readonly Exception Failure = new InvalidOperationException("native original failed");
}

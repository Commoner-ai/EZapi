namespace Il2Cpp
{
    public sealed class GameItem
    {
        public IntPtr Pointer { get; set; } = new(1);
        public string identifier { get; set; } = "";
    }
}

namespace Il2Cpp.ModuleEffectHelper
{
    public sealed class ModuleEffect
    {
        public IntPtr Pointer { get; set; } = new(1);
        public string identifier { get; set; } = "";
        public string displayName { get; set; } = "";
        public string description { get; set; } = "";
    }
}

namespace MelonLoader
{
    public static class MelonLogger
    {
        public static readonly List<string> Warnings = new();
        public static void Warning(string value) => Warnings.Add(value);
    }
}

namespace EZapi
{
    public sealed record ContentSubmissionResult(bool Accepted, string? Reason);

    internal static class GameSession
    {
        internal static bool OnThread = true;
        internal static void RequireThread() { }
    }

    internal static class RunData
    {
        internal static void ValidateModId(string id)
        {
            if (id is null || id.Length > 160 || !System.Text.RegularExpressions.Regex.IsMatch(id, @"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*$"))
                throw new ArgumentException("invalid owner");
        }
    }

    internal static class ItemRegistry
    {
        internal static bool Ready = true;
        internal static bool OwnedModule;
        internal static string? DeclarationRegistrationError() => Ready ? null : "not ready";
        internal static bool IsDeclaredModule(string owner, string id) => owner == "test.mod" && id is "test.mod.module" or "test.mod.other";
        internal static bool IsOwnedModule(string owner, string id) => OwnedModule && owner == "test.mod" && id.StartsWith("test.mod.", StringComparison.Ordinal);
        internal static bool TryGetRegisteredName(string id,bool english, out string? value) { value = id == "test.mod.json" ? "JSON name" : null; return id == "test.mod.json"; }
        internal static bool TryGetRegisteredDescription(string id,bool english, out string? value) { value = id == "test.mod.json" ? "JSON description" : null; return id == "test.mod.json"; }
    }

    internal static class NativeFactoryRegistry
    {
        internal static bool TryGetRegisteredName(string id, bool english, out string? value) { value = id == "test.mod.native" ? english ? "Native EN" : "Native 中文" : null; return id == "test.mod.native"; }
        internal static bool TryGetRegisteredDescription(string id, bool english, out string? value) { value = id == "test.mod.native" ? english ? "Description EN" : "说明" : null; return id == "test.mod.native"; }
    }

    internal static class NativeItemLocalization
    {
        internal static bool IsEnglish = true;
    }

    internal static class EffectApi
    {
        internal static readonly HashSet<(string Owner, string Id)> Declared = new();
        internal static readonly Dictionary<string, (string Owner, Il2Cpp.ModuleEffectHelper.ModuleEffect Effect)> Applied = new();
        internal static bool IsDeclaredForOwner(string ownerId, string effectId) => Declared.Contains((ownerId, effectId));
        internal static bool TryGetApplied(string ownerId, string effectId, out Il2Cpp.ModuleEffectHelper.ModuleEffect effect)
        {
            if (Applied.TryGetValue(effectId, out var value) && value.Owner == ownerId) { effect = value.Effect; return true; }
            effect = null!; return false;
        }
        internal static bool IsAppliedObject(string ownerId, string effectId, Il2Cpp.ModuleEffectHelper.ModuleEffect effect)
            => Applied.TryGetValue(effectId, out var value) && value.Owner == ownerId && value.Effect.Pointer == effect.Pointer;
    }
}

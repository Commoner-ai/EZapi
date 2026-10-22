namespace Il2Cpp
{
    public sealed class PlayerStore { }
}

namespace MelonLoader
{
    public static class MelonLogger
    {
        public static readonly List<string> Warnings = new();
        public static void Warning(string message) => Warnings.Add(message);
    }
}

namespace EZapi
{
    internal static class GameSession
    {
        internal static bool OnThread { get; set; }
        internal static bool HasRun { get; set; }
    }

    internal static class RunData
    {
        internal static bool IsAvailable => GameSession.HasRun;
    }

    internal static class OfficialHookSubscriptions
    {
        internal static bool IsReady { get; set; }
        internal static bool ClientVisitReady { get; set; }
    }

    internal static class ModuleApi
    {
        internal static bool Throw { get; set; }
        internal static bool IsNativeDirectoryReady
        {
            get
            {
                if (Throw) throw new InvalidOperationException("synthetic hook status failure");
                return Ready;
            }
        }
        internal static bool Ready { get; set; }
    }

    internal static class EffectApi
    {
        internal static bool IsNativeRegistrationReady { get; set; }
    }

    internal static class InventoryRuntime
    {
        internal static bool TransferReady { get; set; }
    }

    internal static class MachineRuntimeApi
    {
        internal static bool RuntimeInstalled { get; set; }
    }

    internal static class InventoryEndpointApi
    {
        internal static bool IsAvailable { get; set; }
    }

    internal static class ExternalPowerApi
    {
        internal static bool IsAvailable { get; set; }
    }

    internal static class DialogueApi
    {
        internal static bool IsAvailable { get; set; }
    }

    internal static class ReadingApi
    {
        internal static bool IsAvailable { get; set; }
    }

    internal static class PhoneApi
    {
        internal static bool IsAvailable { get; set; }
    }

    internal static class ClientVisitApi
    {
        internal static bool IsAvailable { get; set; }
    }

    internal static class SupplierApi
    {
        internal static bool Ready { get; set; }
        internal static bool IsAvailable => Ready && ClientVisitApi.IsAvailable && DialogueApi.IsAvailable;
    }
}

namespace EZapi {internal static class ItemStockRuntime {internal static bool IsAvailable {get;set;}}}

namespace EZapi {internal static class NpcStockRuntime {internal static bool IsAvailable {get;set;}}}

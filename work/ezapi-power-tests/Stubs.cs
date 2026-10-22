namespace Il2Cpp
{
    public sealed class GameInventory { public IntPtr Pointer { get; set; } = new(500); }

    public sealed class GameItem
    {
        public IntPtr Pointer { get; set; } = new(1);
        public int uniqueId { get; set; } = 1;
        public string identifier { get; set; } = "test.battery";
        public int unitCount { get; set; } = 1;
        public GameInventory? parentInventory { get; set; } = new();
        public bool Owned { get; set; } = true;
        public bool External { get; set; }
    }

    public static class MachineryHelper
    {
        public static int Cost { get; set; } = 17;
        public static int GetMachinePowerUsage(GameItem machine) => Cost;
    }

    public static class PowerHelper
    {
        private static readonly Dictionary<IntPtr, int> Energies = new();
        public static int DrawCalls { get; private set; }
        public static Func<GameItem, int, bool>? CanDrawOverride { get; set; }
        public static Func<GameItem, int, bool>? DrawOverride { get; set; }
        public static Action<GameItem, int>? AfterDraw { get; set; }
        public static bool ThrowOnRestore { get; set; }

        public static void SetEnergy(GameItem item, int energy) => Energies[item.Pointer] = energy;
        public static int GetAvailableEnergyFromItem(GameItem item, bool useBandwidth) => Energies.GetValueOrDefault(item.Pointer);
        public static bool CanDrawPowerSource(GameItem item, int amount)
            => CanDrawOverride?.Invoke(item, amount) ?? amount >= 0 && GetAvailableEnergyFromItem(item, false) >= amount;
        public static bool DrawPowerSource(GameItem item, int amount)
        {
            DrawCalls++;
            if (DrawOverride is not null) return DrawOverride(item, amount);
            SetEnergy(item, GetAvailableEnergyFromItem(item, false) - amount);
            AfterDraw?.Invoke(item, amount);
            return true;
        }
        public static void SetPowerSourceAt(GameItem item, int energy)
        {
            if (ThrowOnRestore) throw new InvalidOperationException("restore failed");
            SetEnergy(item, energy);
        }
        public static void Reset()
        {
            Energies.Clear(); DrawCalls = 0; CanDrawOverride = null; DrawOverride = null; AfterDraw = null; ThrowOnRestore = false;
        }
    }

    public static class GeneralHelper
    {
        public static bool IsItemOwned(GameItem item) => item.Owned;
    }
}

namespace EZapi
{
    internal static class GameSession
    {
        internal static object Identity { get; set; } = new();
        internal static bool OnThread { get; set; } = true;
    }

    internal static class NativeAccess
    {
        internal static Il2Cpp.GameItem RequireItem(Il2Cpp.GameItem item)
        {
            if (!GameSession.OnThread || item is null || item.Pointer == IntPtr.Zero || item.unitCount <= 0)
                throw new ArgumentException("invalid test item");
            return item;
        }
        internal static bool SameRun(object run) => GameSession.OnThread && ReferenceEquals(run, GameSession.Identity);
    }

    public static class ExternalPowerApi
    {
        public static bool IsConnector(Il2Cpp.GameItem item) => item.External;
        public static int GetEnergy(Il2Cpp.GameItem item) => 900;
    }
}

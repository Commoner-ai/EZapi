using System.Security.Cryptography;
using Il2Cpp;
using MelonLoader;

namespace EZapi;

/// <summary>
/// A point-in-time view of runtime integrations. A false value means the feature is
/// unsupported here or its required runtime gate is not ready. This snapshot does
/// not claim that a feature has been exercised in gameplay.
/// </summary>
/// <remarks>
/// These are EZapi integration states, not capabilities supplied by the official game API.
/// Use the operation's own result as the final authority before changing game state.
/// </remarks>
public sealed record RuntimeCapabilitySnapshot(
    bool KnownGameBuild,
    bool NativeItemDirectoryHook,
    bool NativeModuleDirectoryHook,
    bool NativeAmenityDirectoryHook,
    bool NightShopStock,
    bool RunDataStaging,
    bool DirectInventoryRead,
    bool WholeTransferPreview,
    bool NativeEffectRegistration,
    bool InventoryTransfer,
    bool MachineNightProcessing,
    bool NpcTradeStock,
    bool DossierExpansion,
    bool PersistentInventoryEndpoints,
    bool ExternalPowerTransactions,
    bool BranchingDialogue,
    bool ClientVisits,
    bool ReadingWindow,
    bool PhoneContacts,
    bool SupplierVisits);

/// <summary>
/// Reports what EZapi can currently use without retaining Unity objects. Read on
/// the initialized game thread; off-thread calls fail closed. No capability is
/// inferred from the compile-time feature list alone. The official modding guide
/// defines the native hooks; this API only summarizes EZapi's own live gates.
/// </summary>
public static class RuntimeCapabilities
{
    private static readonly RuntimeCapabilitySnapshot Empty = new(
        false, false, false, false, false, false, false, false, false, false,
        false, false, false, false, false, false, false, false, false, false);
    private static readonly HashSet<string> ReportedProbeFailures = new(StringComparer.Ordinal);

    /// <summary>Rebuilds a point-in-time snapshot; no result is cached across calls.</summary>
    public static RuntimeCapabilitySnapshot Current
    {
        get
        {
            if (!GameSession.OnThread) return Empty;

            var runReady = Probe("run-data", () => RunData.IsAvailable);
            return new RuntimeCapabilitySnapshot(
                KnownGameBuild: Probe("game-build", () => GameBuildFingerprint.IsKnown),
                NativeItemDirectoryHook: Probe("item-directory", () => OfficialHookSubscriptions.IsReady),
                NativeModuleDirectoryHook: Probe("module-directory", () => ModuleApi.IsNativeDirectoryReady),
                NativeAmenityDirectoryHook: false,
                NightShopStock: Probe("night-stock", () => ItemStockRuntime.IsAvailable),
                RunDataStaging: runReady,
                DirectInventoryRead: runReady,
                WholeTransferPreview: runReady,
                NativeEffectRegistration: Probe("native-effects", () => EffectApi.IsNativeRegistrationReady),
                InventoryTransfer: runReady && Probe("inventory-transfer", () => InventoryRuntime.TransferReady),
                MachineNightProcessing: Probe("machine-night", () => MachineRuntimeApi.RuntimeInstalled),
                NpcTradeStock: Probe("npc-stock", () => NpcStockRuntime.IsAvailable),
                DossierExpansion: false,
                PersistentInventoryEndpoints: Probe("inventory-endpoints", () => InventoryEndpointApi.IsAvailable),
                ExternalPowerTransactions: Probe("external-power", () => ExternalPowerApi.IsAvailable),
                BranchingDialogue: Probe("dialogue", () => DialogueApi.IsAvailable),
                ClientVisits: Probe("client-visits", () => ClientVisitApi.IsAvailable),
                ReadingWindow: Probe("reading", () => ReadingApi.IsAvailable),
                PhoneContacts: Probe("phone", () => PhoneApi.IsAvailable),
                SupplierVisits: Probe("suppliers", () => SupplierApi.IsAvailable));
        }
    }

    private static bool Probe(string feature, Func<bool> read)
    {
        try { return read(); }
        catch (Exception error)
        {
            if (ReportedProbeFailures.Add(feature))
                MelonLogger.Warning("[EZapi] Runtime capability probe failed closed: " + feature + " (" + error.GetType().Name + ").");
            return false;
        }
    }
}

/// <summary>Recognizes only the Assembly-CSharp build recorded in the checked-in official-source evidence.</summary>
internal static class GameBuildFingerprint
{
    internal const string KnownSha256 = "E9514545697169936C98D040CF7B1F7CF5E80405F7DD3F0195A80B65DB2752CF";
    private static bool? _known;

    internal static bool IsKnown
    {
        get
        {
            if (_known.HasValue) return _known.Value;
            _known = ReadAndMatch();
            return _known.Value;
        }
    }

    internal static bool Matches(string? sha256) =>
        string.Equals(sha256, KnownSha256, StringComparison.OrdinalIgnoreCase);

    private static bool ReadAndMatch()
    {
        try
        {
            var path = typeof(PlayerStore).Assembly.Location;
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            return Matches(Convert.ToHexString(sha.ComputeHash(stream)));
        }
        catch (Exception error)
        {
            MelonLogger.Warning("[EZapi] Could not read the game build fingerprint; KnownGameBuild is false (" + error.GetType().Name + ").");
            return false;
        }
    }
}

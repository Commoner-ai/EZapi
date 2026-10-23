namespace EZapi;

/// <summary>
/// Searchable native faction reputation presentation. Native storeReputations entries are discovered automatically.
/// For Mod-owned data, register appearance plus a read-only provider. The Mod retains gameplay and saving ownership.
/// </summary>
public static class FactionUiApi
{
    internal static readonly FactionUiCatalog Catalog = new();
    private static bool _started;
    public static bool IsAvailable => _started && GameSession.OnThread && GameBuildFingerprint.IsKnown;
    internal static void Start() => _started=true;
    /// <summary>Register before or after loading. A queued declaration becomes visible when the native reputation window opens.</summary>
    public static PresentationResult Register(string ownerId, FactionUiDefinition definition, Func<FactionUiContext,FactionReputationView?> readState)
    {
        if (!IsAvailable) return new(PresentationStatus.Unavailable,"EZAPI_FACTION_THREAD_OR_BUILD");
        var result=Catalog.Register(ownerId,definition,readState);
        if(result.Status==PresentationStatus.Queued)FactionReputationBrowser.RefreshSoon();
        return result;
    }
    /// <summary>Read a strict JSON appearance definition, then register its read-only state provider. Does not own the stream.</summary>
    public static PresentationResult RegisterJson(string ownerId, Stream definitionJson, Func<FactionUiContext,FactionReputationView?> readState)
        => Register(ownerId,FactionUiJson.Parse(definitionJson),readState);
    /// <summary>Unregister only the caller's presentation. No native faction, reputation, perk or save is removed.</summary>
    public static PresentationResult Unregister(string ownerId,string factionId)
    {
        if(!IsAvailable)return new(PresentationStatus.Unavailable,"EZAPI_FACTION_THREAD_OR_BUILD");
        var result=Catalog.Remove(ownerId,factionId);FactionReputationBrowser.RefreshSoon();return result;
    }
    /// <summary>Read immutable current native and registered faction views. No loaded store produces an empty list.</summary>
    public static IReadOnlyList<FactionUiSnapshot> Snapshot()
    { GameSession.RequireThread();return FactionReputationBrowser.ReadCurrent(); }
    /// <summary>Open the native reputation window with the EZapi presentation. Never alters reputation or requests saving.</summary>
    public static PresentationResult Open() => IsAvailable ? FactionReputationBrowser.Open() : new(PresentationStatus.Unavailable,"EZAPI_FACTION_THREAD_OR_BUILD");
    internal static void Stop()
    { _started=false;FactionReputationBrowser.Stop();Catalog.Entries.Clear();Catalog.Errors.Clear(); }
}

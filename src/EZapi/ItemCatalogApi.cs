using Il2Cpp;

namespace EZapi;

/// <summary>
/// Discover the actual native GameItem directory, including vanilla and other Mods.
/// Unlike ItemApi.Snapshot/FactorySnapshot this is not restricted to EZapi declarations.
/// Uses official public directory/factory methods; does not register, deliver or save items.
/// </summary>
public static class ItemCatalogApi
{
    /// <summary>Safety ceiling. Exceeding it rejects the entire capture, never silently truncates IDs.</summary>
    public const int MaximumIdentifiers = 65536;

    /// <summary>Copy and sort all current native item IDs without calling factories. Loaded game main thread only.</summary>
    public static IReadOnlyList<string> CaptureIdentifiers()
    {
        var source=new NativeSource();
        var native=DirectoryMaster.GetIdentifierList<GameItem>();
        if(native is null || native.Count==0)
            throw new InvalidOperationException("EZAPI_CATALOG_NOT_READY: native item directory is empty or unavailable.");
        if(native.Count>MaximumIdentifiers)
            throw new InvalidOperationException("EZAPI_CATALOG_LIMIT: no partial directory returned.");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var id in native)
        {
            if(!ItemCatalogRules.ValidId(id)) throw new InvalidOperationException("EZAPI_CATALOG_ID: native directory contains an invalid ID.");
            ids.Add(id);
        }
        if(!source.IsCurrent) throw new InvalidOperationException("EZAPI_CATALOG_STALE: run or locale changed while capturing IDs.");
        return Array.AsReadOnly(ids.OrderBy(id=>id,StringComparer.Ordinal).ToArray());
    }

    /// <summary>Capture all native IDs, then explicitly call Step in Update to read factory metadata across frames.</summary>
    public static ItemCatalogScan BeginScan()=>BeginScan(CaptureIdentifiers());

    /// <summary>Scan an explicit ID subset. IDs are copied, validated, deduplicated and sorted; no factories execute here.</summary>
    public static ItemCatalogScan BeginScan(IReadOnlyList<string> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if(itemIds.Count>MaximumIdentifiers) throw new ArgumentException("EZAPI_CATALOG_LIMIT: too many IDs.");
        return new(new NativeSource(),itemIds);
    }

    private sealed class NativeSource : IItemCatalogSource
    {
        public GameRunIdentity Run { get; }
        public string LocaleCode { get; }
        internal NativeSource()
        {
            Run=NativeAccess.RequireRun();
            if(!GameBuildFingerprint.IsKnown) throw new InvalidOperationException("EZAPI_CATALOG_BUILD: unsupported game build.");
            var language=GameLanguage.Current;
            if(language.IsFallback) throw new InvalidOperationException("EZAPI_CATALOG_LOCALE: current native locale unavailable.");
            // The locale code can change before Unity's localized tables finish
            // loading. Reuse the async locale gate used by native UI previews;
            // never force synchronous table completion from an item factory.
            if(!NativeGuideLocale.IsReady) throw new InvalidOperationException("EZAPI_CATALOG_LOCALE: native language resources are still loading; begin again after readiness.");
            LocaleCode=language.LocaleCode;
        }
        public void RequireThread()=>GameSession.RequireThread();
        public bool IsCurrent
        {
            get
            {
                if(!NativeAccess.SameRun(Run)) return false;
                var language=GameLanguage.Current;
                return !language.IsFallback && language.LocaleCode==LocaleCode;
            }
        }
        public ItemCatalogEntry Read(string id)
        {
            if(!IsCurrent) throw new InvalidOperationException("EZAPI_CATALOG_STALE");
            GameItem? item=null;
            try
            {
                // Factory execution is explicit and may run arbitrary Mod callbacks.
                // Never instantiate in OnGUI, on a worker, or once per visible card.
                item=ItemApi.Create(id,owned:false);
                if(!IsCurrent) throw new InvalidOperationException("EZAPI_CATALOG_STALE");
                var name=item.GetDisplayName();
                if(!IsCurrent) throw new InvalidOperationException("EZAPI_CATALOG_STALE");
                var value=item.GetBaseValue();
                if(!IsCurrent) throw new InvalidOperationException("EZAPI_CATALOG_STALE");
                return new(id,name,value,item.spriteAtlasPath??"",item.spritePath??"");
            }
            finally
            {
                if(item is not null && NativeAccess.SameRun(Run))
                {
                    try
                    {
                        if(item.Pointer==IntPtr.Zero || item.parentInventory is not null)
                            throw new InvalidOperationException("Factory item is no longer detached.");
                        item.Destroy();
                    }
                    catch(Exception error) { throw new ItemCatalogCleanupException(error); }
                }
                // A factory that changed scenes invalidates the scan. Do not
                // dereference its old native wrapper in the newly loaded scene.
            }
        }
    }
}

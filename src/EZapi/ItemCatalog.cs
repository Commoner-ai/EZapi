using System.Diagnostics;

namespace EZapi;

/// <summary>Catalog scan lifecycle. Complete describes metadata collection, not item artwork or gameplay acceptance.</summary>
public enum ItemCatalogStatus { Reading, Complete, Invalidated, Failed, Cancelled }

/// <summary>Managed native factory metadata in one captured game locale. No GameItem, Sprite or inventory references.</summary>
public sealed record ItemCatalogEntry(string Id, string DisplayName, long BaseValue, string SpriteAtlasPath, string SpritePath);

/// <summary>One factory's metadata could not be read. Reason is a stable code plus exception type, not a success placeholder.</summary>
public sealed record ItemCatalogFailure(string Id, string Reason);

/// <summary>Progress never exposes an incomplete product pool as a completed catalog. Total includes failed entries.</summary>
public sealed record ItemCatalogProgress(ItemCatalogStatus Status, GameRunIdentity Run, string LocaleCode,
    int Total, int Processed, int Accepted, int Rejected, string? Reason = null);

// Separate the batch engine from the native boundary so tests execute the real
// scheduling, invalidation, cleanup and result publication code.
internal interface IItemCatalogSource
{
    GameRunIdentity Run { get; }
    string LocaleCode { get; }
    bool IsCurrent { get; }
    void RequireThread();
    ItemCatalogEntry Read(string id);
}

/// <summary>
/// Explicit main-thread incremental scan. Call Step from Update; no hidden polling or background Unity work.
/// Captures ID strings only; transient factory objects are destroyed by the native adapter before each read returns.
/// Reload/scene/locale changes invalidate the entire scan, including a completed one.
/// </summary>
public sealed class ItemCatalogScan : IDisposable
{
    private readonly IItemCatalogSource _source;
    private readonly string[] _ids;
    private readonly List<ItemCatalogEntry> _entries = new();
    private readonly List<ItemCatalogFailure> _failures = new();
    private IReadOnlyList<ItemCatalogEntry>? _published;
    private int _index;
    private bool _stepping;
    private ItemCatalogStatus _status = ItemCatalogStatus.Reading;
    private string? _reason;

    internal ItemCatalogScan(IItemCatalogSource source, IEnumerable<string> ids)
    {
        _source=source;
        var unique=new HashSet<string>(StringComparer.Ordinal);
        foreach(var id in ids)
        {
            if(!ItemCatalogRules.ValidId(id)) throw new ArgumentException("EZAPI_CATALOG_ID: readable native ID, at most 256 characters required.");
            unique.Add(id);
            if(unique.Count>ItemCatalogApi.MaximumIdentifiers)
                throw new InvalidOperationException("EZAPI_CATALOG_LIMIT: directory exceeds the supported limit; no partial catalog returned.");
        }
        _ids=unique.OrderBy(id=>id,StringComparer.Ordinal).ToArray();
    }

    /// <summary>Read the current run/locale gate and return immutable counts. Use only on the initialized game main thread.</summary>
    public ItemCatalogProgress Progress
    {
        get { _source.RequireThread(); CheckCurrent(); return View(); }
    }

    /// <summary>
    /// Attempt at most maximumItems factories, checking the time budget between calls.
    /// A single synchronous native factory may exceed the budget; it cannot safely be preempted.
    /// An individual factory failure is recorded and scanning continues. No automatic retries.
    /// </summary>
    public ItemCatalogProgress Step(int maximumItems = 4, double timeBudgetMilliseconds = 4)
    {
        _source.RequireThread();
        if(maximumItems is <1 or >64 || !double.IsFinite(timeBudgetMilliseconds) || timeBudgetMilliseconds is <=0 or >50)
            throw new ArgumentOutOfRangeException(nameof(maximumItems),"EZAPI_CATALOG_BUDGET: 1..64 items and >0..50 milliseconds required.");
        if(_stepping) throw new InvalidOperationException("EZAPI_CATALOG_REENTRY: do not step a scan inside a factory.");
        if(!CheckCurrent() || _status!=ItemCatalogStatus.Reading) return View();
        _stepping=true;
        try
        {
            var clock=Stopwatch.StartNew();
            for(var n=0;_index<_ids.Length && n<maximumItems && (n==0 || clock.Elapsed.TotalMilliseconds<timeBudgetMilliseconds);n++)
            {
                var id=_ids[_index++];
                try
                {
                    var entry=_source.Read(id);
                    if(!CheckCurrent()) break;
                    if(entry.Id!=id || string.IsNullOrWhiteSpace(entry.DisplayName) || entry.BaseValue<0)
                        throw new InvalidOperationException("EZAPI_CATALOG_METADATA: factory metadata is invalid.");
                    _entries.Add(entry);
                }
                catch(ItemCatalogCleanupException)
                {
                    _status=ItemCatalogStatus.Failed;
                    _reason="EZAPI_CATALOG_CLEANUP: detached factory instance cleanup could not be confirmed.";
                    _entries.Clear(); break;
                }
                catch(Exception error)
                {
                    if(!CheckCurrent()) break;
                    _failures.Add(new(id,"EZAPI_CATALOG_FACTORY: "+error.GetType().Name));
                }
            }
            if(CheckCurrent() && _status==ItemCatalogStatus.Reading && _index==_ids.Length)
            {
                _published=Array.AsReadOnly(_entries.ToArray());
                _status=ItemCatalogStatus.Complete;
            }
            return View();
        }
        finally { _stepping=false; }
    }

    /// <summary>All successful metadata, sorted by ordinal ID. Throws until Complete or after run/locale invalidation.</summary>
    public IReadOnlyList<ItemCatalogEntry> Snapshot()
    {
        _source.RequireThread(); CheckCurrent();
        if(_status!=ItemCatalogStatus.Complete)
            throw new InvalidOperationException("EZAPI_CATALOG_INCOMPLETE: check Progress before reading the complete pool.");
        return _published!;
    }

    /// <summary>Per-item failures collected so far, copied without native object references.</summary>
    public IReadOnlyList<ItemCatalogFailure> Failures
    {
        get { _source.RequireThread(); CheckCurrent(); return Array.AsReadOnly(_failures.ToArray()); }
    }

    /// <summary>Release managed scan data. Does not destroy or alter the game's catalog, items or saves.</summary>
    public void Dispose()
    {
        _source.RequireThread();
        if(_stepping) throw new InvalidOperationException("EZAPI_CATALOG_REENTRY: do not cancel a scan inside a factory.");
        _status=ItemCatalogStatus.Cancelled; _entries.Clear(); _failures.Clear(); _published=null;
        _reason="EZAPI_CATALOG_CANCELLED";
    }

    private bool CheckCurrent()
    {
        if(_status is ItemCatalogStatus.Cancelled or ItemCatalogStatus.Invalidated or ItemCatalogStatus.Failed) return false;
        if(_source.IsCurrent) return true;
        _status=ItemCatalogStatus.Invalidated; _reason="EZAPI_CATALOG_STALE: run, scene or game locale changed; start a new scan.";
        _entries.Clear(); _published=null; return false;
    }
    private ItemCatalogProgress View()=>new(_status,_source.Run,_source.LocaleCode,_ids.Length,_index,_entries.Count,_failures.Count,_reason);
}

internal sealed class ItemCatalogCleanupException : Exception
{
    internal ItemCatalogCleanupException(Exception cause):base("EZAPI_CATALOG_CLEANUP",cause) { }
}
internal static class ItemCatalogRules
{
    internal static bool ValidId(string? id)=>!string.IsNullOrWhiteSpace(id) && id.Length<=256 && !id.Any(char.IsControl);
}

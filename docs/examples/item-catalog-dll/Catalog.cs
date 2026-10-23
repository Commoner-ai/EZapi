using EZapi;

namespace Author.Stock;

/// <summary>
/// Call Begin from a loaded-run main-thread action, Tick once per Update,
/// and Dispose from scene/reload/shutdown callbacks. This is a consumer helper,
/// not a complete auto-loaded Mod. No Unity or game reference needed to compile.
/// </summary>
public sealed class Catalog : IDisposable
{
    private ItemCatalogScan? _scan;
    private bool _published;
    public IReadOnlyList<ItemCatalogEntry> Stock { get; private set; }=Array.Empty<ItemCatalogEntry>();
    public IReadOnlyList<ItemCatalogFailure> Failures { get; private set; }=Array.Empty<ItemCatalogFailure>();
    public ItemCatalogProgress? Progress { get; private set; }

    public void Begin()
    {
        Dispose();
        _scan=ItemCatalogApi.BeginScan();
    }

    public void Tick()
    {
        if(_scan is null) return;
        Progress=_scan.Step();
        if(Progress.Status!=ItemCatalogStatus.Complete)
        {
            // Never keep an earlier locale/run's stock after invalidation.
            Stock=Array.Empty<ItemCatalogEntry>();
            Failures=Progress.Status is ItemCatalogStatus.Invalidated or ItemCatalogStatus.Cancelled
                ?Array.Empty<ItemCatalogFailure>():_scan.Failures;
            _published=false;
            return;
        }
        if(_published) return;
        Stock=Array.AsReadOnly(_scan.Snapshot().Where(item=>item.BaseValue>150).ToArray());
        Failures=_scan.Failures;
        _published=true;
        // Keep ticking Progress so locale/run invalidation clears Stock. Do not
        // construct factory objects per frame; Complete Step performs no reads.
    }

    public void Dispose()
    {
        _scan?.Dispose();_scan=null;_published=false;
        Stock=Array.Empty<ItemCatalogEntry>();Failures=Array.Empty<ItemCatalogFailure>();Progress=null;
    }
}

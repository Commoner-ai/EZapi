using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;

namespace EZapi;

/// <summary>Optional manual catalog probe; same public API available to content Mods. No automatic factory scans.</summary>
internal static class DebugItemCatalog
{
    private static ItemCatalogScan? _scan;
    internal static object? LastReport { get; private set; }
    internal static string Status { get; private set; }="";
    internal static string Start()
    {
        Clear();
        try
        {
            _scan=ItemCatalogApi.BeginScan();
            Status=GameLanguage.Resolve(new("正在分帧读取全物品目录…","Reading the complete item directory across frames..."));
        }
        catch(Exception error) { Status=error.Message; MelonLogger.Warning("[EZapi] EZAPI_CATALOG_DEBUG: "+error); }
        return Status;
    }
    internal static void Update()
    {
        if(_scan is null) return;
        try
        {
            var progress=_scan.Step();
            Status=$"{progress.Status}: {progress.Processed}/{progress.Total}; {progress.Accepted} OK, {progress.Rejected} failed";
            if(progress.Status==ItemCatalogStatus.Reading) return;
            LastReport=new{progress,entries=progress.Status==ItemCatalogStatus.Complete?_scan.Snapshot():Array.Empty<ItemCatalogEntry>(),failures=_scan.Failures,
                scope="native metadata only; no artwork, delivery or save acceptance"};
            var path=Path.Combine(MelonEnvironment.UserDataDirectory,"EZapi","item-catalog.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,JsonSerializer.Serialize(LastReport,new JsonSerializerOptions{WriteIndented=true}));
            MelonLogger.Msg("[EZapi] Item catalog probe: "+Status+"; exported UserData/EZapi/item-catalog.json");
            _scan.Dispose();_scan=null;
            EZapiBootstrap.WriteDiagnostics();
        }
        catch(Exception error)
        {
            LastReport=new{error=error.GetType().Name};
            _scan?.Dispose();_scan=null;Status="EZAPI_CATALOG_DEBUG: "+error.GetType().Name;
            MelonLogger.Warning("[EZapi] "+Status);
        }
    }
    internal static void Clear() { _scan?.Dispose();_scan=null;LastReport=null;Status=""; }
}

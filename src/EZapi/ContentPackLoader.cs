using MelonLoader;
namespace EZapi;

/// <summary>运行时复用离线整包检查和依赖排序；每个独立包单独隔离失败。</summary>
internal static class ContentPackLoader
{
    private static readonly List<object> Results=new();
    internal static object Report()=>Results.ToArray();
    internal static void Clear()=>Results.Clear();
    internal static void LoadAll(string root,Action<string> recordError)
    {
        Results.Clear();
        if(!Directory.Exists(root))return;
        var plan=ContentPacks.Plan(root);
        foreach(var rejected in plan.Results.Where(r=>!r.Valid)) {
            var error=rejected.Folder+": "+rejected.Error;
            Results.Add(new {folder=rejected.Folder,id=rejected.Id,state="Rejected",error=rejected.Error});
            recordError(error);MelonLogger.Error("[EZapi] EZAPI_PACK_FAILED "+error);
        }
        foreach(var pack in plan.Order) {
            try {
                var result=Load(pack);
                if(result.Any(r=>!r.Accepted))throw new InvalidDataException(result.First(r=>!r.Accepted).Message);
                Results.Add(new {folder=pack.Folder,id=pack.Manifest.Id,state="Queued",version=pack.Manifest.Version,localization=pack.Localization.Index});
                MelonLogger.Msg("[EZapi] Queued pack "+pack.Manifest.Id+": "+result.Count+" item(s).");
            } catch(Exception e) {
                Results.Add(new {folder=pack.Folder,id=pack.Manifest.Id,state="Rejected",error=e.Message});
                recordError(pack.Folder+": "+e.Message);
                MelonLogger.Error("[EZapi] EZAPI_PACK_FAILED "+pack.Folder+": "+e);
            }
        }
    }
    internal static IReadOnlyList<ItemRegistrationResult> Load(string folder)=>Load(ContentPacks.Read(folder));
    private static IReadOnlyList<ItemRegistrationResult> Load(CheckedPack pack)
    {
        foreach(var dependency in pack.Manifest.Dependencies)
            if(!ItemRegistry.HasPack(dependency.Id,dependency.MinVersion))
                throw new InvalidDataException("EZAPI_PACK_DEPENDENCY: dependency not queued: "+dependency.Id+" >= "+dependency.MinVersion);
        // Prepare 不修改原生 journal。资源准备失败时，也不占用包版本、物品或掉落 ID。
        var prepared=LootTables.Prepare(pack.Manifest.Id,pack.Loot);
        var results=ItemRegistry.RegisterPack(pack.Items,pack.Manifest.Id,pack.Manifest.Name,pack.Folder,prepared,pack.Manifest.Version);
        if(results.All(r=>r.Accepted)) {
            Localization.CommitPack(pack.Manifest.Id,pack.Localization);
            foreach(var warning in pack.Localization.Index.Warnings)
                MelonLogger.Warning("[EZapi] EZAPI_LOCALIZATION_COVERAGE "+pack.Manifest.Id+": "+warning);
        }
        return results;
    }
}

namespace EZapi;

/// <summary>主线程图鉴目录。按owner原子替换；空组移除该owner；不创建物品、不改原生配方、库存或存档。</summary>
public static class RecipeGuideApi
{
    private static readonly Dictionary<string,IReadOnlyList<RecipeGuideEntry>> Entries=new(StringComparer.Ordinal);
    // 自动图鉴按机器分区，与作者手动整组替换互不覆盖。
    private static readonly Dictionary<string,IReadOnlyList<RecipeGuideEntry>> Managed=new(StringComparer.Ordinal);
    private static IEnumerable<RecipeGuideEntry> All()=>Entries.Values.SelectMany(x=>x).Concat(Managed.Values.SelectMany(x=>x));
    internal static string? CheckManaged(string machine,IReadOnlyList<RecipeGuideEntry> copy)
    {
        if(!_started)return "EZAPI_GUIDE_NOT_READY";
        var other=Entries.Values.SelectMany(x=>x).Concat(Managed.Where(e=>e.Key!=machine).SelectMany(e=>e.Value)).ToArray();
        return CheckCombined(other,copy);
    }
    private static string? CheckCombined(IEnumerable<RecipeGuideEntry> other,IReadOnlyList<RecipeGuideEntry> copy)
    {
        var all=other.Concat(copy).ToArray();
        if(all.Length>2048||all.GroupBy(e=>e.OwnerId).Any(g=>g.Count()>512))return "EZAPI_GUIDE_LIMIT: max512 per owner /2048 total.";
        if(all.GroupBy(e=>(e.OwnerId,e.RecipeId)).Any(g=>g.Count()>1))return "EZAPI_GUIDE_DUPLICATE: manual/automatic recipe ID already claimed.";
        return null;
    }
    internal static void CommitManaged(string machine,IReadOnlyList<RecipeGuideEntry> copy){Managed[machine]=copy;_revision++;}
    internal static void RemoveManaged(string machine){if(Managed.Remove(machine))_revision++;}
    private static long _revision;
    private static bool _started;
    internal static long Revision=>_revision;
    public static bool IsVisible=>RecipeGuideBrowser.Visible;
    public static void Close(){GameSession.RequireThread();RecipeGuideBrowser.Close();}
    public static bool Open(){GameSession.RequireThread();return _started&&RecipeGuideBrowser.Open();}
    public static ContentSubmissionResult RegisterJson(string ownerId,Stream json)=>RegisterRecipes(ownerId,JsonRecipeGuide.Parse(ownerId,json));
    public static ContentSubmissionResult RegisterRecipes(string ownerId,IReadOnlyList<RecipeGuideEntry> recipes)
    {
        GameSession.RequireThread();
        if(!_started)return new(false,"EZAPI_GUIDE_NOT_READY: initialize EZapi first.");
        IReadOnlyList<RecipeGuideEntry> copy;
        try {copy=RecipeGuideRules.Copy(ownerId,recipes);}catch(ArgumentException e){return new(false,e.Message);}
        var rejection=CheckCombined(Entries.Where(e=>e.Key!=ownerId).SelectMany(e=>e.Value).Concat(Managed.Values.SelectMany(x=>x)),copy);
        if(rejection is not null)return new(false,rejection);
        if(copy.Count==0)Entries.Remove(ownerId);else Entries[ownerId]=copy;
        _revision++;return new(true,null);
    }
    public static RecipeGuideSnapshot Snapshot()
    {
        GameSession.RequireThread();return new(_revision,Array.AsReadOnly(All().OrderBy(e=>e.OwnerId,StringComparer.Ordinal).ThenBy(e=>e.RecipeId,StringComparer.Ordinal).ToArray()));
    }
    internal static void Start(){_started=true;}
    internal static void Stop(){RecipeGuideBrowser.Close();NativeGuideLocale.Stop();_started=false;Entries.Clear();Managed.Clear();_revision++;}
}


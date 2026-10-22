using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
namespace EZapi;

/// <summary>逻辑注册种类；统一进入官方ModItemDirectory，不伪造原生商店/掉落目录资格。</summary>
public enum NativeItemKind { Item, Node, Amenity, Module }
/// <summary>固定或中英双语的显示文本。null保留工厂值；空串明确清空；不创建商店货源或使用行为。</summary>
public sealed record NativeItemOptions(string? Name = null, string? ShortDescription = null,
    string? FlavorText = null, string? SourceName = null, bool ShowSource = true,
    LocalizedItemText? LocalizedName = null, LocalizedItemText? LocalizedShortDescription = null,
    LocalizedItemText? LocalizedFlavorText = null, LocalizedItemText? LocalizedSourceName = null)
{
    /// <summary>夜间商店供应策略；默认不加入。Unique按当前玩家持有情况判断。</summary>
    public NightShopStockPolicy NightShop { get; init; }
    /// <summary>NPC分类随机货源。BuildPool/TryRoll可用；原版NPC自动路由须另查运行时能力。</summary>
    public NpcTradeStockOptions? NpcTrade { get; init; }
    /// <summary>每次补货按需执行一次；异常只排除本物品。主线程，不允许保存或换场景。</summary>
    public Func<bool>? IsNightShopAvailable { get; init; }
    internal string? ResolveName(string? locale) => LocalizedName?.ForLocale(locale) ?? Name;
    internal string? ResolveShortDescription(string? locale) => LocalizedShortDescription?.ForLocale(locale) ?? ShortDescription;
    internal string? ResolveFlavorText(string? locale) => LocalizedFlavorText?.ForLocale(locale) ?? FlavorText;
    internal string? ResolveSourceName(string? locale) => LocalizedSourceName?.ForLocale(locale) ?? SourceName;
}
/// <summary>工厂每次须返回ID匹配、未入库存的独立实例；禁止缓存实例，异常前须自行销毁已创建对象。</summary>
public sealed record NativeModuleRegistration(string ItemId, Func<GameItem> Factory, NativeItemOptions? Options = null);
/// <summary>目录应用与工厂执行分别报告；Applied不代表工厂/玩法/持久化已验证。只有托管数据。</summary>
public sealed record NativeItemFactoryView(string OwnerId, string ItemId, NativeItemKind Kind,
    NativeApplicationStatus Status, long Created, long Failures, string? LastError, bool Creating);

/// <summary>DLL作者的低层工厂入口。初始化阶段注册，不在声明时创建原生物品。JSON与DLL共享ID空间。</summary>
public static class ItemApi
{
    public static ContentSubmissionResult RegisterItem(string ownerId, string itemId, Func<GameItem> factory, NativeItemOptions? options = null)
        => NativeFactoryRegistry.Submit(ownerId, NativeItemKind.Item, new[] { new NativeModuleRegistration(itemId, factory, options) });
    public static ContentSubmissionResult RegisterNode(string ownerId, string itemId, Func<GameItem> factory, NativeItemOptions? options = null)
        => NativeFactoryRegistry.Submit(ownerId, NativeItemKind.Node, new[] { new NativeModuleRegistration(itemId, factory, options) });
    public static ContentSubmissionResult RegisterAmenity(string ownerId, string itemId, Func<GameItem> factory, NativeItemOptions? options = null)
        => NativeFactoryRegistry.Submit(ownerId, NativeItemKind.Amenity, new[] { new NativeModuleRegistration(itemId, factory, options) });
    public static ContentSubmissionResult RegisterModule(string ownerId, string itemId, Func<GameItem> factory, NativeItemOptions? options = null)
        => NativeFactoryRegistry.Submit(ownerId, NativeItemKind.Module, new[] { new NativeModuleRegistration(itemId, factory, options) });
    /// <summary>整批预检后统一入队；不执行工厂。原生Add逐项报告，不承诺原生目录的批量回滚。</summary>
    public static ContentSubmissionResult RegisterModules(string ownerId, IReadOnlyList<NativeModuleRegistration> modules)
        => NativeFactoryRegistry.Submit(ownerId, NativeItemKind.Module, modules);
    public static IReadOnlyList<NativeApplicationView> Snapshot() => ItemRegistry.Snapshot();
    public static IReadOnlyList<NativeItemFactoryView> FactorySnapshot() => NativeFactoryRegistry.Snapshot();
    public static IReadOnlyList<ItemStockView> StockSnapshot() => ItemStockRuntime.Snapshot();
    public static bool Exists(string itemId) => Items.Exists(itemId);
    public static GameItem Create(string itemId, bool owned = true) => Items.Create(itemId, owned);
    public static string GetDisplayName(string itemId)
    {
        NativeAccess.RequireRun();
        return NativeFactoryRegistry.GetDisplayName(itemId) ?? Items.GetDisplayName(itemId);
    }
}

internal static class NativeFactoryRegistry
{
    private sealed class Entry
    {
        internal readonly string Owner;
        internal readonly NativeItemKind Kind;
        internal readonly NativeModuleRegistration Definition;
        internal NativeApplicationView View;
        internal Il2CppSystem.Func<GameItem>? NativeFactory;
        internal long Created, Failures;
        internal string? LastError;
        internal bool Creating;
        internal IntPtr LastPointer;
        internal int LastUnique;
        // 保留最近一次原生包装对象的GC根，直到下一次成功创建或清理目录。
        // unowned物品uniqueId可能为0；只记地址/ID会把回收后复用地址的新实例误判成旧对象。
        internal GameItem? LastInstance;
        internal Entry(string owner, NativeItemKind kind, NativeModuleRegistration definition)
        {
            Owner=owner; Kind=kind; Definition=definition;
            View=new(owner, definition.ItemId, kind.ToString().ToLowerInvariant(), NativeApplicationStatus.Staged, null);
        }
    }
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    // 旧目录仍可能持有原生委托，保留接收器直到进程退出；generation使其停止后保持惰性。
    private static readonly List<Il2CppSystem.Func<GameItem>> Retired = new();
    private static long _generation;
    internal static IEnumerable<(string Id,NightShopStockPolicy Policy,Func<bool>? Available)> StockDeclarations()
        =>Entries.Values.Where(e=>e.Definition.Options is { NightShop: not NightShopStockPolicy.None }).Select(e=>(e.Definition.ItemId,e.Definition.Options!.NightShop,e.Definition.Options.IsNightShopAvailable));
    internal static IEnumerable<(string Owner,string Id,NpcTradeStockOptions Options)> NpcStockDeclarations()
        =>Entries.Values.Where(e=>e.Definition.Options?.NpcTrade is not null).Select(e=>(e.Owner,e.Definition.ItemId,e.Definition.Options!.NpcTrade!));
    internal static bool Contains(string id) => Entries.ContainsKey(id);
    // 原生UNKNOWN占位物也可能沿用请求ID；只有本注册表知道本次作者工厂是否失败。
    internal static bool HasCreationError(string id)=>Entries.TryGetValue(id,out var e)&&e.LastError is not null;
    private static bool ValidLocalized(LocalizedItemText? text,int maximum,bool requireText)
    {
        if(text is null)return true;
        if(text.Key is not null)return false;
        var values=new[]{text.Chinese,text.English};
        if(requireText&&(!values.Any(value=>value is not null)||values.Any(value=>value is not null&&string.IsNullOrWhiteSpace(value))))return false;
        return values.All(value=>value is null||value.Length<=maximum&&(!requireText||!value.Any(char.IsControl)));
    }
    internal static ContentSubmissionResult Submit(string owner, NativeItemKind kind, IReadOnlyList<NativeModuleRegistration> definitions, bool publish = true)
    {
        GameSession.RequireThread(); RunData.ValidateModId(owner); ArgumentNullException.ThrowIfNull(definitions);
        var rejection=ItemRegistry.DeclarationRegistrationError();
        if(rejection is not null)return new(false,rejection);
        if(definitions.Count is <1 or >500 || Entries.Count+definitions.Count>4096)
            return new(false,"EZAPI_FACTORY_LIMIT: max500 per batch /4096 custom factories.");
        var copy=definitions.ToArray(); var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var d in copy) {
            if(d is null || d.Factory is null || d.ItemId is null || d.ItemId.Length>160 ||
                !System.Text.RegularExpressions.Regex.IsMatch(d.ItemId,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)+\z") ||
                !d.ItemId.StartsWith(owner+".",StringComparison.Ordinal) || d.ItemId.Length<=owner.Length+1)
                return new(false,"EZAPI_FACTORY_ID: nonnull factory and lowercase owner-prefixed ID required.");
            if(!ids.Add(d.ItemId)||ItemRegistry.ContainsDeclaration(d.ItemId))return new(false,"EZAPI_ITEM_DUPLICATE: "+d.ItemId);
            var o=d.Options;
            if(o?.NpcTrade is {} npc)try{NpcStockPlanner.Validate(npc);}catch(ArgumentException ex){return new(false,ex.Message);}
            if(o is not null && !Enum.IsDefined(typeof(NightShopStockPolicy),o.NightShop))return new(false,"EZAPI_STOCK_POLICY: unknown night stock policy.");
            if(o is not null && (o.Name is not null && (string.IsNullOrWhiteSpace(o.Name)||o.Name.Length>200||o.Name.Any(char.IsControl)) ||
                o.ShortDescription?.Length>4000 || o.FlavorText?.Length>4000 ||
                o.SourceName is not null && (string.IsNullOrWhiteSpace(o.SourceName)||o.SourceName.Length>200||o.SourceName.Any(char.IsControl)) ||
                !ValidLocalized(o.LocalizedName,200,true) || !ValidLocalized(o.LocalizedShortDescription,4000,false) ||
                !ValidLocalized(o.LocalizedFlavorText,4000,false) || !ValidLocalized(o.LocalizedSourceName,200,true)))
                return new(false,"EZAPI_FACTORY_TEXT: invalid name/source or description length.");
        }
        var entries=copy.Select(d=>new Entry(owner,kind,d with {Options=d.Options is null?new():d.Options with {}})).ToArray();
        foreach(var entry in entries)Entries.Add(entry.Definition.ItemId,entry);
        if(publish)ContentApi.PublishBatch(entries.Select(e=>e.View));
        return new(true,null);
    }
    internal static IReadOnlyList<NativeApplicationView> Views() => Entries.Values.Select(e=>e.View).ToArray();
    internal static IReadOnlyList<NativeItemFactoryView> Snapshot()
    {
        GameSession.RequireThread();
        return Entries.Values.OrderBy(e=>e.Definition.ItemId,StringComparer.Ordinal).Select(e=>new NativeItemFactoryView(
            e.Owner,e.Definition.ItemId,e.Kind,e.View.Status,e.Created,e.Failures,e.LastError,e.Creating)).ToArray();
    }
    internal static bool TryPresentation(string id,out ItemDefinition definition)
    {
        definition=null!;
        if(!Entries.TryGetValue(id,out var e)||e.View.Status!=NativeApplicationStatus.Applied)return false;
        var options=e.Definition.Options!;
        var locale=NativeItemLocalization.CurrentLocaleCode;
        definition=new ItemDefinition {
            Id=e.Definition.ItemId, ModId=e.Owner, Name=options.ResolveName(locale)??e.Definition.ItemId, LocalizedName=options.LocalizedName,LocalizedShortDescription=options.LocalizedShortDescription,LocalizedFlavorText=options.LocalizedFlavorText,
            Tooltip=new() {ShowSource=options.ShowSource,SourceName=options.ResolveSourceName(locale)??PresentationApi.ResolveOwnerDisplayName(e.Owner,locale)??e.Owner,LocalizedSourceName=options.LocalizedSourceName}
        };
        return true;
    }
    internal static string? GetDisplayName(string id)
    {
        if(!Entries.TryGetValue(id,out var e)||e.View.Status!=NativeApplicationStatus.Applied)return null;
        return e.Definition.Options!.ResolveName(NativeItemLocalization.CurrentLocaleCode);
    }
    internal static bool IsOwnedModule(string owner,string id)
        =>Entries.TryGetValue(id,out var entry)&&entry.Owner==owner&&entry.Kind==NativeItemKind.Module&&entry.View.Status==NativeApplicationStatus.Applied;
    internal static bool IsDeclaredModule(string owner,string id)
        =>Entries.TryGetValue(id,out var entry)&&entry.Owner==owner&&entry.Kind==NativeItemKind.Module;
    internal static bool TryGetRegisteredName(string id,bool english,out string? value)
    {
        value=null;if(!Entries.TryGetValue(id,out var entry))return false;
        value=entry.Definition.Options!.LocalizedName?.For(english)??entry.Definition.Options.Name??id;
        return true;
    }
    internal static bool TryGetRegisteredDescription(string id,bool english,out string? value)
    {
        value=null;if(!Entries.TryGetValue(id,out var entry))return false;
        value=entry.Definition.Options!.LocalizedShortDescription?.For(english)??entry.Definition.Options.ShortDescription;
        return true;
    }
    internal static string? Status(string id) => Entries.TryGetValue(id,out var e) ? e.View.Status switch {
        NativeApplicationStatus.Staged=>"Queued", NativeApplicationStatus.Applied=>"Applied", _=>e.View.Status+": "+e.View.Reason } : null;
    internal static IReadOnlyList<NativeApplicationView> Apply(ModItemDirectory directory)
    {
        var notifications=new List<NativeApplicationView>();
        foreach(var e in Entries.Values.ToArray()) {
            try {
                if(directory.Has(e.Definition.ItemId)||DirectoryMaster.Has<GameItem>(e.Definition.ItemId)) {
                    e.View=e.View with {Status=NativeApplicationStatus.Conflict,Reason="ID already exists in native directory"};
                } else {
                    if(e.NativeFactory is null) {
                        var generation=_generation;
                        e.NativeFactory=DelegateSupport.ConvertDelegate<Il2CppSystem.Func<GameItem>>((Func<GameItem>)(()=>Create(e,generation)))
                            ??throw new InvalidOperationException("factory delegate conversion returned null");
                    }
                    if(!directory.Add(e.Definition.ItemId,e.NativeFactory))throw new InvalidOperationException("Directory.Add returned false");
                    e.View=e.View with {Status=NativeApplicationStatus.Applied,Reason=null};
                    MelonLogger.Msg("[EZapi] Applied factory "+e.Definition.ItemId);
                }
            }catch(Exception ex){e.View=e.View with {Status=NativeApplicationStatus.Failed,Reason=ex.Message};}
            notifications.Add(e.View);
        }
        return notifications;
    }
    // 原生回调边界不能把作者异常抛进IL2CPP。返回null并保留诊断，Items.Create会明确报告失败。
    private static GameItem Create(Entry e,long generation)
    {
        if(generation!=_generation||!ItemRegistry.IsInitializationThread)return null!;
        GameItem? candidate=null;
        try {
            GameSession.RequireThread();
            if(e.Creating)throw new InvalidOperationException("EZAPI_FACTORY_REENTRANT: recursive factory call");
            e.Creating=true;
            try {
                var item=e.Definition.Factory();
                if(generation!=_generation)throw new InvalidOperationException("EZAPI_FACTORY_STOPPED: framework stopped during factory execution");
                if(item is null||item.Pointer==IntPtr.Zero||item.identifier!=e.Definition.ItemId||item.unitCount<=0||item.parentInventory is not null)
                    throw new InvalidOperationException("EZAPI_FACTORY_RESULT: return a fresh detached item with the registered ID");
                if(item.Pointer==e.LastPointer&&item.uniqueId==e.LastUnique)
                    throw new InvalidOperationException("EZAPI_FACTORY_REUSED: factory returned its previous instance");
                candidate=item;
                if(e.Kind==NativeItemKind.Node&&!item.IsGameItemType("NODE")||e.Kind==NativeItemKind.Module&&!item.IsGameItemType("MODULE"))
                    throw new InvalidOperationException("EZAPI_FACTORY_KIND: initialize NODE/MODULE with official helpers in the factory");
                var options=e.Definition.Options!;
                var locale=NativeItemLocalization.CurrentLocaleCode;
                var name=options.ResolveName(locale);
                var shortDescription=options.ResolveShortDescription(locale);
                var flavorText=options.ResolveFlavorText(locale);
                if(name is not null){item.identifierName=name;item.SetName(name);}
                if(shortDescription is not null)item.shortDescription=shortDescription;
                if(flavorText is not null)item.flavorText=flavorText;
                item.SyncModifiedState();
                e.LastInstance=item;e.LastPointer=item.Pointer;e.LastUnique=item.uniqueId;e.Created++;e.LastError=null;
                candidate=null;
                return item;
            }finally{e.Creating=false;}
        }catch(Exception ex){
            // 已确认当前ID/未附着/非上次实例后，包装阶段失败须释放新实例。
            // 不匹配ID、已附着、旧实例等结果不属于本次工厂的安全清理范围。
            if(candidate is not null)try{candidate.Destroy();}catch(Exception cleanup){MelonLogger.Warning("[EZapi] Factory cleanup: "+cleanup.Message);}
            e.Failures++;e.LastError=ex.Message;
            // 首次失败和之后每100次记录，避免坏工厂刷满日志；每次失败数仍可查询。
            if(e.Failures==1||e.Failures%100==0)MelonLogger.Warning("[EZapi] "+e.Definition.ItemId+": "+ex.Message);
            return null!;
        }
    }
    internal static void Clear()
    {
        _generation++;
        foreach(var e in Entries.Values){e.LastInstance=null;if(e.NativeFactory is not null)Retired.Add(e.NativeFactory);}
        Entries.Clear();
    }
    internal static IEnumerable<DebugItemRow> DebugRows()=>Entries.Values.OrderBy(e=>e.Definition.ItemId,StringComparer.Ordinal)
        .Select(e=>new DebugItemRow(e.Definition.ItemId,GetDisplayName(e.Definition.ItemId)??e.Definition.ItemId,Status(e.Definition.ItemId)!,null,
            $"DLL工厂 {e.Kind} · 创建 {e.Created} · 失败 {e.Failures}"));
}

/// <summary>只在已核对的当前游戏构建调用官方 locale getter；未知构建安全回退到英文。</summary>
internal static class NativeItemLocalization
{
    private static bool _warned;
    internal static bool IsEnglish=>!CurrentLocaleCode.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
    internal static string CurrentLocaleCode
    {
        get
        {
            var language=GameLanguage.Current;
            if(language.IsFallback&&!_warned){_warned=true;MelonLogger.Warning("[EZapi] EZAPI_ITEM_LOCALE_UNAVAILABLE: using English fallback ("+language.Reason+").");}
            return language.LocaleCode;
        }
    }
}

using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
using System.Text.Json;
namespace EZapi;

/// <summary>声明/目录状态边界；只保存托管定义和工厂委托，不操作玩家库存或 UI。</summary>
public static class ItemRegistry
{
    private static readonly Dictionary<string,ItemDefinition> Definitions=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,string> States=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,Il2CppSystem.Func<GameItem>> Factories=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,string> PackNames=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,string> PackVersions=new(StringComparer.Ordinal);
    private static int _thread;
    private static bool _started, _directorySeen;
    private static IntPtr _lastDirectory;

    internal static void Start()
    {
        if(_started)return;
        _thread=Environment.CurrentManagedThreadId;
        OfficialHookSubscriptions.Start();
        _started=true;
    }
    internal static void Stop()
    {
        OfficialHookSubscriptions.Stop();
        ItemActivation.Clear();
        Localization.ClearPacks();
        _started=false; _directorySeen=false; _lastDirectory=IntPtr.Zero;
        NativeFactoryRegistry.Clear();
        Definitions.Clear(); States.Clear(); Factories.Clear(); PackNames.Clear(); PackVersions.Clear();
    }

    /// <summary>初始化主线程提交；Accepted=已排队，GetStatus=Applied 才表示目录接受。</summary>
    public static ItemRegistrationResult Register(ItemDefinition definition)
        => RegisterBatch(new[]{definition})[0];

    /// <summary>整组预检后一次提交；字段、ID、分类名或 PNG 失败时整组均不入队。</summary>
    public static IReadOnlyList<ItemRegistrationResult> RegisterBatch(IReadOnlyList<ItemDefinition> definitions)
        => Submit(definitions,null,null,null);

    /// <summary>从绝对内容包目录读取 pack.json/items.json/Textures；供 DLL 作者复用同一入口。</summary>
    public static IReadOnlyList<ItemRegistrationResult> RegisterPack(string folder)
    {
        if(!Path.IsPathFullyQualified(folder))throw new ArgumentException("EZAPI_PACK_PATH: absolute pack directory required.");
        return ContentPackLoader.Load(folder);
    }

    internal static IReadOnlyList<ItemRegistrationResult> RegisterPack(
        IReadOnlyList<ItemDefinition> definitions,string id,string name,string folder,PreparedLoot? loot=null,string version="0.0.0")
        => Submit(definitions,id,name,folder,loot,version);

    internal static bool HasPack(string id,string minimum)=>PackVersions.TryGetValue(id,out var version)&&
        ContentPacks.ParseVersion(version)>=ContentPacks.ParseVersion(minimum);

    private static IReadOnlyList<ItemRegistrationResult> Submit(
        IReadOnlyList<ItemDefinition> definitions,string? packId,string? packName,string? folder,PreparedLoot? loot=null,string version="0.0.0")
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if(definitions.Count>500)throw new ArgumentException("EZAPI_JSON_LIMIT: max 500 items per submission.");
        foreach(var d in definitions)DefinitionRules.Validate(d);
        if(packId is null)foreach(var d in definitions)if(ItemText.Bindings(d).Any(b=>b.Text.Key is not null)) {
            var catalog=ItemText.Catalog(d)??throw new InvalidDataException("EZAPI_ITEM_LOCALIZATION: register the referenced content pack first: "+d.ModId);
            ItemText.ValidateCatalog(d,catalog);
        }
        // 捕获声明快照，调用方后续修改自己的 List 不会改变已排队/读档重建的内容。
        definitions=definitions.Select(d=>JsonSerializer.Deserialize<ItemDefinition>(JsonSerializer.Serialize(d))!).ToArray();
        string? rejection=DeclarationRegistrationError();
        var ids=new HashSet<string>(StringComparer.Ordinal);
        var names=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var existing in Definitions.Values)foreach(var type in existing.TypeNames)names[type.Id]=ItemText.TypeSignature(type,existing.ModId);
        foreach(var d in definitions) {
            if(!ids.Add(d.Id)||ContainsDeclaration(d.Id))rejection??="EZAPI_ITEM_DUPLICATE: "+d.Id;
            if(packId is not null&&(!d.Id.StartsWith(packId+".",StringComparison.Ordinal)||d.ModId!=packId))
                rejection??="EZAPI_OWNER: item ID/modId must belong to "+packId;
            foreach(var type in d.TypeNames) {
                if(names.TryGetValue(type.Id,out var prior)&&prior!=ItemText.TypeSignature(type,d.ModId))rejection??="EZAPI_ITEM_TYPE: conflicting display name for "+type.Id;
                names[type.Id]=ItemText.TypeSignature(type,d.ModId);
            }
        }
        if(packId is not null&&PackNames.ContainsKey(packId))rejection??="EZAPI_PACK_DUPLICATE: "+packId;
        if(rejection is not null) {
            if(definitions.Count==0)throw new InvalidOperationException(rejection);
            return definitions.Select(d=>new ItemRegistrationResult(d.Id,false,rejection)).ToArray();
        }
        // Prepare 仅检查路径/文件，不写共享资源表。全部成功后才提交。
        var assets=definitions.Select(d=>LegacySpriteAdapter.Prepare(d,folder)).ToArray();
        for(var i=0;i<definitions.Count;i++) {
            var d=definitions[i];
            LegacySpriteAdapter.Commit(d,assets[i]);
            Definitions.Add(d.Id,d); States.Add(d.Id,"Queued");
        }
        if(packId is not null) { PackNames.Add(packId,packName!); PackVersions.Add(packId,version); }
        LootTables.Commit(loot);
        // 一次性声明完成后才发Staged，订阅者不会观察到半批定义。
        ContentApi.PublishBatch(definitions.Select(d=>new NativeApplicationView(
            d.ModId??"ezapi.core",d.Id,"item",NativeApplicationStatus.Staged,null)));
        return definitions.Select(d=>new ItemRegistrationResult(d.Id,true,"Queued")).ToArray();
    }

    public static string GetStatus(string itemId)=>States.TryGetValue(itemId,out var state)?state:NativeFactoryRegistry.Status(itemId)??"Unknown";
    internal static bool ContainsDeclaration(string id)=>Definitions.ContainsKey(id)||NativeFactoryRegistry.Contains(id);
    internal static bool IsOwnedModule(string owner,string id)
    {
        if(NativeFactoryRegistry.IsOwnedModule(owner,id))return true;
        return Definitions.TryGetValue(id,out var definition)&&definition.Module is not null&&!definition.Node&&
            definition.Id.StartsWith(owner+".",StringComparison.Ordinal)&&(definition.ModId is null||definition.ModId==owner);
    }
    internal static bool IsDeclaredModule(string owner,string id)
    {
        if(NativeFactoryRegistry.IsDeclaredModule(owner,id))return true;
        return Definitions.TryGetValue(id,out var definition)&&definition.Module is not null&&!definition.Node&&
            definition.Id.StartsWith(owner+".",StringComparison.Ordinal)&&(definition.ModId is null||definition.ModId==owner);
    }
    internal static bool TryGetRegisteredName(string id,out string? value)
    { if(Definitions.TryGetValue(id,out var definition)){value=ItemText.Name(definition,NativeItemLocalization.CurrentLocaleCode);return true;}value=null;return false; }
    internal static bool TryGetRegisteredName(string id,bool english,out string? value)
    { if(Definitions.TryGetValue(id,out var definition)){value=ItemText.Name(definition,english?"en":"zh");return true;}value=null;return false; }
    internal static bool TryGetRegisteredDescription(string id,out string? value)
    {
        if(Definitions.TryGetValue(id,out var definition)){value=ItemText.Description(definition,NativeItemLocalization.CurrentLocaleCode);return true;}
        value=null;return false;
    }
    internal static bool TryGetRegisteredDescription(string id,bool english,out string? value)
    {
        if(Definitions.TryGetValue(id,out var definition)){value=ItemText.Description(definition,english?"en":"zh");return true;}
        value=null;return false;
    }
    internal static bool IsInitializationThread=>_started&&Environment.CurrentManagedThreadId==_thread;
    /// <summary>所有JSON/DLL物品的应用快照；只含托管记录，原生工厂失败另查FactorySnapshot。</summary>
    public static IReadOnlyList<NativeApplicationView> Snapshot()
    {
        GameSession.RequireThread();
        return Definitions.Values.Select(d=> {
            var state=GetStatus(d.Id);
            var status=state=="Applied"?NativeApplicationStatus.Applied:state=="Queued"?NativeApplicationStatus.Staged:
                state.StartsWith("Rejected",StringComparison.Ordinal)?NativeApplicationStatus.Conflict:NativeApplicationStatus.Failed;
            return new NativeApplicationView(d.ModId??"ezapi.core",d.Id,"item",status,status is NativeApplicationStatus.Staged or NativeApplicationStatus.Applied?null:state);
        }).Concat(NativeFactoryRegistry.Views()).OrderBy(v=>v.ContentId,StringComparer.Ordinal).ToArray();
    }
    internal static string? DeclarationRegistrationError()=>!_started||Environment.CurrentManagedThreadId!=_thread
        ? "EZAPI_NOT_READY: submit on initialization thread."
        : _directorySeen ? "EZAPI_TOO_LATE: restart and register during initialization." : null;
    internal static string? BehaviorRegistrationError(string id)
        => !_started||Environment.CurrentManagedThreadId!=_thread ? "EZAPI_NOT_READY: submit on initialization thread."
        : _directorySeen ? "EZAPI_TOO_LATE: register activation before directory initialization."
        : !Definitions.ContainsKey(id) ? "EZAPI_ACTIVATION_ITEM: register the item definition first."
        : Definitions[id].Activation is not null || Definitions[id].Food is not null || Definitions[id].LiquidContainer is not null || Definitions[id].Container is not null || Definitions[id].Module is not null || Definitions[id].Node
            ? "EZAPI_ACTIVATION_CONFLICT: native/JSON behavior already configured." : null;
    internal static void Apply(ModItemDirectory directory)
    {
        if(_lastDirectory==directory.Pointer)return;
        _lastDirectory=directory.Pointer;
        _directorySeen=true;
        var pending=Definitions.ToArray();
        var notifications=new List<NativeApplicationView>(pending.Length);
        foreach(var pair in pending) {
            try {
                if(directory.Has(pair.Key)) {
                    var reason="ID already exists in directory";States[pair.Key]="Rejected: "+reason;
                    notifications.Add(new(pair.Value.ModId??"ezapi.core",pair.Key,"item",NativeApplicationStatus.Conflict,reason));continue;
                }
                if(!Factories.TryGetValue(pair.Key,out var factory)) {
                    var definition=pair.Value;
                    factory=DelegateSupport.ConvertDelegate<Il2CppSystem.Func<GameItem>>((Func<GameItem>)(()=>NativeItemFactory.Create(definition)))
                        ??throw new InvalidOperationException("Factory conversion returned null.");
                    Factories.Add(pair.Key,factory);
                }
                if(!directory.Add(pair.Key,factory))throw new InvalidOperationException("Directory.Add returned false.");
                States[pair.Key]="Applied";
                notifications.Add(new(pair.Value.ModId??"ezapi.core",pair.Key,"item",NativeApplicationStatus.Applied,null));
                MelonLogger.Msg("[EZapi] Applied "+pair.Key);
            }catch(Exception e) { States[pair.Key]="Failed: "+e.Message; notifications.Add(new(pair.Value.ModId??"ezapi.core",pair.Key,"item",NativeApplicationStatus.Failed,e.Message)); MelonLogger.Error("[EZapi] EZAPI_ITEM_APPLY_FAILED "+pair.Key+": "+e); }
        }
        notifications.AddRange(NativeFactoryRegistry.Apply(directory));
        LootTables.OnDirectoryReady();
        ContentApi.PublishBatch(notifications);
        EZapiBootstrap.WriteDiagnostics();
    }
    internal static bool TryGet(string id,out ItemDefinition definition)
    { definition=null!; return !string.IsNullOrEmpty(id)&&GetStatus(id)=="Applied"&&(Definitions.TryGetValue(id,out definition!)||NativeFactoryRegistry.TryPresentation(id,out definition!)); }
    internal static bool TryGetDeclarative(string id,out ItemDefinition definition)
    { definition=null!;return !string.IsNullOrEmpty(id)&&GetStatus(id)=="Applied"&&Definitions.TryGetValue(id,out definition!); }
    internal static bool TryTypeName(string id,out string name)
    {
        foreach(var d in Definitions.Values)if(GetStatus(d.Id)=="Applied")
            foreach(var type in d.TypeNames)if(type.Id==id) { name=ItemText.Resolve(d,type.LocalizedName,type.Name,NativeItemLocalization.CurrentLocaleCode); return true; }
        name=""; return false;
    }
    internal static string SourceName(ItemDefinition d)
    {
        if(d.Tooltip.LocalizedSourceName is not null)return ItemText.Resolve(d,d.Tooltip.LocalizedSourceName,d.Tooltip.SourceName,NativeItemLocalization.CurrentLocaleCode);
        if(d.Tooltip.SourceName is not null)return d.Tooltip.SourceName;
        var owner=d.ModId;
        if(owner is null){var separator=d.Id.LastIndexOf('.');if(separator>0)owner=d.Id[..separator];}
        if(owner is not null) {
            var display=PresentationApi.ResolveOwnerDisplayName(owner,NativeItemLocalization.CurrentLocaleCode);
            if(display is not null)return display;
            if(PackNames.TryGetValue(owner,out var packName))return packName;
        }
        return owner??"EZapi";
    }
    internal static IReadOnlyList<ItemDefinition> AllDefinitions()=>Definitions.Values.ToArray();
    internal static object Report()=>States.ToDictionary(p=>p.Key,p=>p.Value);
    internal static IReadOnlyList<DebugItemRow> DebugRows()=>Definitions.Values.OrderBy(x=>x.Id,StringComparer.Ordinal)
        .Select(x=>new DebugItemRow(x.Id,ItemText.Name(x,NativeItemLocalization.CurrentLocaleCode),GetStatus(x.Id),LegacySpriteAdapter.SourcePath(x),DebugSummary(x)))
        .Concat(NativeFactoryRegistry.DebugRows()).ToArray();
    private static string DebugSummary(ItemDefinition x)
    {
        var en=NativeItemLocalization.IsEnglish;
        return (en?$"Types {x.Types.Count} · Tags {x.Tags.Count} · Single item · Tooltip {x.Tooltip.Lines.Count+x.Tooltip.Labels.Count}":
            $"类型 {x.Types.Count} · 标签 {x.Tags.Count} · 单件 · tooltip {x.Tooltip.Lines.Count+x.Tooltip.Labels.Count}")+
            (x.Food is {} food?(en?$" · Food {food.Calories} kcal":$" · 食品 {food.Calories} 热量"):
             x.LiquidContainer is {} liquid?(en?$" · Liquid container {liquid.Capacity} ml":$" · 液体容器 {liquid.Capacity} ml"):
             x.Container is {} box?(en?$" · Storage {box.Width}×{box.Height}":$" · 储物 {box.Width}×{box.Height}"):
             x.Module is {} module?(en?$" · Module {module.Type} · Tier {module.Tier}":$" · 模块 {module.Type} · 等级 {module.Tier}"):
             x.Node?(en?" · Native node":" · 原生节点"):"");
    }

}
internal sealed record DebugItemRow(string Id,string Name,string State,string? SpritePath,string Summary);

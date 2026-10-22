using Il2Cpp;
namespace EZapi;

/// <summary>
/// 一次声明独立机器、每实例布局、配方与图鉴。初始化、目录出现前注册机器；追加配方可在主线程空闲时调用。
/// 默认新建2×2单件物品；ConfigureItem配置新实例（含外观），随后框架建立Template布局。
/// CreateItem可提供自己的新游离实例，不能返回既有库存/缓存实例或预建contentWindow。
/// 默认接入BeforeNight，每晚1批；EnableNightly=false可禁用。不请求保存。Guide=null且配方Guide=null时不生成该配方图鉴。
/// </summary>
public sealed record MachineDefinition(string MachineId,MachineLayoutDefinition Template,IReadOnlyList<MachineRecipe> Recipes)
{
    public MachinePowerRule? Power {get;init;}
    public NativeItemOptions? ItemOptions {get;init;}
    public Action<GameItem>? ConfigureItem {get;init;}
    public Func<GameItem>? CreateItem {get;init;}
    public int ProductionMarkupPercent {get;init;}
    public RecipeGuideOptions? Guide {get;init;}=new();
    /// <summary>夜间默认1批；显式EnableNightly=false停用。每次成功后重新只读解析，上限64批，失败即停。</summary>
    public Func<GameItem,int>? ResolveNightlyBatchCount {get;init;}
    public bool EnableNightly {get;init;}=true;
}
/// <summary>纯托管状态。Applied仅证明目录应用；工厂、同步加工和夜间调度分别报告。</summary>
public sealed record MachineView(string OwnerId,string MachineId,MachineLayoutDefinition Template,int RecipeCount,
    NativeApplicationStatus Status,bool ProcessingAvailable,bool NightlyInstalled,long Created,long Failures,string? LastError)
{
    /// <summary>
    /// 本次Snapshot时机器运行时的全局就绪状态，与此定义是否启用夜间加工无关。
    /// 不证明目录Applied或材料/电量充足；手动构造的旧视图默认false，原有构造和解构签名保留。
    /// </summary>
    public bool RuntimeInstalled {get;init;}
}

/// <summary>统一机器注册。预检完成后提交三个托管目录，最后发布工厂通知；不执行作者工厂或动态解析器。</summary>
public static class MachineRegistrationApi
{
    private sealed record Entry(string Owner,MachineDefinition Definition,IReadOnlyList<RegisteredMachineRecipe> Recipes,IDisposable Admission,IDisposable Conditions);
    private static readonly Dictionary<string,Entry> Entries=new(StringComparer.Ordinal);
    private static bool _started,_editing;
    private static string? _terminalMarkupOwner;
    private static long _generation;
    internal static bool Contains(string machine)=>Entries.ContainsKey(machine);
    internal static bool TryNightly(string machine,out Func<GameItem,int>? resolve)
    {
        resolve=null;if(!Entries.TryGetValue(machine,out var e)||!e.Definition.EnableNightly)return false;
        resolve=e.Definition.ResolveNightlyBatchCount;return true;
    }
    internal static void Start(){_started=true;_generation++;}
    internal static void Stop(){_started=false;_generation++;foreach(var e in Entries.Values){e.Admission.Dispose();e.Conditions.Dispose();MachineRecipeApi.RemoveManaged(e.Definition.MachineId);RecipeGuideApi.RemoveManaged(e.Definition.MachineId);}Entries.Clear();_terminalMarkupOwner=null;}
    internal static object Report()=>new{ready=_started,editing=_editing,machines=Entries.Count,generation=_generation,nightlyInstalled=MachineRuntimeApi.RuntimeInstalled};

    public static ContentSubmissionResult RegisterMachine(string ownerId,MachineDefinition definition)
    {
        GameSession.RequireThread();
        if(!_started||_editing||!MachineRecipeApi.CanEdit)return new(false,"EZAPI_MACHINE_NOT_READY: stopped, editing or processing.");
        _editing=true;IDisposable? admission=null;IDisposable? conditions=null;
        try {
            MachineRecipeRules.Owner(ownerId);ArgumentNullException.ThrowIfNull(definition);
            var id=definition.MachineId;
            if(id is null||!id.StartsWith(ownerId+".",StringComparison.Ordinal)||id.Length<=ownerId.Length+1||
                !System.Text.RegularExpressions.Regex.IsMatch(id,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)+\z")||id.Length+20>160)
                return new(false,"EZAPI_MACHINE_ID: lowercase owner-prefixed ID with room for inventory suffix required.");
            var layout=JsonMachineLayout.Copy(definition.Template);
            if(layout.LiquidInputs.Any(s=>id.Length+".ezapi.machine.liquid.".Length+s.SlotId.Length>160))return new(false,"EZAPI_MACHINE_ID: liquid inventory ID exceeds160.");
            var recipes=MachineRecipeRules.Copy(ownerId,definition.Recipes).Select(r=>new RegisteredMachineRecipe(ownerId,r)).ToArray();
            var spec=definition with {Template=layout,Recipes=Array.AsReadOnly(recipes.Select(r=>r.Recipe).ToArray()),ItemOptions=definition.ItemOptions is {} o?o with {}:null};
            if(spec.ProductionMarkupPercent is <0 or >1000000||spec.Power?.Cost<0)throw Invalid("nonnegative power and markup0..1000000 required");
            CheckFit(spec,recipes);
            var guides=MachineAutomaticGuide.Build(spec,recipes);
            var rejection=ItemRegistry.DeclarationRegistrationError();
            if(rejection is not null)return new(false,rejection);
            if(Entries.Count>=128)return new(false,"EZAPI_MACHINE_LIMIT: max128 unified machines.");
            if(Entries.ContainsKey(id)||MachineRecipeApi.HasCatalog(id)||ItemRegistry.ContainsDeclaration(id))return new(false,"EZAPI_MACHINE_DUPLICATE: machine/catalog/item ID already claimed.");
            rejection=MachineRecipeApi.CheckManaged(id,recipes.Length)??RecipeGuideApi.CheckManaged(id,guides);
            if(rejection is not null)return new(false,rejection);
            var generation=_generation;
            // 先取得可释放准入句柄；后续工厂声明失败时释放，不留下半个机器目录。
            admission=InventoryAdmissionApi.Register("ezapi.machine-registration",id,id+".ezapi.machine.input",(item,_)=>Allows(id,generation,item));
            conditions=MachineContainerConditionApi.Register(ownerId,id,layout);
            var staged=NativeFactoryRegistry.Submit(ownerId,NativeItemKind.Item,new[]{new NativeModuleRegistration(id,()=>Create(spec,generation),spec.ItemOptions)},publish:false);
            if(!staged.Accepted)return staged;
            Entries.Add(id,new(ownerId,spec,Array.AsReadOnly(recipes),admission,conditions));admission=null;conditions=null;
            MachineRecipeApi.CommitManaged(ownerId,id,recipes,spec.Power,spec.ProductionMarkupPercent);
            RecipeGuideApi.CommitManaged(id,guides);
            // 全部可观察目录已就绪，再开放重入和发布。订阅者查询/追加时看到完整一致的状态。
            _editing=false;
            ContentApi.PublishBatch(new[]{new NativeApplicationView(ownerId,id,"item",NativeApplicationStatus.Staged,null)});
            return new(true,null);
        }catch(ArgumentException e){return new(false,e.Message);}
        catch(InvalidOperationException e){return new(false,e.Message);}
        finally{conditions?.Dispose();admission?.Dispose();_editing=false;}
    }

    /// <summary>按调用顺序追加，整批接受或拒绝；ID归追加作者。禁止替换/抢占，最多64条/机器。</summary>
    public static ContentSubmissionResult RegisterAdditionalRecipes(string ownerId,string machineId,IReadOnlyList<MachineRecipe> recipes)
    {
        GameSession.RequireThread();
        if(!_started||_editing||!MachineRecipeApi.CanEdit)return new(false,"EZAPI_MACHINE_NOT_READY: stopped, editing or processing.");
        _editing=true;
        try {
            var copy=MachineRecipeRules.Copy(ownerId,recipes);MachineRecipeRules.Id(machineId);
            if(!Entries.TryGetValue(machineId,out var e))return new(false,"EZAPI_MACHINE_MISSING: target must use RegisterMachine.");
            if(copy.Length==0)return new(false,"EZAPI_MACHINE_ADDITIONAL: nonempty batch required; append does not remove recipes.");
            var merged=e.Recipes.Concat(copy.Select(r=>new RegisteredMachineRecipe(ownerId,r))).ToArray();
            if(merged.Length>64||merged.Select(r=>r.Recipe.RecipeId).Distinct(StringComparer.Ordinal).Count()!=merged.Length)
                return new(false,"EZAPI_MACHINE_ADDITIONAL: duplicate recipe ID or max64 recipes exceeded.");
            CheckFit(e.Definition,merged);
            var guides=MachineAutomaticGuide.Build(e.Definition,merged);
            var rejection=MachineRecipeApi.CheckManaged(machineId,merged.Length)??RecipeGuideApi.CheckManaged(machineId,guides);
            if(rejection is not null)return new(false,rejection);
            Entries[machineId]=e with {Recipes=Array.AsReadOnly(merged)};
            MachineRecipeApi.CommitManaged(e.Owner,machineId,merged,e.Definition.Power,e.Definition.ProductionMarkupPercent);
            RecipeGuideApi.CommitManaged(machineId,guides);
            return new(true,null);
        }catch(ArgumentException e){return new(false,e.Message);}
        finally{_editing=false;}
    }
    /// <summary>
    /// 共享终端加价0..1000%。第一个有效配置作者获得配置权（包括提交默认值）；同作者可更新。
    /// 仅主线程空闲时修改，不修改任何存档物品；下一次ProcessBatch读取新目录，独立处理器仍保留创建时快照。
    /// </summary>
    public static ContentSubmissionResult ConfigureManufacturingTerminalMarkup(string ownerId,int markupPercent)
    {
        GameSession.RequireThread();
        if(!_started||_editing||!MachineRecipeApi.CanEdit)return new(false,"EZAPI_MACHINE_NOT_READY: stopped, editing or processing.");
        _editing=true;
        try {
            MachineRecipeRules.Owner(ownerId);
            if(markupPercent is <0 or >1000)return new(false,"EZAPI_TERMINAL_MARKUP: percent must be0..1000.");
            if(!Entries.TryGetValue(ManufacturingTerminal.ItemId,out var e)||e.Owner!=ManufacturingTerminal.OwnerId)
                return new(false,"EZAPI_TERMINAL_MISSING: shared terminal not registered.");
            if(_terminalMarkupOwner is not null&&_terminalMarkupOwner!=ownerId)
                return new(false,"EZAPI_TERMINAL_OWNER: shared markup already configured by "+_terminalMarkupOwner);
            if(e.Definition.ProductionMarkupPercent!=markupPercent) {
                var definition=e.Definition with {ProductionMarkupPercent=markupPercent};
                // 更新可执行目录和统一定义，保留每条追加配方的实际作者、次序和准入/图鉴。
                MachineRecipeApi.CommitManaged(e.Owner,definition.MachineId,e.Recipes,definition.Power,markupPercent);
                Entries[definition.MachineId]=e with {Definition=definition};
            }
            _terminalMarkupOwner=ownerId;
            return new(true,null);
        }catch(ArgumentException e){return new(false,e.Message);}
        finally{_editing=false;}
    }
    internal static ManufacturingTerminalView TerminalSnapshot()
    {
        GameSession.RequireThread();var found=Entries.TryGetValue(ManufacturingTerminal.ItemId,out var e)&&e.Owner==ManufacturingTerminal.OwnerId;
        return new(found,_terminalMarkupOwner,found?e!.Definition.ProductionMarkupPercent:ManufacturingTerminal.DefaultProductionMarkupPercent,found?e!.Recipes.Count:0);
    }
    public static IReadOnlyList<RegisteredMachineRecipe> RegisteredRecipes(string machineId)
    {
        GameSession.RequireThread();MachineRecipeRules.Id(machineId);
        // 再复制集合，作者不能通过快照篡改目录；委托本身是声明的一部分。
        return Entries.TryGetValue(machineId,out var e)?Array.AsReadOnly(e.Recipes.Select(r=>new RegisteredMachineRecipe(r.OwnerId,MachineRecipeRules.Copy(r.OwnerId,new[]{r.Recipe})[0])).ToArray()):Array.Empty<RegisteredMachineRecipe>();
    }
    public static IReadOnlyList<MachineRecipe> Recipes(string machineId)=>Array.AsReadOnly(RegisteredRecipes(machineId).Select(r=>r.Recipe).ToArray());
    public static RecipeGuideSnapshot GuideSnapshot()
    {
        GameSession.RequireThread();var ids=Entries.Keys.ToHashSet(StringComparer.Ordinal);var all=RecipeGuideApi.Snapshot();
        return all with {Recipes=Array.AsReadOnly(all.Recipes.Where(r=>ids.Contains(r.MachineId)&&Entries[r.MachineId].Recipes.Any(x=>x.OwnerId==r.OwnerId&&x.Recipe.RecipeId==r.RecipeId)).ToArray())};
    }
    public static IReadOnlyList<MachineView> Snapshot()
    {
        GameSession.RequireThread();var factories=ItemApi.FactorySnapshot().ToDictionary(v=>v.ItemId,StringComparer.Ordinal);
        return Array.AsReadOnly(Entries.Values.OrderBy(e=>e.Definition.MachineId,StringComparer.Ordinal).Select(e=>{
            var f=factories[e.Definition.MachineId];return new MachineView(e.Owner,e.Definition.MachineId,JsonMachineLayout.Copy(e.Definition.Template),e.Recipes.Count,f.Status,
                f.Status==NativeApplicationStatus.Applied&&MachineRecipeApi.CanEdit&&InventoryAdmissionApi.IsAvailable&&InventoryFreezeApi.IsAvailable,
                MachineRuntimeApi.RuntimeInstalled&&e.Definition.EnableNightly,f.Created,f.Failures,f.LastError){RuntimeInstalled=MachineRuntimeApi.RuntimeInstalled};
        }).ToArray());
    }
    private static bool Allows(string id,long generation,GameItem item)=>!_started||generation!=_generation||!Entries.TryGetValue(id,out var e)||
        e.Recipes.Any(x=>x.Recipe.AuxiliaryItemIds.Contains(item.identifier,StringComparer.Ordinal)||x.Recipe.ItemInputs.Any(i=>
            (i.ItemId is null||i.ItemId==item.identifier)&&(i.ItemTag is null||item.IsTag(i.ItemTag))&&(i.Condition?.Invoke(item)??true)));
    private static GameItem Create(MachineDefinition spec,long generation)
    {
        if(!_started||generation!=_generation)throw new InvalidOperationException("EZAPI_MACHINE_STALE: factory belongs to stopped generation.");
        var item=spec.CreateItem?.Invoke()??(spec.CreateItem is null?ItemDirectory.CreateEmptyItem(spec.MachineId):null);
        if(item is null||item.Pointer==IntPtr.Zero||item.identifier!=spec.MachineId||item.parentInventory is not null||item.unitCount!=1||item.contentWindow is not null)
            throw new InvalidOperationException("EZAPI_MACHINE_FACTORY: fresh detached single matching item without window required.");
        try {
            if(spec.CreateItem is null){item.SetShape(new GridShapeBuilder(2,2).SetDataFill(1).Build());item.mayStackItemFunc=NativeDelegateBridge.Stack();}
            if(spec.ItemOptions?.Name is {} name){item.identifierName=name;item.SetName(name);}
            spec.ConfigureItem?.Invoke(item);
            if(!_started||generation!=_generation)throw new InvalidOperationException("EZAPI_MACHINE_STALE: stopped during configure.");
            if(item.identifier!=spec.MachineId||item.unitCount!=1||item.parentInventory is not null||item.contentWindow is not null)
                throw new InvalidOperationException("EZAPI_MACHINE_CONFIGURE: configure must preserve the fresh detached single item ID and leave layout creation to EZapi.");
            MachineLayoutApi.Create(item,spec.Template);return item;
        }catch {
            // 只清理仍属于此工厂的游离对象，避免作者错误回调附着后误删玩家库存。
            if(item.parentInventory is null)item.Destroy();throw;
        }
    }
    private static void CheckFit(MachineDefinition definition,IReadOnlyList<RegisteredMachineRecipe> recipes)
    {
        if(recipes.Any(r=>(r.Recipe.Output is MachineContainerOutput)!=(definition.Template.Output.Kind=="container")))
            throw Invalid("recipe item/container output must match template output kind");
        if((definition.Power?.Cost>0||definition.Power?.ResolveCost is not null)&&definition.Template.Battery is null)throw Invalid("configured power requires battery slot");
        var slots=definition.Template.LiquidInputs.Select(s=>s.SlotId).ToHashSet(StringComparer.Ordinal);
        if(recipes.Any(r=>r.Recipe.LiquidInputs.Any(i=>!slots.Contains(i.SlotId))))throw Invalid("liquid ingredient references absent template slot");
    }
    private static ArgumentException Invalid(string reason)=>new("EZAPI_MACHINE_DEFINITION: "+reason);
}

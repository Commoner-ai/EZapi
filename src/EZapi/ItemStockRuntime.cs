using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
namespace EZapi;

/// <summary>官方发明家补货结束后的显式上架扩展。只在夜间补货调用域执行，不改全局物品/库存接纳规则。</summary>
internal static class ItemStockRuntime
{
    private static readonly NativeHookGroup Hooks=new("ezapi.items.night-stock");
    private static Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<GameItem>>? _late;
    private static bool _started,_busy;
    private static long _generation;
    private sealed record Scope(long Token,long Generation,bool Night)
    {
        internal readonly HashSet<string> Attempted=new(StringComparer.Ordinal);
    }
    private static long _token;
    private static readonly Stack<Scope> Contexts=new();
    private static readonly Dictionary<string,ItemStockView> Views=new(StringComparer.Ordinal);
    internal static bool IsAvailable
    {
        get {
            if(!_started || !GameSession.OnThread || _late is null || !Hooks.Snapshot().Ready)return false;
            var handlers=ModHook.OnPlaceInventorInventoryItemLate;
            if(handlers is null)return false;
            foreach(var handler in handlers.GetInvocationList())if(handler.Pointer==_late.Pointer)return true;
            return false;
        }
    }
    internal static void Start()=>_started=true;
    internal static void DirectoryReady()
    {
        if(!_started)return;
        if(_late is null)try {
            _late=DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppSystem.Collections.Generic.List<GameItem>>>(
                (Action<Il2CppSystem.Collections.Generic.List<GameItem>>)OnLate) ?? throw new InvalidOperationException("delegate conversion failed");
            ModHook.add_OnPlaceInventorInventoryItemLate(_late);
        } catch(Exception ex) {
            if(_late is not null)try{ModHook.remove_OnPlaceInventorInventoryItemLate(_late);}catch(Exception cleanup){MelonLogger.Error("[EZapi] Stock hook cleanup: "+cleanup.Message);}
            _late=null;MelonLogger.Error("[EZapi] EZAPI_STOCK_HOOK: "+ex.Message);return;
        }
        Hooks.Install(typeof(ItemStockRuntime),new NativeHookSpec(typeof(StoreClientList),nameof(StoreClientList.PlaceInventorInventory),typeof(void),new[]{typeof(bool)},Prefix:nameof(Before),Finalizer:nameof(After)));
    }
    private static void Before(bool __0,out long __state)
    {
        __state=0;
        if(!_started || !GameSession.OnThread || !Hooks.Ready)return;
        __state=++_token;Contexts.Push(new(__state,_generation,!__0));
    }
    private static Exception? After(Exception? __exception,long __state)
    {
        if(__state!=0 && Contexts.Count>0 && Contexts.Peek().Token==__state)Contexts.Pop();
        return __exception;
    }
    private static void OnLate(Il2CppSystem.Collections.Generic.List<GameItem> ownedSnapshot)
    {
        try {
            if(!IsAvailable || _busy || Contexts.Count==0 || !Contexts.Peek().Night || Contexts.Peek().Generation!=_generation)return;
            Supply(ownedSnapshot);
        }
        catch(Exception ex){MelonLogger.Error("[EZapi] EZAPI_STOCK_APPLY: "+ex);}
    }
    /// <summary>显式调试调用域：发出真实Late事件，执行真实原生上架；不生成其它原版货物。</summary>
    internal static void ProbeEvent(Il2CppSystem.Collections.Generic.List<GameItem> ownedSnapshot,bool visiting)
    {
        Before(visiting,out var state);
        try{ModHook.FireOnPlaceInventorInventoryItemLate(ownedSnapshot);}
        finally{After(null,state);}
    }
    // 官方Early/Late参数均是玩家已拥有物品的快照，不是待放置队列，绝不能往该列表追加货物。
    // 参考官方LearnerMod Late示例显式调用AddDirectSellingItemToTable，并读回真实柜台数量。
    internal static void Supply(Il2CppSystem.Collections.Generic.List<GameItem> ownedSnapshot)
    {
        var run=NativeAccess.RequireRun();
        if(_busy)throw new InvalidOperationException("EZAPI_STOCK_REENTRY: nested stock operation.");
        if(ownedSnapshot is null || ownedSnapshot.Count>16384)throw new ArgumentException("EZAPI_STOCK_LIMIT: bounded owned snapshot required.");
        if(Contexts.Count==0 || !Contexts.Peek().Night || Contexts.Peek().Generation!=_generation || !IsAvailable)
            throw new InvalidOperationException("EZAPI_STOCK_SCOPE: current night-stock scope required.");
        var scope=Contexts.Peek();
        var table=EmporiumEntry.Instance?.frontInvinvElement ?? throw new InvalidOperationException("EZAPI_STOCK_TABLE: native sale table unavailable.");
        var tablePointer=table.Pointer;
        bool Current()=>_started && scope.Generation==_generation && NativeAccess.SameRun(run) &&
            Contexts.Count>0 && ReferenceEquals(Contexts.Peek(),scope) && EmporiumEntry.Instance?.frontInvinvElement?.Pointer==tablePointer;
        long Count(string id) {
            var items=table.childItems;
            if(items is null || items.Count>4096)throw new InvalidOperationException("EZAPI_STOCK_LIMIT: bounded native sale table required.");
            long count=0;
            foreach(var item in items)if(item is not null && item.identifier==id && !GeneralHelper.IsItemOwned(item))count=checked(count+item.unitCount);
            return count;
        }
        _busy=true;
        try {
            var player=GameSession.Resolve(run);
            var candidates=NativeFactoryRegistry.StockDeclarations().Concat(ItemRegistry.AllDefinitions()
                .Where(d=>d.NightShop!="none").Select(d=>(d.Id,d.NightShop=="unique"?NightShopStockPolicy.Unique:NightShopStockPolicy.Repeatable,(Func<bool>?)null))).ToArray();
            foreach(var (id,policy,available) in candidates) {
                if(!Current())break;
                if(!scope.Attempted.Add(id))continue; // 同一原生调用内的重复Late事件不可重复创建，失败也不重跑作者工厂。
                var view=Views.GetValueOrDefault(id)??new ItemStockView(id,policy,0,0,null);
                GameItem? made=null;var handedOff=false;
                try {
                    var owned=player.FindAllItem(true);
                    if(owned is null || owned.Count>16384)throw new InvalidOperationException("EZAPI_STOCK_LIMIT: bounded current player items required.");
                    var hasOwned=false;
                    foreach(var item in owned)if(item is not null && item.unitCount>0 && item.identifier==id && GeneralHelper.IsItemOwned(item)){hasOwned=true;break;}
                    var before=Count(id);
                    if(ItemRegistry.GetStatus(id)!="Applied" || !ItemStockRules.Eligible(policy,hasOwned,before>0,available)) {
                        if(Current())Views[id]=view with {Skipped=view.Skipped+1,LastError=null};
                        continue;
                    }
                    if(!Current())throw new InvalidOperationException("EZAPI_STOCK_STALE: author predicate changed run, table or stopped framework.");
                    if(table.childItems.Count>=4096)throw new InvalidOperationException("EZAPI_STOCK_LIMIT: native sale table full.");
                    made=Items.Create(id,false);
                    if(!Current())throw new InvalidOperationException("EZAPI_STOCK_STALE: author factory changed run, table or stopped framework.");
                    // 原生方法负责所有权、perk检查和接纳；拒绝时会销毁游离实例。
                    // 交接后绝不再解引用made，也不重复Destroy；数量读回可兼容原生堆叠合并。
                    var amount=made.unitCount;handedOff=true;
                    player.AddDirectSellingItemToTable(made);
                    made=null;
                    if(!Current())throw new InvalidOperationException("EZAPI_STOCK_STALE: native placement changed run or sale table.");
                    if(Count(id)-before!=amount)throw new InvalidOperationException("EZAPI_STOCK_PLACEMENT: native sale table did not retain the complete supply; native method owns rejected items.");
                    Views[id]=view with {Added=view.Added+1,LastError=null};
                } catch(Exception ex) {
                    if(made is not null && !handedOff && made.parentInventory is null)made.Destroy();
                    if(_started && scope.Generation==_generation)Views[id]=view with {LastError=ex.GetType().Name+": "+ex.Message};
                    MelonLogger.Warning("[EZapi] EZAPI_STOCK_ITEM "+id+": "+ex.Message);
                    if(!Current())break;
                }
            }
        } finally {_busy=false;}
    }
    internal static IReadOnlyList<ItemStockView> Snapshot(){GameSession.RequireThread();return Views.Values.OrderBy(v=>v.ItemId,StringComparer.Ordinal).ToArray();}
    internal static object Report()=>new {available=IsAvailable,hooks=Hooks.Snapshot(),contextDepth=Contexts.Count,busy=_busy,items=Views.Values.ToArray()};
    internal static void Stop()
    {
        _started=false;_generation++;Hooks.Stop();Views.Clear();
        if(_late is null)return;
        try{ModHook.remove_OnPlaceInventorInventoryItemLate(_late);_late=null;}
        catch(Exception ex){MelonLogger.Error("[EZapi] EZAPI_STOCK_UNSUBSCRIBE: "+ex.Message);}
    }
}

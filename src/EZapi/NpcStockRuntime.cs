using System.Reflection;
using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>只在已核对NPC货物生成调用域改用候选池；拾荒和普通LootRegistry调用原样执行。</summary>
internal static class NpcStockRuntime
{
    private sealed record Scope(long Token,long Generation,GameRunIdentity Run,NpcTradeStockSupplier Supplier,bool Night,bool Miner)
    {
        internal long NamedRolls;
        // 仅当前同步生成范围，防止命名表结果在交付时再抽一次；不跨场景保留原生实例。
        internal readonly HashSet<(IntPtr Pointer,int Id)> TableItems=new();
        internal readonly List<GameItem> Retired=new();
        internal readonly HashSet<(IntPtr Pointer,int Id)> Existing=new();
    }
    private sealed record SpawnState(Scope Scope,long Before);
    private sealed record SaleState(Scope Scope,GameItem Original,GameItem? Replacement);
    private static readonly NativeHookGroup Hooks=new("ezapi.items.npc-stock");
    private static readonly Stack<Scope> Scopes=new();
    private static bool _started,_routing;
    private static long _token,_routed,_generation;
    private static string? _error;
    private static readonly HashSet<string> Observed=new(StringComparer.Ordinal);
    private static List<GameItem>? _minerCapture;
    private static List<object>? _saleDetails;
    internal static bool IsAvailable=>_started && GameSession.OnThread && GameBuildFingerprint.IsKnown && Hooks.Snapshot().Ready;
    internal static void Start(){_generation++;_started=true;}
    private static bool Current(Scope scope)=>scope.Generation==_generation&&NativeAccess.SameRun(scope.Run);
    internal static void DirectoryReady()
    {
        if(!_started || !GameBuildFingerprint.IsKnown)return;
        // 生成回调名仅对已核对SHA256构建启用；升级不按相似名字猜测入口。
        var callbacks=typeof(StoreClientList).GetNestedType("__c",BindingFlags.Public|BindingFlags.NonPublic);
        if(callbacks is null){_error="EZAPI_NPC_STOCK_HOOK: exact StoreClientList callback type missing";return;}
        Hooks.Install(typeof(NpcStockRuntime),
            new(typeof(StoreClientList),nameof(StoreClientList.PlaceInventorInventory),typeof(void),new[]{typeof(bool)},Prefix:nameof(Inventor),Finalizer:nameof(Exit)),
            new(callbacks,"_CreateThief_b__47_0",typeof(void),Type.EmptyTypes,Prefix:nameof(Thief),Finalizer:nameof(Exit)),
            new(callbacks,"_CreatePettyThief_b__48_0",typeof(void),Type.EmptyTypes,Prefix:nameof(Thief),Finalizer:nameof(Exit)),
            new(callbacks,"_CreateFoodThief_b__49_0",typeof(void),Type.EmptyTypes,Prefix:nameof(Thief),Finalizer:nameof(Exit)),
            new(callbacks,"_CreateMiner_b__38_0",typeof(void),Type.EmptyTypes,Prefix:nameof(Miner),Finalizer:nameof(Exit)),
            new(typeof(LootRegistry),nameof(LootRegistry.RollInternal),typeof(string),new[]{typeof(string),typeof(string)},Prefix:nameof(Roll)),
            new(typeof(DirectoryMaster),nameof(DirectoryMaster.Item),typeof(GameItem),new[]{typeof(string),typeof(bool)},Prefix:nameof(MinerItem)),
            new(typeof(ItemSpawner),nameof(ItemSpawner.Spawn),typeof(GameItem),new[]{typeof(string)},Prefix:nameof(BeforeSpawn),Postfix:nameof(AfterSpawn)),
            new(typeof(ItemSpawner),nameof(ItemSpawner.SpawnFromTable),typeof(GameItem),new[]{typeof(string)},Prefix:nameof(BeforeSpawn),Postfix:nameof(AfterSpawn)),
            new(typeof(ItemSpawner),nameof(ItemSpawner.SpawnFromTableGroup),typeof(GameItem),new[]{typeof(string)},Prefix:nameof(BeforeSpawn),Postfix:nameof(AfterSpawn)),
            new(typeof(PlayerStore),nameof(PlayerStore.AddDirectSellingItemToTable),typeof(void),new[]{typeof(GameItem),typeof(bool),typeof(bool),typeof(bool),typeof(int)},Prefix:nameof(PrepareSale),Finalizer:nameof(FinishSale)));
    }
    private static long Enter(NpcTradeStockSupplier supplier,bool night,bool miner)
    {
        if(!IsAvailable || GameSession.Identity is not {} run)return 0;
        var existing=GameSession.Resolve(run).FindAllItem(true);
        if(existing is null||existing.Count>16384)return 0;
        var token=++_token;var scope=new Scope(token,_generation,run,supplier,night,miner);
        foreach(var item in existing)if(item is not null)scope.Existing.Add((item.Pointer,item.uniqueId));
        Scopes.Push(scope);return token;
    }
    private static void Inventor(bool __0,out long __state)=>__state=Enter(NpcTradeStockSupplier.Inventor,!__0,false);
    private static void Thief(out long __state)=>__state=Enter(NpcTradeStockSupplier.Thief,false,false);
    private static void Miner(out long __state)=>__state=Enter(NpcTradeStockSupplier.Any,false,true);
    private static Exception? Exit(Exception? __exception,long __state)
    {
        if(__state!=0 && Scopes.Count>0 && Scopes.Peek().Token==__state) {
            var scope=Scopes.Pop();
            // 延迟到整个生成方法及官方Late事件结束；方法内的临时列表不接触已销毁包装对象。
            foreach(var item in scope.Retired)try{if(NativeAccess.SameRun(scope.Run)&&item.parentInventory is null&&!scope.Existing.Contains((item.Pointer,item.uniqueId)))item.Destroy();}
                catch(Exception ex){_error="EZAPI_NPC_STOCK_CLEANUP: "+ex.Message;MelonLogger.Warning("[EZapi] "+_error);}
            scope.Retired.Clear();scope.TableItems.Clear();
        }
        return __exception;
    }
    private static bool MinerItem(string __0,bool __1,ref GameItem __result)
    {
        if(!IsAvailable||_routing||Scopes.Count==0||!Scopes.Peek().Miner||!Current(Scopes.Peek()))return true;
        _routing=true;
        try {
            if(!NpcStockApi.HasCandidates(NpcTradeStockCategory.Ore,miner:true))return true;
            if(!NpcStockApi.TryRollMiner(__0,out var id)||id==__0)return true;
            // 原生矿工先决定槽数，再每槽创建固定矿石。把原矿石视为权重1，保留原生槽数/到访规则。
            // 在原工厂之前选择，未选中的原物品不会创建，也无需销毁或搬动已有库存。
            __result=Items.Create(id!,__1);_routed++;return false;
        }catch(Exception ex){_error=ex.GetType().Name+": "+ex.Message;MelonLogger.Warning("[EZapi] EZAPI_NPC_STOCK_MINER: "+_error);return true;}
        finally{_routing=false;}
    }
    private static bool CaptureMinerItem(GameItem __0)
    {
        if(_minerCapture is null||!DebugNpcStock.Running||Scopes.Count==0||!Scopes.Peek().Miner)return true;
        if(_minerCapture.Count>=64)throw new InvalidOperationException("EZAPI_NPC_STOCK_PROBE: miner item limit.");
        _minerCapture.Add(__0);return false;
    }
    private static void BeforeSpawn(out SpawnState? __state)
    {
        __state=IsAvailable&&!_routing&&Scopes.Count>0&&Current(Scopes.Peek())?new(Scopes.Peek(),Scopes.Peek().NamedRolls):null;
    }
    private static void AfterSpawn(GameItem __result,SpawnState? __state)
    {
        if(__state is null||!IsAvailable||Scopes.Count==0||!ReferenceEquals(Scopes.Peek(),__state.Scope)||
            !Current(__state.Scope)||__state.Before==__state.Scope.NamedRolls||__result is null)return;
        if(__state.Scope.TableItems.Count>=4096)throw new InvalidOperationException("EZAPI_NPC_STOCK_LIMIT: too many generated table items.");
        __state.Scope.TableItems.Add((__result.Pointer,__result.uniqueId));
    }
    // 分类来自当前实例的官方类型查询。优先机器/模块/矿石，避免多类型物品变成其它用途的货物。
    internal static NpcTradeStockCategory Classify(GameItem item)
    {
        foreach(var (type,category) in new[]{("MACHINE",NpcTradeStockCategory.Machine),("MODULE",NpcTradeStockCategory.Module),
            ("ORE",NpcTradeStockCategory.Ore),("FOOD",NpcTradeStockCategory.Food),("MEDICAL",NpcTradeStockCategory.Medical),
            ("MATERIAL",NpcTradeStockCategory.Material),("HOUSEHOLD_GOOD",NpcTradeStockCategory.Household),("HOUSEHOLD",NpcTradeStockCategory.Household)})
            if(item.IsGameItemType(type))return category;
        return NpcTradeStockCategory.None;
    }
    private static bool PrepareSale(ref GameItem __0,out SaleState? __state)
    {
        __state=null;
        if(!IsAvailable||_routing||Scopes.Count==0||!Current(Scopes.Peek()))return true;
        var scope=Scopes.Peek();var original=__0;var routed=false;var tableItem=false;
        if(original is null)return true;
        if(!scope.Miner&&original.parentInventory is null&&!scope.Existing.Contains((original.Pointer,original.uniqueId))) {
            tableItem=scope.TableItems.Remove((original.Pointer,original.uniqueId));
            var category=Classify(original);
            // 作者已直接供应的物品不再作为原版槽位替换；命名表也已有完整候选池。
            if(!tableItem&&category!=NpcTradeStockCategory.None&&!NativeFactoryRegistry.Contains(original.identifier)&&
                !ItemRegistry.AllDefinitions().Any(d=>d.Id==original.identifier)&&NpcStockApi.HasCandidates(category)) {
                if(scope.Retired.Count>=4096){_error="EZAPI_NPC_STOCK_LIMIT: fixed-slot cleanup bound reached";return true;}
                GameItem? replacement=null;_routing=true;
                try {
                    if(NpcStockApi.TryRollFixed(original.identifier,category,scope.Supplier,scope.Night,false,out var id)) {
                        if(id!=original.identifier) {
                            replacement=Items.Create(id!,GeneralHelper.IsItemOwned(original));
                            if(!IsAvailable||Scopes.Count==0||!ReferenceEquals(Scopes.Peek(),scope)||!Current(scope))
                                throw new InvalidOperationException("EZAPI_NPC_STOCK_STALE: factory changed stock scope/run.");
                            __state=new(scope,original,replacement);__0=replacement;replacement=null;routed=true;_routed++;
                        }
                    } else {
                        // 官方抽取事件清空结果代表取消此槽位；最终清理原游离实例。
                        __state=new(scope,original,null);return false;
                    }
                }catch(Exception ex) {
                    if(replacement is not null&&replacement.parentInventory is null)replacement.Destroy();
                    _error=ex.GetType().Name+": "+ex.Message;
                    MelonLogger.Warning("[EZapi] EZAPI_NPC_STOCK_FIXED: "+_error);
                    // 抽取/创建失败保留已经存在的原货物，不重抽、不重跑原工厂。
                }finally{_routing=false;}
            }
        }
        if(_minerCapture is null||!DebugNpcStock.Running)return true;
        if(_minerCapture.Count>=512)throw new InvalidOperationException("EZAPI_NPC_STOCK_PROBE: stock item limit.");
        var types=new List<string>();foreach(var type in original.itemTypes)types.Add(type);
        _saleDetails?.Add(new{original=original.identifier,category=Classify(original).ToString(),selected=__0.identifier,routed,tableItem,owned=GeneralHelper.IsItemOwned(original),types});
        _minerCapture.Add(__0);return false;
    }
    private static Exception? FinishSale(Exception? __exception,SaleState? __state)
    {
        if(__state is not null) {
            __state.Scope.Retired.Add(__state.Original);
            if(__exception is not null&&__state.Replacement is {} item&&item.parentInventory is null&&
                (_minerCapture is null||!_minerCapture.Any(i=>i.Pointer==item.Pointer)))__state.Scope.Retired.Add(item);
        }
        return __exception;
    }
    internal static IReadOnlyList<object> ProbeFixedStock(string kind)
    {
        NativeAccess.RequireRun();
        if(!DebugNpcStock.Running||_minerCapture is not null)throw new InvalidOperationException("EZAPI_NPC_STOCK_PROBE: isolated debug scope required.");
        var items=new List<GameItem>();var details=new List<object>();_minerCapture=items;_saleDetails=details;
        try {
            switch(kind) {
                case "inventor-night":StoreClientList.PlaceInventorInventory(false);break;
                case "inventor-visit":StoreClientList.PlaceInventorInventory(true);break;
                case "thief":StoreClientList.__c.__9._CreateThief_b__47_0();break;
                case "petty-thief":StoreClientList.__c.__9._CreatePettyThief_b__48_0();break;
                case "food-thief":StoreClientList.__c.__9._CreateFoodThief_b__49_0();break;
                default:throw new ArgumentException("known native stock kind required");
            }
            return details.ToArray();
        }finally{
            _minerCapture=null;_saleDetails=null;
            foreach(var item in items)if(item is not null&&item.Pointer!=IntPtr.Zero&&item.parentInventory is null)item.Destroy();
        }
    }
    internal static IReadOnlyList<string> ProbeMinerStock()
    {
        NativeAccess.RequireRun();
        if(!DebugNpcStock.Running||_minerCapture is not null)throw new InvalidOperationException("EZAPI_NPC_STOCK_PROBE: isolated debug scope required.");
        var captured=new List<GameItem>();_minerCapture=captured;
        try {
            // 本构建把生成动作放在对话委托中，OnArrival本身为空。直接执行被精确钩住的原生委托。
            StoreClientList.__c.__9._CreateMiner_b__38_0();
            return captured.Select(i=>i.identifier).ToArray();
        }finally{
            _minerCapture=null;
            foreach(var item in captured)if(item is not null&&item.Pointer!=IntPtr.Zero&&item.parentInventory is null)item.Destroy();
        }
    }
    private static bool Roll(string __0,string __1,ref string __result)
    {
        if(!IsAvailable || _routing || Scopes.Count==0)return true;
        var scope=Scopes.Peek();
        if(!Current(scope))return true;
        scope.NamedRolls++;
        if(Observed.Count<64)Observed.Add(__0);
        var category=Category(__0);
        if(category==NpcTradeStockCategory.None)return true;
        _routing=true;
        try {
            // 没有相关Applied声明时不替换原生抽取，也不多推进一次RNG。
            if(!NpcStockApi.HasCandidates(category))return true;
            NpcStockApi.TryRoll(__0,category,scope.Supplier,out var id,scope.Night,scope.Miner,__1);
            __result=id??"";_routed++;return false;
        } catch(Exception ex) {
            _error=ex.GetType().Name+": "+ex.Message;
            MelonLogger.Warning("[EZapi] EZAPI_NPC_STOCK_ROLL: "+_error);
            // 可能已经消耗RNG或发布事件，不能重跑原生抽取伪装无事发生。
            __result="";return false;
        } finally {_routing=false;}
    }
    internal static NpcTradeStockCategory Category(string id)=>id switch {
        "materialTable"=>NpcTradeStockCategory.Material,
        "allModuleTable" or "t1moduleTable" or "t2moduleTable"=>NpcTradeStockCategory.Module,
        "householdTable"=>NpcTradeStockCategory.Household,
        "packedFoodTable"=>NpcTradeStockCategory.Food,
        "medicalTable"=>NpcTradeStockCategory.Medical,
        _=>NpcTradeStockCategory.None
    };
    internal static string Probe(string table,NpcTradeStockSupplier supplier,bool night=false,bool miner=false,string? groupId=null)
    {
        var state=Enter(supplier,night,miner);
        try{return groupId is null?LootRegistry.Roll(table):LootRegistry.RollInternal(table,groupId);}
        finally{Exit(null,state);}
    }
    internal static object Report()=>new{available=IsAvailable,hooks=Hooks.Snapshot(),scopeDepth=Scopes.Count,routing=_routing,routed=_routed,observedTables=Observed.ToArray(),lastError=_error,
        boundary="named tables, miner Ore and unowned detached typed fixed inventor/thief sale slots; actual shelf/payment persistence requires separate evidence"};
    internal static void Stop(){_started=false;_generation++;Hooks.Stop();Observed.Clear();_routing=false;_error=null;_routed=0;}
}

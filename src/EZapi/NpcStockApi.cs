using Il2Cpp;
namespace EZapi;

/// <summary>作者商人可直接读取原生表并抽取EZapi货源；自动原版NPC路由另由运行时适配，不修改全局journal。</summary>
public static class NpcStockApi
{
    private static bool _rolling;
    internal static IEnumerable<(string Owner,string Id,NpcTradeStockOptions Options)> Declarations()
        =>NativeFactoryRegistry.NpcStockDeclarations().Concat(ItemRegistry.AllDefinitions().Where(d=>d.NpcTrade is not null).Select(d=>(d.ModId??Owner(d.Id),d.Id,d.NpcTrade!)))
            .Where(d=>(!d.Item2.StartsWith("ezapi.debug.",StringComparison.Ordinal)||DebugNpcStock.Running) && ItemRegistry.GetStatus(d.Item2)=="Applied");
    internal static bool HasCandidates(NpcTradeStockCategory category,bool miner=false)
        =>Declarations().Any(d=>(d.Options.Category & category)!=0&&(!miner||d.Options.MinerWeight>0));
    /// <summary>当前周目同步调用。读取真实天数/拥有量及Applied目录；只返回托管候选池，不创建物品。</summary>
    public static NpcStockPool BuildPool(string tableId,NpcTradeStockCategory category,NpcTradeStockSupplier supplier,
        bool nightShop=false,bool miner=false)
    {
        NativeAccess.RequireRun();
        var source=LootTables.GetSnapshot(tableId);
        if(!source.Found)throw new ArgumentException("EZAPI_NPC_STOCK_TABLE: missing native loot table: "+tableId);
        return BuildPool(source.Entries.Select(e=>new NpcStockEntry(e.Id,e.Weight,e.SourceModId)).ToArray(),category,supplier,nightShop,miner);
    }
    /// <summary>为没有命名表的货源传入独立基础权重；输入不会加入全局掉落表。读取当前真实天数/持有状态。</summary>
    public static NpcStockPool BuildPool(IReadOnlyList<NpcStockEntry> baseline,NpcTradeStockCategory category,NpcTradeStockSupplier supplier,
        bool nightShop=false,bool miner=false)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var run=NativeAccess.RequireRun();
        var player=GameSession.Resolve(run);var items=player.FindAllItem(true);
        if(items is null||items.Count>16384)throw new InvalidOperationException("EZAPI_NPC_STOCK_LIMIT: current player inventory unavailable or too large.");
        var owned=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in items)if(item is not null && item.Pointer!=IntPtr.Zero && item.unitCount>0 && GeneralHelper.IsItemOwned(item))owned.Add(item.identifier);
        var candidates=Declarations().Select(d=>new NpcStockCandidate(d.Owner,d.Id,d.Options,owned.Contains(d.Id))).ToArray();
        if(!NativeAccess.SameRun(run))throw new InvalidOperationException("EZAPI_NPC_STOCK_STALE: current run changed.");
        return NpcStockPlanner.Build(new(category,supplier,StoreStation.GetDayCounter(),nightShop,miner),
            baseline,candidates);
    }
    /// <summary>使用游戏RNG抽取ID并触发一次官方OnLootRolled；不生成或交付物品。空池返回false。</summary>
    public static bool TryRoll(string tableId,NpcTradeStockCategory category,NpcTradeStockSupplier supplier,out string? itemId,
        bool nightShop=false,bool miner=false)
        =>TryRoll(tableId,category,supplier,out itemId,nightShop,miner,"");
    internal static bool TryRoll(string tableId,NpcTradeStockCategory category,NpcTradeStockSupplier supplier,out string? itemId,
        bool nightShop,bool miner,string groupId)
    {
        NativeAccess.RequireRun();itemId=null;
        if(_rolling)throw new InvalidOperationException("EZAPI_NPC_STOCK_REENTRY: no nested roll from loot event.");
        _rolling=true;
        try {
            return RollPool(BuildPool(tableId,category,supplier,nightShop,miner),tableId,groupId,out itemId);
        } finally {_rolling=false;}
    }
    internal static bool TryRollMiner(string baselineId,out string? itemId)
        =>TryRollFixed(baselineId,NpcTradeStockCategory.Ore,NpcTradeStockSupplier.Any,false,true,out itemId);
    /// <summary>原生已决定槽位后，以原物品权重1与分类声明抽取；无合格替代项时不推进RNG。</summary>
    internal static bool TryRollFixed(string baselineId,NpcTradeStockCategory category,NpcTradeStockSupplier supplier,
        bool night,bool miner,out string? itemId)
    {
        NativeAccess.RequireRun();itemId=null;
        if(_rolling)throw new InvalidOperationException("EZAPI_NPC_STOCK_REENTRY: no nested roll from loot event.");
        _rolling=true;
        try{
            var pool=BuildPool(new[]{new NpcStockEntry(baselineId,1)},category,supplier,night,miner);
            // 无合格自定义候选时不推进RNG，也不额外发布原版从未发送的抽取事件。
            if(!pool.Entries.Any(e=>e.ItemId!=baselineId&&e.Weight>0)){itemId=baselineId;return true;}
            return RollPool(pool,miner?"ezapi.npc.miner":supplier==NpcTradeStockSupplier.Inventor?"ezapi.npc.inventor.fixed":"ezapi.npc.thief.fixed","",out itemId);
        }
        finally{_rolling=false;}
    }
    private static bool RollPool(NpcStockPool pool,string tableId,string groupId,out string? itemId)
    {
            itemId=null;var run=GameSession.Identity!;
            if(pool.TotalWeight<=0)return false;
            var native=new Il2CppSystem.Collections.Generic.List<LootEntry>();
            foreach(var row in pool.Entries)if(row.Weight>0)native.Add(new LootEntry(row.ItemId,row.Weight,row.SourceModId??""));
            var result=LootRegistry.Pick(native);
            if(string.IsNullOrEmpty(result))return false;
            var context=new LootRollContext{tableId=tableId,groupId=groupId,itemId=result};
            ModHook.FireOnLootRolled(context);
            if(!NativeAccess.SameRun(run))throw new InvalidOperationException("EZAPI_NPC_STOCK_STALE: loot callback changed current run.");
            if(string.IsNullOrEmpty(context.itemId))return false;
            itemId=context.itemId;return true;
    }
    private static string Owner(string id)=>id[..id.LastIndexOf('.')];
}

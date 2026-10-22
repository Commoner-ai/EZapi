using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;
namespace EZapi;

/// <summary>
/// 初始化时提交声明，物品目录和官方掉落表加载事件都就绪后才向原生 journal 提交一次。
/// 原生 journal 负责场景重建；EZapi 不在每次读档重复添加，不缓存原生物品或列表。
/// </summary>
public static class LootTables
{
    private static readonly List<PreparedLoot> Batches=new();
    private static Il2CppSystem.Action? _loaded;
    private static bool _ready,_directoryReady,_tablesReady,_applying;
    public static LootRegistrationResult Register(string modId,LootDefinition definition)
    {
        var batch=Prepare(modId,new[]{definition});
        Commit(batch!);return new(true,"Queued",batch!.Operations.Count);
    }
    internal static PreparedLoot? Prepare(string owner,IReadOnlyList<LootDefinition> definitions)
    {
        var error=ItemRegistry.DeclarationRegistrationError();
        if(!_ready||error is not null)throw new InvalidOperationException(error??"EZAPI_LOOT_NOT_READY: wait for EZapi initialization.");
        RunData.ValidateModId(owner);
        if(definitions.Count==0)return null;
        if(definitions.Count>16)throw new ArgumentException("EZAPI_LOOT_LIMIT: max 16 loot files per owner.");
        if(Batches.Count>=256)throw new InvalidOperationException("EZAPI_LOOT_LIMIT: max 256 owners.");
        if(Batches.Any(b=>b.Owner==owner))throw new ArgumentException("EZAPI_LOOT_DUPLICATE: owner already registered: "+owner);
        var operations=new List<LootOperation>();
        foreach(var definition in definitions) {
            JsonLoot.Validate(definition);
            foreach(var table in definition.Tables)operations.Add(Register(false,table));
            foreach(var group in definition.Groups)operations.Add(Register(true,group));
            foreach(var patch in definition.Patches) {
                var group=patch.Group is not null;var id=patch.Table??patch.Group!;
                if(patch.Clear)operations.Add(new("clear",group,id,null,0,null));
                foreach(var row in patch.Add)operations.Add(new("add",group,id,row.Id,row.Weight,null));
                foreach(var idToRemove in patch.Remove)operations.Add(new("remove",group,id,idToRemove,0,null));
                foreach(var row in patch.SetWeight)operations.Add(new("setWeight",group,id,row.Id,row.Weight,null));
            }
        }
        if(operations.Count>4096)throw new ArgumentException("EZAPI_LOOT_LIMIT: max 4096 operations per owner.");
        var ids=Batches.SelectMany(b=>b.Operations).Where(o=>o.Kind=="register").Select(o=>(o.IsGroup,o.ListId)).ToHashSet();
        foreach(var op in operations.Where(o=>o.Kind=="register")) {
            if(!op.ListId.StartsWith(owner+".",StringComparison.Ordinal))
                throw new ArgumentException("EZAPI_LOOT_OWNER: new table/group IDs must start with "+owner+".");
            if(!ids.Add((op.IsGroup,op.ListId)))throw new ArgumentException("EZAPI_LOOT_DUPLICATE: "+op.ListId);
        }
        return new(owner,operations.AsReadOnly());
    }
    private static LootOperation Register(bool group,LootListDefinition list)=>new("register",group,list.Id,null,0,
        list.Entries.Select(e=>new LootWeightDefinition{Id=e.Id,Weight=e.Weight}).ToArray());
    internal static void Commit(PreparedLoot? batch) { if(batch is not null)Batches.Add(batch); }
    internal static void Start()
    {
        if(_ready)return;
        if(_loaded is not null)throw new InvalidOperationException("EZAPI_LOOT_HOOK: old native subscription still retained.");
        try {
            _loaded=DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(()=> {
                if(!_ready)return;
                _tablesReady=true;TryApply();
            }))??throw new InvalidOperationException("EZAPI_LOOT_HOOK: callback conversion returned null.");
            ModHook.add_OnLootTablesLoaded(_loaded);_ready=true;
        } catch { Stop();throw; }
    }
    internal static void Stop()
    {
        _ready=false;_tablesReady=false;_directoryReady=false;Batches.Clear();
        if(_loaded is null)return;
        try { ModHook.remove_OnLootTablesLoaded(_loaded);_loaded=null; }
        catch(Exception e) { MelonLogger.Error("[EZapi] EZAPI_LOOT_UNSUBSCRIBE_FAILED: "+e); }
        // 不清空原生 journal 或撤销其它 Mod 的结果；Mod 启停/顺序变更需要重启游戏。
    }
    internal static void SceneChanged()=>_tablesReady=false;
    internal static void OnDirectoryReady() { _directoryReady=true;TryApply(); }
    private static void TryApply()
    {
        if(!_ready||!_directoryReady||!_tablesReady||_applying)return;
        _applying=true;
        try {
            foreach(var batch in Batches.Where(b=>b.State=="Queued")) {
                try {
                    NativeLootAdapter.Refresh();
                    PreflightNative(batch);
                    foreach(var operation in batch.Operations) {
                        batch.AttemptedOperations++;
                        NativeLootAdapter.Execute(batch.Owner,operation);
                        batch.CompletedOperations++;
                    }
                    NativeLootAdapter.Refresh();batch.State="Submitted";
                    MelonLogger.Msg("[EZapi] Loot submitted "+batch.Owner+": "+batch.CompletedOperations+" journal operation(s).");
                } catch(Exception e) {
                    batch.State=batch.AttemptedOperations==0?"Rejected":"FailedPartial";
                    batch.Error=e.Message;
                    MelonLogger.Error("[EZapi] EZAPI_LOOT_APPLY_FAILED "+batch.Owner+" "+batch.State+": "+e+
                        "; no automatic retry or native journal rollback.");
                }
            }
            EZapiBootstrap.WriteDiagnostics();
        } catch(Exception e) { MelonLogger.Error("[EZapi] EZAPI_LOOT_HOOK_FAILED: "+e); }
        finally { _applying=false; }
    }
    private static void PreflightNative(PreparedLoot batch)
    {
        var created=new HashSet<(bool Group,string Id)>();
        foreach(var op in batch.Operations) {
            var key=(op.IsGroup,op.ListId);
            if(op.Kind=="register") {
                if(created.Contains(key)||NativeLootAdapter.Has(op.ListId,op.IsGroup))
                    throw new InvalidOperationException("EZAPI_LOOT_COLLISION: "+op.ListId);
                created.Add(key);
            } else if(!created.Contains(key)&&!NativeLootAdapter.Has(op.ListId,op.IsGroup))
                throw new InvalidOperationException("EZAPI_LOOT_TARGET: missing table/group: "+op.ListId);
            var entries=op.Entries??(op.Kind=="add"||op.Kind=="setWeight"?
                new[]{new LootWeightDefinition{Id=op.EntryId!,Weight=op.Weight}}:Array.Empty<LootWeightDefinition>());
            foreach(var entry in entries) {
                if(op.IsGroup) {
                    if(!created.Contains((false,entry.Id))&&!NativeLootAdapter.Has(entry.Id,false))
                        throw new InvalidOperationException("EZAPI_LOOT_REFERENCE: group entry refers to missing table: "+entry.Id);
                } else {
                    var state=ItemRegistry.GetStatus(entry.Id);
                    if((entry.Id.StartsWith(batch.Owner+".",StringComparison.Ordinal)||state!="Unknown")&&state!="Applied")
                        throw new InvalidOperationException("EZAPI_LOOT_REFERENCE: EZapi item is not Applied: "+entry.Id);
                }
            }
        }
    }
    public static LootListSnapshot GetSnapshot(string listId,bool isGroup=false)
    {
        GameSession.RequireThread();JsonLoot.ValidateId(listId);
        if(!_ready||!_tablesReady)throw new InvalidOperationException("EZAPI_LOOT_NOT_READY: wait for OnLootTablesLoaded in this scene.");
        return NativeLootAdapter.Snapshot(listId,isGroup);
    }
    /// <summary>抽取返回物品 ID，不创建物品或放入库存。会推进游戏 RNG 并触发官方抽取事件。</summary>
    public static bool TryRoll(string listId,bool isGroup,out string? itemId)
    {
        itemId=null;var snapshot=GetSnapshot(listId,isGroup);
        if(!snapshot.Found||snapshot.TotalWeight<=0)return false;
        var result=NativeLootAdapter.Roll(listId,isGroup);
        if(string.IsNullOrEmpty(result))return false;
        itemId=result;return true;
    }
    internal static object Report()=>new {ready=_ready,directoryReady=_directoryReady,tablesReady=_tablesReady,
        batches=Batches.Select(b=>new{modId=b.Owner,state=b.State,operations=b.Operations.Count,
            attempted=b.AttemptedOperations,completed=b.CompletedOperations,error=b.Error}).ToArray()};
}
internal sealed record LootOperation(string Kind,bool IsGroup,string ListId,string? EntryId,float Weight,IReadOnlyList<LootWeightDefinition>? Entries);
internal sealed class PreparedLoot
{
    internal PreparedLoot(string owner,IReadOnlyList<LootOperation> operations)=>(Owner,Operations)=(owner,operations);
    internal string Owner { get; }
    internal IReadOnlyList<LootOperation> Operations { get; }
    internal string State="Queued";
    internal string? Error;
    internal int AttemptedOperations,CompletedOperations;
}

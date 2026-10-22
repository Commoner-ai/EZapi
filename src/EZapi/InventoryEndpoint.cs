using System.Text;
using System.Text.Json;
using Il2Cpp;
namespace EZapi;

/// <summary>当前载入的隐藏库存根。IsCurrent为false时禁止使用Root，重新Open获得新句柄。</summary>
public sealed record InventoryEndpoint
{
    private GameItem _root=null!;
    private GameRunIdentity? _identity;
    public string OwnerId {get; init;}
    public string EndpointId {get; init;}
    public GameItem Root {get=>_identity is null||InventoryEndpointApi.IsCurrent(this,_identity,_root)?_root:throw new InvalidOperationException("EZAPI_ENDPOINT_STALE: reopen in the current loaded run.");init=>_root=value;}
    public bool ChargingOnly {get; init;}
    public bool IsCurrent=>_identity is not null&&InventoryEndpointApi.IsCurrent(this,_identity,_root);
    public InventoryEndpoint(string OwnerId,string EndpointId,GameItem Root,bool ChargingOnly=false)
        =>(this.OwnerId,this.EndpointId,this.Root,this.ChargingOnly)=(OwnerId,EndpointId,Root,ChargingOnly);
    internal InventoryEndpoint(string owner,string id,GameItem root,bool charging,GameRunIdentity identity)
        :this(owner,id,root,charging)=>_identity=identity;
    public void Deconstruct(out string OwnerId,out string EndpointId,out GameItem Root,out bool ChargingOnly)
        =>(OwnerId,EndpointId,Root,ChargingOnly)=(this.OwnerId,this.EndpointId,this.Root,this.ChargingOnly);
    internal GameItem Item=>_root;
    public bool Equals(InventoryEndpoint? other)=>other is not null&&StringComparer.Ordinal.Equals(OwnerId,other.OwnerId)&&
        StringComparer.Ordinal.Equals(EndpointId,other.EndpointId)&&EqualityComparer<GameItem>.Default.Equals(_root,other._root)&&ChargingOnly==other.ChargingOnly;
    public override int GetHashCode()=>HashCode.Combine(OwnerId,EndpointId,_root,ChargingOnly);
}

/// <summary>
/// 基于原生saveBags的隔离库存；modData保存完整原生图。不会调用SaveGame或自动重试恢复。
/// Root只在一次载入有效。删除前须取回全部内容；已打开窗口/拖拽时不销毁。
/// </summary>
public static class InventoryEndpointApi
{
    private const string Prefix="EZapi.Endpoint/v1/",StateKey="EZapi.Endpoints/v1/state";
    private const int Limit=InventoryEndpointCodec.MaxEndpoints;
    private static readonly Dictionary<string,InventoryEndpoint> Entries=new(StringComparer.Ordinal);
    private static GameRunIdentity? _identity;
    private static bool _started,_busy,_transferBusy,_restored,_fault;
    private static readonly List<GameItem> Retained=new();
    /// <summary>最近一次原生保存被安全门控阻止的错误；成功保存后清空。</summary>
    public static string? SaveError {get;private set;}
    public static string? Error {get;private set;}
    public static bool IsAvailable=>_started&&GameSession.OnThread&&InventoryEndpointRuntime.Ready&&InventoryRuntime.CatalogReady&&SaveGraphApi.CanDecode&&!_fault;
    /// <summary>Explains a missing restore prerequisite before endpoint creation can write native or persistent state.</summary>
    public static string? UnavailableReason=>!SaveGraphApi.CanDecode?SaveGraphApi.DecodeUnavailableReason:Error;
    internal static void Start()=>_started=true;
    private static string Key(string owner,string id)=>Prefix+owner+"/"+id;
    private static void Validate(string owner,string id)
    {
        if(!PresentationRules.Owned(owner,id))throw new ArgumentException("EZAPI_ENDPOINT_ID: owner and namespaced endpoint ID required.");
    }
    private static PlayerStore Store(PlayerStore? explicitStore=null)
    {
        GameSession.RequireThread();
        if(!IsAvailable||!GameSession.TryResolve(null,out var store))throw new InvalidOperationException(UnavailableReason??"EZAPI_ENDPOINT_NOT_READY");
        if(explicitStore is not null&&(explicitStore.Pointer==IntPtr.Zero||explicitStore.Pointer!=store.Pointer))
            throw new ArgumentException("EZAPI_ENDPOINT_STORE: exact current PlayerStore required.");
        return store;
    }
    internal static bool IsCurrent(InventoryEndpoint endpoint,GameRunIdentity identity,GameItem root)
    {
        if(!IsAvailable||_identity!=identity||GameSession.Identity!=identity||!GameSession.TryResolve(identity.Epoch,out var store))return false;
        var key=Key(endpoint.OwnerId,endpoint.EndpointId);
        return Entries.TryGetValue(key,out var current)&&ReferenceEquals(current,endpoint)&&
            root.Pointer!=IntPtr.Zero&&root.unitCount>0&&root.parentInventory is null&&store.saveBags is not null&&
            store.saveBags.ContainsKey(key)&&store.saveBags[key]?.Pointer==root.Pointer;
    }
    /// <summary>默认只打开已有记录；createIfMissing=true才调用物品工厂。chargingOnly表示库存参与原生充电但不向家具供电；不限制物品准入。</summary>
    public static InventoryEndpoint? Open(PlayerStore store,string ownerId,string endpointId,string itemId,bool createIfMissing=false,bool chargingOnly=false)
    {
        Validate(ownerId,endpointId);
        if(!PresentationRules.Owned(ownerId,itemId))throw new ArgumentException("EZAPI_ENDPOINT_ITEM: item ID must belong to owner namespace.");
        var player=Store(store);EnsureRestored(player);
        if(_busy||_transferBusy)throw new InvalidOperationException("EZAPI_ENDPOINT_REENTRANT");
        var key=Key(ownerId,endpointId);
        if(Entries.TryGetValue(key,out var entry)) {
            if(!entry.IsCurrent)throw new InvalidOperationException("EZAPI_ENDPOINT_IDENTITY");
            if(entry.Item.identifier!=itemId||entry.ChargingOnly!=chargingOnly)throw new InvalidOperationException("EZAPI_ENDPOINT_CONFLICT");
            _=Grid(entry.Item);
            return entry;
        }
        if(player.saveBags?.ContainsKey(key)==true)throw new InvalidOperationException("EZAPI_ENDPOINT_CONFLICT: native key occupied.");
        if(!createIfMissing)return null;
        if(Entries.Count>=Limit)throw new InvalidOperationException("EZAPI_ENDPOINT_LIMIT");
        _busy=true;GameItem? made=null;InventoryEndpoint? createdEntry=null;var scope=GameSession.Identity!;
        try {
            made=ItemApi.Create(itemId,true);
            if(!NativeAccess.SameRun(scope)||made.unitCount<=0||made.parentInventory is not null||!GeneralHelper.IsItemOwned(made))
                throw new InvalidOperationException("EZAPI_ENDPOINT_ROOT: detached player-owned root required.");
            _=Grid(made);
            if(InventoryApi.CaptureRunItems(player).Any(x=>x.Pointer==made.Pointer||x.uniqueId==made.uniqueId))
                throw new InvalidOperationException("EZAPI_ENDPOINT_ALIAS");
            if(player.saveBags is null)player.saveBags=new();
            if(player.saveBags.Count>=4096||player.saveBags.ContainsKey(key))throw new InvalidOperationException("EZAPI_ENDPOINT_CONFLICT");
            createdEntry=new(ownerId,endpointId,made,chargingOnly,scope);
            player.saveBags.Add(key,made);Entries.Add(key,createdEntry);
            entry=createdEntry;
            if(!entry.IsCurrent)throw new InvalidOperationException("EZAPI_ENDPOINT_READBACK");
            return entry;
        }catch(Exception error) {
            // 先撤回本次发表的精确绑定；只有确认没有其它原生引用时才销毁刚创建的图。
            var cleaned=made is null||made.Pointer==IntPtr.Zero;string? cleanupError=null;
            if(!cleaned&&NativeAccess.SameRun(scope))try {
                var created=made!;
                if(Entries.TryGetValue(key,out var current)&&ReferenceEquals(current,createdEntry))Entries.Remove(key);
                var bags=player.saveBags;
                if(bags is not null&&bags.ContainsKey(key)&&bags[key]?.Pointer==created.Pointer)bags.Remove(key);
                var referenced=Entries.Values.Any(x=>x.Item.Pointer==created.Pointer);
                if(player.saveBags is { } liveBags)foreach(var pair in liveBags)
                    if(pair.Value?.Pointer==created.Pointer){referenced=true;break;}
                if(!referenced&&created.parentInventory is null&&!InventoryApi.CaptureRunItems(player).Any(x=>x.Pointer==created.Pointer)) {
                    created.Destroy();cleaned=true;
                }
            }catch(Exception cleanup){cleanupError=cleanup.Message;}
            if(!cleaned) {
                if(made is not null&&made.Pointer!=IntPtr.Zero&&!Retained.Any(x=>x.Pointer==made.Pointer))Retained.Add(made);
                _fault=true;Error="EZAPI_ENDPOINT_OPEN_INDETERMINATE: "+(cleanupError??error.Message);
            }
            throw;
        }finally{_busy=false;}
    }
    public static IReadOnlyList<InventoryEndpoint> Snapshot()
    {
        var store=Store();EnsureRestored(store);
        if(_busy||_transferBusy)throw new InvalidOperationException("EZAPI_ENDPOINT_REENTRANT");
        if(Entries.Values.Any(e=>!e.IsCurrent))throw new InvalidOperationException("EZAPI_ENDPOINT_IDENTITY");
        return Array.AsReadOnly(Entries.Values.OrderBy(e=>e.OwnerId,StringComparer.Ordinal).ThenBy(e=>e.EndpointId,StringComparer.Ordinal).ToArray());
    }

    /// <summary>读取端点容器的直接内容。返回与当前周目绑定的只读快照，不递归展开箱内物品。</summary>
    public static NativeInventorySnapshot CaptureContents(InventoryEndpoint endpoint,bool includeGeometry=false)
    {
        if(_busy||_transferBusy)throw new InvalidOperationException("EZAPI_ENDPOINT_REENTRANT");
        var grid=Grid(endpoint);var scope=_identity!;
        var snapshot=InventoryApi.CaptureDirect(grid,includeGeometry);
        if(!NativeAccess.SameRun(scope)||!endpoint.IsCurrent)
            throw new InvalidOperationException("EZAPI_ENDPOINT_STALE: endpoint changed while its contents were read.");
        return snapshot;
    }

    /// <summary>通过一次原生接纳把物品存入端点；数量、归属及不确定结果遵循 ItemTransferApi.Move 契约。</summary>
    public static ItemTransferResult Store(InventoryEndpoint endpoint,GameItem item,int maximum)
        =>Transfer(endpoint,item,null,maximum,false);

    /// <summary>从端点取出直接子物品；来源必须是该端点网格，结果遵循 ItemTransferApi.Move 契约。</summary>
    public static ItemTransferResult Take(InventoryEndpoint endpoint,GameItem item,GameGridInventory destination,int maximum)
        =>Transfer(endpoint,item,destination,maximum,true);

    private static ItemTransferResult Transfer(InventoryEndpoint endpoint,GameItem item,GameGridInventory? destination,int maximum,bool taking)
    {
        GameSession.RequireThread();
        if(_busy||_transferBusy)return new(0,false,"Reentrant",Error:"EZAPI_ENDPOINT_REENTRANT");
        if(maximum<=0)throw new ArgumentOutOfRangeException(nameof(maximum),"EZAPI_TRANSFER_AMOUNT: positive maximum required.");
        NativeAccess.RequireItem(item);
        var grid=Grid(endpoint);var scope=_identity!;
        if(taking&&item.parentInventory?.Pointer!=grid.Pointer)
            return new(0,false,"Rejected",Error:"EZAPI_ENDPOINT_SOURCE: item must be a direct child of this endpoint.");
        if(taking&&(destination is null||destination.Pointer==IntPtr.Zero))
            return new(0,false,"Rejected",Error:"EZAPI_ENDPOINT_TARGET: a live destination grid is required.");
        _transferBusy=true;
        try {
            var result=ItemTransferApi.Move(item,destination??grid,maximum);
            if(!NativeAccess.SameRun(scope)||!endpoint.IsCurrent||grid.Pointer==IntPtr.Zero)
                return new(result.Moved,true,"Indeterminate",result.NativeReported,"EZAPI_ENDPOINT_STALE: endpoint identity changed during native transfer.");
            return result;
        } finally {_transferBusy=false;}
    }

    private static GameGridInventory Grid(InventoryEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if(!endpoint.IsCurrent)throw new InvalidOperationException("EZAPI_ENDPOINT_STALE: reopen in the current loaded run.");
        var grid=Grid(endpoint.Item);
        if(!endpoint.IsCurrent)throw new InvalidOperationException("EZAPI_ENDPOINT_STALE: endpoint changed while resolving its grid.");
        return grid;
    }
    private static GameGridInventory Grid(GameItem root)
    {
        var grid=root?.contentWindow?.childElement?.TryCast<GameGridInventory>();
        if(grid is null||grid.Pointer==IntPtr.Zero)
            throw new InvalidOperationException("EZAPI_ENDPOINT_CONTAINER: endpoint root must expose a live native grid inventory.");
        return grid;
    }
    /// <summary>仅删除自己的空端点。非空、拖拽、开放窗口、原生键被替换均拒绝并保留资源。</summary>
    public static bool Delete(string ownerId,string endpointId)
    {
        Validate(ownerId,endpointId);var store=Store();EnsureRestored(store);
        if(_busy||_transferBusy)throw new InvalidOperationException("EZAPI_ENDPOINT_REENTRANT");
        var key=Key(ownerId,endpointId);if(!Entries.TryGetValue(key,out var entry))return false;
        if(!entry.IsCurrent||InventoryApi.IsItemDragActive||entry.Item.contentWindow?.IsVisible()==true||entry.Item.examineWindow?.IsVisible()==true||
            SaveGraphApi.Items(entry.Item).Count!=1)return false;
        _busy=true;var identity=_identity!;var root=entry.Item;
        try {
            // 先把空root从原生存档索引摘除；失败时保留引用并封锁保存，不能留下指向已销毁对象的键。
            if(!Retained.Any(x=>x.Pointer==root.Pointer))Retained.Add(root);
            if(!store.saveBags!.Remove(key))throw new InvalidOperationException("EZAPI_ENDPOINT_DELETE_READBACK");
            if(!NativeAccess.SameRun(identity))throw new InvalidOperationException("EZAPI_ENDPOINT_STALE");
            Entries.Remove(key);
            root.Destroy();Retained.Remove(root);return true;
        }catch(Exception e){_fault=true;Error="EZAPI_ENDPOINT_DELETE: "+e.Message;throw;}
        finally{_busy=false;}
    }
    // 查询钩子只枚举已经恢复并绑定的根，绝不从原生FindAllItem递归触发Decode。
    internal static IReadOnlyList<GameItem> RuntimeItems(PlayerStore store,bool chargingOnly=false,bool ownedOnly=false)
    {
        if(!IsAvailable||_busy||_transferBusy||!_restored||_identity!=GameSession.Identity||!GameSession.TryResolve(null,out var player)||store.Pointer!=player.Pointer)
            return Array.Empty<GameItem>();
        var result=new List<GameItem>();var ids=new HashSet<int>();
        foreach(var e in Entries.Values) {
            if(!e.IsCurrent)throw new InvalidOperationException("EZAPI_ENDPOINT_IDENTITY");
            if(chargingOnly&&!e.ChargingOnly)continue;
            foreach(var item in SaveGraphApi.Items(e.Item)) {
                if(!ids.Add(item.uniqueId))throw new InvalidOperationException("EZAPI_ENDPOINT_ALIAS");
                if(!ownedOnly||GeneralHelper.IsItemOwned(item))result.Add(item);
            }
        }
        return result.AsReadOnly();
    }
    
    internal static void EnsureRestored(PlayerStore store)
    {
        if(_busy||_transferBusy)throw new InvalidOperationException("EZAPI_ENDPOINT_REENTRANT");
        var identity=GameSession.Identity!;
        if(_identity!=identity){
            // 场景往返可能只换Epoch而继续使用相同原生库存；更换句柄，不能重建或复制原图。
            var keep=_identity is not null&&_identity.RunId==identity.RunId&&_identity.SaveSlotId==identity.SaveSlotId&&
                Entries.All(p=>store.saveBags?.ContainsKey(p.Key)==true&&store.saveBags[p.Key]?.Pointer==p.Value.Item.Pointer);
            if(keep&&_restored&&!_fault) {
                foreach(var pair in Entries.ToArray())Entries[pair.Key]=new(pair.Value.OwnerId,pair.Value.EndpointId,pair.Value.Item,pair.Value.ChargingOnly,identity);
                _identity=identity;return;
            }
            Entries.Clear();_identity=identity;_restored=false;_fault=false;Error=null;
        }
        if(_fault)throw new InvalidOperationException(Error??"EZAPI_ENDPOINT_FAULT");
        if(_restored)return;
        _busy=true;
        try {
            var data=store.modData;
            if(data is null||!data.ContainsKey(StateKey)){_restored=true;return;}
            var state=InventoryEndpointCodec.Parse(data[StateKey],identity.SaveSlotId,identity.RunId);
            if(store.saveBags is null)store.saveBags=new();
            var bags=store.saveBags;
            if(Entries.Count+state.Endpoints.Count>Limit||bags.Count+state.Endpoints.Count>4096)
                throw new InvalidOperationException("EZAPI_ENDPOINT_LIMIT");
            foreach(var saved in state.Endpoints)if(bags.ContainsKey(Key(saved.OwnerId,saved.EndpointId)))
                throw new InvalidOperationException("EZAPI_ENDPOINT_CONFLICT: saved native key occupied.");

            // Decode every graph off-index first. Cross-endpoint instance aliases are rejected before any root is published.
            var reservedIds=InventoryApi.CaptureRunItems(store).Select(i=>i.uniqueId).ToHashSet();
            var staged=new List<(string Key,InventoryEndpointSaved Saved,GameItem Root)>();
            foreach(var saved in state.Endpoints) {
                var key=Key(saved.OwnerId,saved.EndpointId);
                var root=SaveGraphApi.Decode(saved.Graph,reservedIds);
                if(!Retained.Any(x=>x.Pointer==root.Pointer))Retained.Add(root);
                _=Grid(root);
                var graph=SaveGraphApi.Items(root);
                if(!NativeAccess.SameRun(identity))throw new InvalidOperationException("EZAPI_ENDPOINT_STALE");
                if(root.identifier!=saved.ItemId||root.uniqueId!=saved.InstanceId||root.unitCount<=0||root.parentInventory is not null||!GeneralHelper.IsItemOwned(root))
                    throw new InvalidOperationException("EZAPI_ENDPOINT_RESTORE_ROOT");
                foreach(var item in graph)if(!reservedIds.Add(item.uniqueId))
                    throw new InvalidOperationException("EZAPI_ENDPOINT_ALIAS: restored endpoint graphs overlap.");
                staged.Add((key,saved,root));
            }

            var addedBags=new List<(string Key,GameItem Root)>();
            var addedEntries=new List<(string Key,InventoryEndpoint Entry)>();
            try {
                foreach(var row in staged) {
                    if(bags.ContainsKey(row.Key))throw new InvalidOperationException("EZAPI_ENDPOINT_CONFLICT: native key changed during restore.");
                    var restored=new InventoryEndpoint(row.Saved.OwnerId,row.Saved.EndpointId,row.Root,row.Saved.ChargingOnly,identity);
                    bags.Add(row.Key,row.Root);addedBags.Add((row.Key,row.Root));
                    Entries.Add(row.Key,restored);addedEntries.Add((row.Key,restored));
                    if(!restored.IsCurrent)throw new InvalidOperationException("EZAPI_ENDPOINT_READBACK");
                }
            }catch {
                // Roll back only the exact handles written by this restore. Decoded graphs stay rooted for diagnostics/reload.
                foreach(var row in addedEntries.AsEnumerable().Reverse())
                    if(Entries.TryGetValue(row.Key,out var current)&&ReferenceEquals(current,row.Entry))Entries.Remove(row.Key);
                foreach(var row in addedBags.AsEnumerable().Reverse())
                    if(bags.ContainsKey(row.Key)&&bags[row.Key]?.Pointer==row.Root.Pointer)bags.Remove(row.Key);
                throw;
            }
            foreach(var row in staged)Retained.Remove(row.Root);
            _restored=true;
        }catch(Exception e){_fault=true;Error="EZAPI_ENDPOINT_RESTORE: "+e.Message;throw;}
        finally{_busy=false;}
    }
    // Exposed internally for product code and direct offline boundary tests.
    internal static InventoryEndpointDocument Parse(string json,int slot,string run)=>InventoryEndpointCodec.Parse(json,slot,run);
    internal static string Encode(PlayerStore store)
    {
        Store(store);EnsureRestored(store);
        if(_busy||_transferBusy)throw new InvalidOperationException("EZAPI_ENDPOINT_REENTRANT");
        _busy=true;
        try {
            var scope=_identity!;var rows=new List<InventoryEndpointSaved>();
            InventoryApi.CaptureRunItems(store); // 完整原生图中重复实例ID/别名在编码任何记录前拒绝。
            var graphPointers=new HashSet<IntPtr>();var graphIds=new HashSet<int>();
            foreach(var entry in Entries.Values.OrderBy(e=>e.OwnerId,StringComparer.Ordinal).ThenBy(e=>e.EndpointId,StringComparer.Ordinal)) {
                if(!entry.IsCurrent)throw new InvalidOperationException("EZAPI_ENDPOINT_IDENTITY");
                foreach(var item in SaveGraphApi.Items(entry.Item))if(!graphPointers.Add(item.Pointer)||!graphIds.Add(item.uniqueId))
                    throw new InvalidOperationException("EZAPI_ENDPOINT_ALIAS: endpoint graphs overlap or duplicate instance IDs.");
                var graph=SaveGraphApi.Encode(entry.Item);
                if(!entry.IsCurrent)throw new InvalidOperationException("EZAPI_ENDPOINT_STALE");
                rows.Add(new(entry.OwnerId,entry.EndpointId,entry.Item.identifier,entry.Item.uniqueId,entry.ChargingOnly,graph));
            }
            return InventoryEndpointCodec.Serialize(scope.SaveSlotId,scope.RunId,Array.AsReadOnly(rows.ToArray()));
        }finally{_busy=false;}
    }
    internal static void StageSave(PlayerStore store)
    {
        if(!_started||!GameSession.OnThread)return;
        if(_transferBusy)throw new InvalidOperationException("EZAPI_ENDPOINT_REENTRANT: save requested during a native endpoint transfer.");
        if(Entries.Count==0&&store.modData?.ContainsKey(StateKey)!=true)return;
        if(_fault)throw new InvalidOperationException(Error??"EZAPI_ENDPOINT_FAULT");
        var encoded=Encode(store);
        if(store.modData is null)store.modData=new();
        store.modData[StateKey]=encoded;
        if(store.modData[StateKey]!=encoded)throw new InvalidOperationException("EZAPI_ENDPOINT_STAGE_READBACK");
        SaveError=null;
    }
    internal static void RuntimeFault(Exception error)
    {
        _fault=true;Error="EZAPI_ENDPOINT_RUNTIME: "+error.Message;
    }
    internal static void SaveFailed(Exception error)=>SaveError="EZAPI_ENDPOINT_SAVE_BLOCKED: "+error.Message;
    internal static void BeforeLoad()
    {
        // Drop only exact bindings from the currently loaded native bag map; never destroy a displayed old graph here.
        if(GameSession.OnThread&&GameSession.TryResolve(null,out var store)&&store.saveBags is not null)
            foreach(var pair in Entries)
                if(store.saveBags.ContainsKey(pair.Key)&&store.saveBags[pair.Key]?.Pointer==pair.Value.Item.Pointer)store.saveBags.Remove(pair.Key);
        Entries.Clear();_identity=null;_restored=_fault=false;Error=null;SaveError=null;
    }
    internal static void Stop(){BeforeLoad();_started=false;_transferBusy=false;}
    internal static object Report()=>new{ready=IsAvailable,error=Error,unavailableReason=UnavailableReason,restoreAvailable=SaveGraphApi.CanDecode,saveError=SaveError,retained=Retained.Count,hooks=InventoryEndpointRuntime.Status(),restored=_restored,busy=_busy,transferBusy=_transferBusy,identity=_identity,
        entries=Entries.Values.Select(e=>new{e.OwnerId,e.EndpointId,e.ChargingOnly,current=e.IsCurrent}).ToArray()};
}

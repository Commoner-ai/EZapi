using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>给指定容器定义和子库存追加只读准入限制；多个owner按AND组合，不能覆盖原生拒绝。</summary>
public static class InventoryAdmissionApi
{
    private sealed class Rule:IDisposable
    {
        internal readonly string Owner,ItemId,InventoryId;
        internal readonly Func<GameItem,GameGridInventory,bool> Allows;
        private int _disposed;
        internal bool Faulted;
        internal bool Disposed=>Volatile.Read(ref _disposed)!=0;
        internal Rule(string owner,string itemId,string inventoryId,Func<GameItem,GameGridInventory,bool> allows)
            =>(Owner,ItemId,InventoryId,Allows)=(owner,itemId,inventoryId,allows);
        public void Dispose()=>Interlocked.Exchange(ref _disposed,1);
    }
    private static readonly List<Rule> Rules=new();
    private static readonly HashSet<(IntPtr,IntPtr)> Evaluating=new();
    public static bool IsAvailable=>GameSession.OnThread && InventoryRuntime.VerifyAdmission();

    /// <summary>初始化主线程可提前注册，原生组就绪才生效；重复(owner,itemId,inventoryId)拒绝。回调仅同步查询，无写入/递归准入。</summary>
    public static IDisposable Register(string ownerId,string itemId,string inventoryId,Func<GameItem,GameGridInventory,bool> allows)
    {
        GameSession.RequireThread();
        if(!InventoryRuntime.Started)throw new InvalidOperationException("EZAPI_ADMISSION_NOT_READY: register after EZapi initialization.");
        RunData.ValidateModId(ownerId);ArgumentNullException.ThrowIfNull(allows);
        foreach(var id in new[]{itemId,inventoryId})if(string.IsNullOrWhiteSpace(id)||id.Length>160||id.Any(char.IsControl))
            throw new ArgumentException("EZAPI_ADMISSION_ID: nonempty item/inventory IDs <=160 required.");
        Rules.RemoveAll(r=>r.Disposed);
        if(Rules.Count>=256)throw new InvalidOperationException("EZAPI_ADMISSION_LIMIT: max256 rules.");
        if(Rules.Any(r=>r.Owner==ownerId&&r.ItemId==itemId&&r.InventoryId==inventoryId))
            throw new ArgumentException("EZAPI_ADMISSION_DUPLICATE: dispose the old rule before replacing it.");
        var rule=new Rule(ownerId,itemId,inventoryId,allows);Rules.Add(rule);return rule;
    }
    internal static bool Allows(GameItem? item,GameGridInventory? inventory)
    {
        if(!InventoryRuntime.AdmissionReady||!GameSession.OnThread||!GameSession.TryResolve(null,out _))return true; // 目录/存档重建阶段不接管准入。
        if(item is null||inventory is null||item.Pointer==IntPtr.Zero||inventory.Pointer==IntPtr.Zero)return false;
        var owner=inventory.GetParentItem();if(owner is null||owner.Pointer==IntPtr.Zero)return true;
        var matching=Rules.Where(r=>!r.Disposed&&r.ItemId==owner.identifier&&r.InventoryId==inventory.identifier).ToArray();
        if(matching.Length==0)return true;
        var key=(item.Pointer,inventory.Pointer);
        if(!Evaluating.Add(key))return false;
        var run=GameSession.Identity!;
        var ownerPointer=owner.Pointer;var ownerId=owner.identifier;var itemPointer=item.Pointer;var itemId=item.identifier;
        var itemUnits=item.unitCount;var targetPointer=inventory.Pointer;var targetId=inventory.identifier;
        try {
            if(!GeneralHelper.IsItemOwned(owner))return false;
            foreach(var rule in matching) {
                if(rule.Disposed)continue;
                if(rule.Faulted)return false;
                try {
                    var permitted=rule.Allows(item,inventory);
                    if(!NativeAccess.SameRun(run))return false;
                    if(owner.Pointer!=ownerPointer||owner.identifier!=ownerId||item.Pointer!=itemPointer||item.identifier!=itemId||
                        item.unitCount!=itemUnits||inventory.Pointer!=targetPointer||inventory.identifier!=targetId||inventory.GetParentItem()?.Pointer!=ownerPointer)
                        throw new InvalidOperationException("EZAPI_ADMISSION_MUTATION: predicate changed item, owner or target identity.");
                    if(!permitted)return false;
                } catch(Exception e) {
                    rule.Faulted=true;
                    MelonLogger.Error("[EZapi] EZAPI_ADMISSION_CALLBACK: "+rule.Owner+" "+rule.ItemId+"/"+rule.InventoryId+": "+e);
                    return false;
                }
                if(!NativeAccess.SameRun(run))return false;
            }
            return true;
        } finally {Evaluating.Remove(key);}
    }
    internal static void Stop(){foreach(var rule in Rules)rule.Dispose();Rules.Clear();Evaluating.Clear();}
    internal static object Report()=>Rules.Where(r=>!r.Disposed).Select(r=>new{owner=r.Owner,itemId=r.ItemId,inventoryId=r.InventoryId,faulted=r.Faulted}).ToArray();
}

using Il2Cpp;

namespace Il2CppSystem
{
    public sealed class Action<T>(System.Action<T> callback){public void Invoke(T value)=>callback(value);}
}
namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T ConvertDelegate<T>(System.Delegate callback) where T:class
            => (T)Activator.CreateInstance(typeof(T),callback)!;
    }
}

namespace Il2CppSystem.Collections.Generic
{
    public class Dictionary<K,V> : System.Collections.Generic.Dictionary<K,V> where K : notnull
    {
        private static long _pointer;
        public IntPtr Pointer { get; } = new(Interlocked.Increment(ref _pointer));
        public Action? BeforeWrite, AfterWrite;
        public int Writes;
        public new void Add(K key,V value) { BeforeWrite?.Invoke();base.Add(key,value);Writes++;AfterWrite?.Invoke(); }
        public new V this[K key] { get=>base[key];set{BeforeWrite?.Invoke();base[key]=value;Writes++;AfterWrite?.Invoke();} }
    }
}

namespace Il2Cpp
{
    public sealed class TagState
    {
        private static long _pointer;
        public IntPtr Pointer {get;}=new(Interlocked.Increment(ref _pointer));
        private bool _enabled;private string _value="";
        public TagState(string id,string readableName){Identifier=id;}
        public string Identifier {get;}
        public bool IsEnabled()=>_enabled;
        public string GetString()=>_value;
        public TagState Enable(){_enabled=true;return this;}
        public TagState SetString(string value){_value=value;return this;}
        public TagState Clone(){var copy=new TagState(Identifier,Identifier);copy._enabled=_enabled;copy._value=_value;return copy;}
    }
    public sealed class TagSystem
    {
        private static long _pointer;
        public IntPtr Pointer {get;}=new(Interlocked.Increment(ref _pointer));
        public Il2CppSystem.Collections.Generic.Dictionary<string,TagState> dict=new();
        public TagState GetTag(string key){if(!dict.TryGetValue(key,out var tag))dict.Add(key,tag=new(key,key));return tag;}
    }
    public class GameInventory { public IntPtr Pointer=new(900); }
    public sealed class GameGridInventory : GameInventory
    {
        public bool Locked,MayInsert=true,MayOwn=true; public SlotMarker? Marker; public Action? OnFind;
        public bool IsInsertLocked()=>Locked;
        public SlotMarker? TryFindOneValidInventorySlot(GameItem item,bool merge) {OnFind?.Invoke();return Marker;}
    }
    public sealed class SlotMarker
    {
        public IntPtr Pointer=new(9000); public GameItem? item,targetItem; public GameGridInventory? inventory; public object? itemGridShape=new();
        public int numTransfer=1,Calls; public bool Valid=true; public Action? OnAccept;
        public bool IsValid()=>Valid;
        public int TryAcceptOnce(int amount){Calls++;OnAccept?.Invoke();return amount;}
    }
    public static class GeneralHelper
    {
        public static bool MayPlayerInsertInto(GameGridInventory target)=>target.MayInsert;
        public static bool MayOwnershipAcceptItem(GameItem item,GameGridInventory target)=>target.MayOwn;
    }
    public sealed class GameItem
    {
        private static long _pointer,_identity;
        public IntPtr Pointer=new(Interlocked.Increment(ref _pointer));
        public int uniqueId=(int)Interlocked.Increment(ref _identity),unitCount=1;
        public string identifier="base.item";
        public GameInventory? parentInventory;
        public TagSystem? state=new();
        public Action? OnSync, OnRead; public bool Owned=true, Dead; public int Modifications;
        public bool ThrowSync;
        public int ModifyCallbackCount=1;
        public bool ReturnEmptyReadonlyTagForMissing;
        public GameItem ModifyTag(string key,Il2CppSystem.Action<TagState> callback,bool skipValidate=false){Modifications++;for(var i=0;i<ModifyCallbackCount;i++)callback.Invoke(state!.GetTag(key));return this;}
        public TagState? GetTagReadonly(string key){ if(Dead)throw new Exception("destroyed wrapper"); OnRead?.Invoke(); return state!.dict.TryGetValue(key,out var tag)?tag:ReturnEmptyReadonlyTagForMissing?new TagState(key,key):null; }
        public void SyncModifiedState(){if(ThrowSync)throw new InvalidOperationException("sync");OnSync?.Invoke();}
    }
    public sealed class PlayerStore
    {
        private static long _pointer;
        public IntPtr Pointer=new(Interlocked.Increment(ref _pointer));
        public static PlayerStore? instance;
    }
}

namespace EZapi
{
    internal static class GameSession
    {
        internal static bool OnThread=true;
        internal static GameRunIdentity? Identity=new(534,"run-a",1);
        internal static PlayerStore Resolve(GameRunIdentity run)=>TryResolve(run.Epoch,out var player)&&Identity==run ? player:throw new Exception("stale");
        internal static bool TryResolve(long? epoch,out PlayerStore player){player=PlayerStore.instance!;return OnThread&&Identity is not null&&(!epoch.HasValue||epoch==Identity.Epoch)&&player is not null;}
    }
    internal static class NativeAccess
    {
        internal static GameRunIdentity RequireRun()=>GameSession.Identity??throw new InvalidOperationException("no run");
        internal static GameItem RequireItem(GameItem item)=>item;
        internal static bool SameRun(GameRunIdentity run)=>GameSession.OnThread && GameSession.Identity==run;
    }
    internal static class RunData
    {
        internal static void ValidateModId(string id){if(id is null||id.Length>160||!System.Text.RegularExpressions.Regex.IsMatch(id,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*$"))throw new ArgumentException();}
    }
    internal static class GameBuildFingerprint {internal static bool IsKnown=true;}
    public static class InventoryFreezeApi
    {
        public static bool IsAvailable=true;public static GameItem? Frozen;
        public static bool IsFrozen(GameItem item)=>ReferenceEquals(Frozen,item);
    }
    // Inventory graph traversal/ownership are explicit native boundary stand-ins.
    public sealed record InventoryShape;
    public sealed record NativeInventoryItem(long NativeIdentity,int InstanceId,string ItemId,int Units,bool PlayerOwned,InventoryShape? Shape);
    public static class InventoryApi
    {
        public static bool IsItemDragActive;
        public static List<GameItem> Items=new(); public static Action? OnCapture; public static int Captures;
        public static IReadOnlyList<GameItem> CaptureRunItems(PlayerStore store){Captures++;OnCapture?.Invoke();return Items.ToArray();}
    }
    internal static class InventoryChecks
    {
        internal static NativeInventoryItem Item(GameItem item,bool geometry)
        { if(item.Dead||item.Pointer==IntPtr.Zero||item.unitCount<1)throw new Exception("invalid native item"); return new(item.Pointer.ToInt64(),item.uniqueId,item.identifier,item.unitCount,item.Owned,null); }
    }
}

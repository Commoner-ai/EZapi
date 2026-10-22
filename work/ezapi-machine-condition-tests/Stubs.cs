// 离线原生边界替身；真实条件注册、快照、路由、故障和释放逻辑链接生产源码。
namespace Il2Cpp {
 public sealed class TagSystem {static int next;public IntPtr Pointer=new(++next);public sealed class Dict {static int next;public IntPtr Pointer=new(++next);}public Dict dict=new();public int Value;}
 public sealed class GameItem {static int next;public IntPtr Pointer=new(++next);public string identifier="machine";public int uniqueId=next,unitCount=1;public long unitValue;public TagSystem state=new();public GameInventory? parentInventory;}
 public sealed class GameInventory {static int next;public IntPtr Pointer=new(++next);public GameItem? Owner;public int Contents;public GameItem? GetParentItem()=>Owner;}
}
namespace MelonLoader {public static class MelonLogger {public static int Warnings;public static void Warning(string value)=>Warnings++;}}
namespace EZapi {
 internal static class ItemRegistry {internal static IReadOnlyList<ItemRegistrationResult> RegisterBatch(IReadOnlyList<ItemDefinition> items)=>throw new NotSupportedException("offline registration boundary");}
 public sealed record GameRunIdentity(int Epoch);
 internal static class GameSession {internal static bool OnThread=true,Loaded=true;internal static GameRunIdentity Identity=new(1);internal static void RequireThread(){if(!OnThread)throw new InvalidOperationException("thread");}internal static bool TryResolve(object? e,out object? player){player=null;return Loaded;}}
 internal static class NativeAccess {internal static GameRunIdentity RequireRun()=>GameSession.Identity;internal static bool SameRun(GameRunIdentity run)=>GameSession.Loaded&&run==GameSession.Identity;}
 internal static class MachineRecipeRules {internal static void Owner(string s){if(string.IsNullOrWhiteSpace(s))throw new ArgumentException("owner");}internal static void Id(string s){if(string.IsNullOrWhiteSpace(s))throw new ArgumentException("id");}}
 internal static class InventoryChecks {internal static (IntPtr,int,string,int) Item(Il2Cpp.GameItem i)=>(i.Pointer,i.uniqueId,i.identifier,i.unitCount);internal static (IntPtr,IntPtr,int) Read(Il2Cpp.GameInventory i,GameRunIdentity r)=>(i.Pointer,i.Owner?.Pointer??IntPtr.Zero,i.Contents);internal static bool Equal((IntPtr,IntPtr,int) a,(IntPtr,IntPtr,int) b)=>a==b;}
 internal sealed class NativeTagBackup {readonly int value;NativeTagBackup(int v)=>value=v;internal static NativeTagBackup Capture(Il2Cpp.TagSystem s)=>new(s.Value);internal bool Matches(Il2Cpp.TagSystem s)=>s.Value==value;}
}

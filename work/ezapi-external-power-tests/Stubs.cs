// 只替换游戏身份、原生对象、钩子安装；真实ExternalPowerApi注册、查询、事务状态机直接链接。
namespace Il2Cpp {
 public class GameInventory {public IntPtr Pointer=(IntPtr)900;}
 public class Tags {public Dictionary<string,Tags> dict=new();public Tags GetTag(string id)=>this;public void Enable(){}}
 public class GameItem {public object? contentWindow;public Tags state=new();public Tags? GetTagReadonly(string id)=>null;public IntPtr Pointer;public int uniqueId,unitCount=1;public string identifier="author.power.connector";public GameInventory? parentInventory=new();}
}
namespace Il2Cpp {public static class GeneralHelper {public static bool IsItemOwned(GameItem item)=>false;}}
namespace EZapi {
 public enum MachineMaterialContentsPolicy {Reject,ConsumeGraph}
 internal sealed record GameRunIdentity(int Epoch);
 internal static class GameSession {internal static bool OnThread=true;internal static GameRunIdentity Run=new(1);internal static void RequireThread(){if(!OnThread)throw new InvalidOperationException("thread");}}
 internal static class NativeAccess {internal static GameRunIdentity RequireRun(){GameSession.RequireThread();return GameSession.Run;}internal static Il2Cpp.GameItem RequireItem(Il2Cpp.GameItem item){RequireRun();if(item.unitCount<=0||item.Pointer==IntPtr.Zero)throw new ArgumentException();return item;}internal static bool SameRun(GameRunIdentity run)=>GameSession.OnThread&&GameSession.Run==run;}
 internal static class MachineRecipeRules {
  internal static void Owner(string owner){if(owner is null||!System.Text.RegularExpressions.Regex.IsMatch(owner,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z"))throw new ArgumentException();}
  internal static void Id(string id){if(string.IsNullOrWhiteSpace(id)||id.Length>160||id.Any(char.IsControl))throw new ArgumentException();}
 }
 internal static class ExternalPowerRuntime {internal static bool Ready=true;internal static bool VerifyReady()=>Ready;internal static object Status()=>new{Ready};internal static void Stop(){}internal static void Initialize(Il2Cpp.GameItem item){}internal static void Record(string stage,Exception e){} }
}

namespace EZapi { internal static class NativeFurnacePower {internal static object Report()=>new{scope="boundary stub; native furnace not executed in provider tests"};} }

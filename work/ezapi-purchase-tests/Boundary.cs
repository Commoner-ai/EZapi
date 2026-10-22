namespace Il2Cpp
{
 public sealed class PlayerStore
 {
  public IntPtr Pointer=new(1);
  public int playerCash=100,Debits,Refunds,Reads;
  public Action<PlayerStore,int>? Debit,Refund;
  public Func<PlayerStore,int>? Read;
  public int GetCash(){Reads++;return Read?.Invoke(this)??playerCash;}
  public void PayMoney(int value,bool sound){if(sound)throw new Exception("unexpected sound");Debits++;if(Debit is null)playerCash-=value;else Debit(this,value);}
  public void ModCash(int value,bool sound){if(sound)throw new Exception("unexpected sound");Refunds++;if(Refund is null)playerCash+=value;else Refund(this,value);}
 }
}
namespace EZapi
{
 public sealed record GameRunIdentity(int SaveSlotId,string RunId,long Epoch);
 internal static class GameSession
 {
  internal static readonly int Thread=Environment.CurrentManagedThreadId;
  internal static GameRunIdentity? Identity=new(532,"test",1);
  internal static Il2Cpp.PlayerStore? Player=new();
  internal static void RequireThread(){if(Environment.CurrentManagedThreadId!=Thread)throw new InvalidOperationException("thread");}
  internal static bool TryResolve(long? epoch,out Il2Cpp.PlayerStore player){player=Player!;return Environment.CurrentManagedThreadId==Thread&&Identity is not null&&Player is not null&&(!epoch.HasValue||Identity.Epoch==epoch);}
 }
}

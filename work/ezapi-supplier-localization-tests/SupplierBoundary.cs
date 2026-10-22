// Only the visit scheduler and native wrappers are replaced. SupplierApi, the
// dialogue adapter, graph planner, execution and all locale contracts are linked.
namespace EZapi {
 internal static class ClientVisitApi {
  internal static bool IsAvailable=true;
  internal static DialogueScript? Script;
  internal static System.Func<Il2Cpp.StoreClient>? Factory;
  internal static System.Action? Opened,Dismissed;
  internal static PresentationStatus State=PresentationStatus.Queued;
  internal static PresentationResult QueueManaged(string owner,string request,string run,int slot,string actor,string sprite,DialogueScript script,System.Func<Il2Cpp.StoreClient> factory,System.Action dismissed,System.Action opened){Script=script;Factory=factory;Opened=opened;Dismissed=dismissed;State=PresentationStatus.Queued;return new(State);}
  internal static PresentationResult Status(string owner,string request)=>new(State);
  internal static void Arrive(){State=PresentationStatus.Arrived;var r=DialogueApi.Open("author","author.visit","run",1,Script!);if(r.Status!=PresentationStatus.Opened)throw new System.Exception(r.Reason);Opened!();}
 }
}
namespace Il2Cpp {
 public sealed class StoreClient {
  public const int FACTION_LOWER=0;public enum ClientIntent{DIALOGUE}
  public string identifier="",displayName="",spriteName="";public int clientFaction;public ClientIntent clientIntent;
  public bool dismissable,arrestable,nonShootable,isRegularSellDisable,isBargainAvailable,isMultiBuyDisabled,isNoContributeToEvidence;
 }
}

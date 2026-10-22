using System.Text.Json;
using EZapi;
using Il2Cpp;

var passed=new List<string>();
void Check(bool condition,string name){if(!condition)throw new Exception(name);passed.Add(name);}
PlayerStore ResetRun(long epoch=1){GameSession.Identity=new(534,"run-a",epoch);GameSession.OnThread=true;GameBuildFingerprint.IsKnown=true;InventoryFreezeApi.IsAvailable=true;InventoryFreezeApi.Frozen=null;InventoryApi.IsItemDragActive=false;return PlayerStore.instance=new();}
var player=ResetRun();var item=new GameItem{ReturnEmptyReadonlyTagForMissing=true};const string owner="author.tools",key="upgrade";
var missing=ItemDataApi.Read<JsonElement>(item,owner,key,1);
Check(missing.Status==ItemDataStatus.Missing&&missing.Revision?.Json is null&&item.state!.dict.Count==0,"missing read handles empty native readonly sentinel without creating a stored tag");
var write=ItemDataApi.Stage(item,missing.Revision!,new {level=2},1);
Check(write.Status==ItemDataStatus.StagedInMemory&&item.state!.dict.Count==1&&item.state.dict.Values.Single().IsEnabled(),"first stage adds one enabled namespaced tag");
var tagKey=ItemDataApi.TagPrefix+owner+"|"+key;
Check(item.state!.dict[tagKey].GetString()==write.Json&&item.state.dict[tagKey].Pointer!=IntPtr.Zero,"stage writes one bounded versioned JSON envelope");
var read=ItemDataApi.Read<Level>(item,owner,key,1);
Check(read.Status==ItemDataStatus.Present&&read.Value==new Level(2)&&read.Json==write.Json,"typed read returns value and exact comparison revision");
var migrated=ItemDataApi.Stage(item,read.Revision!,new Level(3),2);
Check(migrated.Status==ItemDataStatus.StagedInMemory&&ItemDataApi.Read<Level>(item,owner,key,2).Value==new Level(3),"explicit version update applies after matching revision");
var mismatch=ItemDataApi.Read<Level>(item,owner,key,1);
Check(mismatch.Status==ItemDataStatus.VersionMismatch&&mismatch.Version==2&&mismatch.Revision?.Json==migrated.Json,"version mismatch preserves raw JSON and migration token");
Check(ItemDataApi.Stage(item,mismatch.Revision!,new Level(4),3).Status==ItemDataStatus.StagedInMemory,"migration can explicitly replace a prior version");
Check(ItemDataApi.Stage(item,read.Revision!,new Level(5),2).Status==ItemDataStatus.Conflict,"stale same-item revision cannot overwrite newer tag");

var otherOwnerKey=ItemDataApi.TagPrefix+"other.mod|state";
item.state.GetTag(otherOwnerKey).Enable().SetString("other owner");var beforeOther=item.state.dict[otherOwnerKey].GetString();
var fresh=ItemDataApi.Read<Level>(item,owner,key,3);
Check(ItemDataApi.Stage(item,fresh.Revision!,new Level(6),3).Status==ItemDataStatus.StagedInMemory&&item.state.dict[otherOwnerKey].GetString()==beforeOther,"same item writes preserve another owner tag");

var badAddress=new[]{("Author.tools","x"),(owner,""),(owner,"../x"),(owner,new string('x',121))};
foreach(var (badOwner,badKey) in badAddress)Check(ItemDataApi.Read<JsonElement>(item,badOwner,badKey).Status==ItemDataStatus.Invalid,"invalid owner/key rejected: "+badKey.Length);
Check(ItemDataApi.Read<JsonElement>(item,owner,key,0).Status==ItemDataStatus.Invalid,"zero requested version rejected");
Check(ItemDataApi.Read<JsonElement>(item,owner,key).Status==ItemDataStatus.VersionMismatch,"default reader version is explicit version one");

var uniqueTag=ItemDataApi.TagPrefix+owner+"|damaged";
item.state.GetTag(uniqueTag).Enable().SetString("{broken");
Check(ItemDataApi.Read<Level>(item,owner,"damaged").Status==ItemDataStatus.Invalid,"malformed stored JSON stays visible as invalid");
var disabled=item.state.GetTag(ItemDataApi.TagPrefix+owner+"|disabled").SetString("{}");
Check(ItemDataApi.Read<JsonElement>(item,owner,"disabled").Status==ItemDataStatus.Invalid,"disabled tag is not treated as an empty value");
var duplicate="{\"version\":1,\"version\":1,\"data\":{}}";
item.state.GetTag(ItemDataApi.TagPrefix+owner+"|duplicate").Enable().SetString(duplicate);
Check(ItemDataApi.Read<JsonElement>(item,owner,"duplicate").Status==ItemDataStatus.Invalid,"duplicate envelope member is rejected");
item.state.GetTag(ItemDataApi.TagPrefix+owner+"|unknown").Enable().SetString("{\"version\":1,\"data\":{},\"extra\":1}");
Check(ItemDataApi.Read<JsonElement>(item,owner,"unknown").Status==ItemDataStatus.Invalid,"unknown envelope member is rejected");

var frozenToken=ItemDataApi.Read<Level>(item,owner,key,3).Revision!;InventoryFreezeApi.Frozen=item;
Check(ItemDataApi.Stage(item,frozenToken,new Level(7),3).Status==ItemDataStatus.Frozen,"frozen item refuses writes");InventoryFreezeApi.Frozen=null;
var dragToken=ItemDataApi.Read<Level>(item,owner,key,3).Revision!;InventoryApi.IsItemDragActive=true;
Check(ItemDataApi.Stage(item,dragToken,new Level(7),3).Status==ItemDataStatus.Conflict,"active drag refuses writes");InventoryApi.IsItemDragActive=false;

var changedToken=ItemDataApi.Read<Level>(item,owner,key,3).Revision!;item.state.dict[changedTokenTagKey(changedToken)] = item.state.dict[changedTokenTagKey(changedToken)].Clone().Enable().SetString("{\"version\":3,\"data\":{\"level\":8}}");
Check(ItemDataApi.Stage(item,changedToken,new Level(9),3).Status==ItemDataStatus.Conflict,"external tag change between read and stage conflicts");
string changedTokenTagKey(ItemDataRevision token)=>ItemDataApi.TagPrefix+owner+"|"+key;

var samePointerNewIdentity=ItemDataApi.Read<Level>(item,owner,key,3).Revision!;item.uniqueId++;
Check(ItemDataApi.Stage(item,samePointerNewIdentity,new Level(9),3).Status==ItemDataStatus.Conflict,"reused pointer with changed item identity rejects write");item.uniqueId--;
var staleRun=ItemDataApi.Read<Level>(item,owner,key,3).Revision!;GameSession.Identity=new(534,"run-a",2);
Check(ItemDataApi.Stage(item,staleRun,new Level(9),3).Status==ItemDataStatus.Conflict,"same-slot reload invalidates item revision");GameSession.Identity=new(534,"run-a",1);

var partialToken=ItemDataApi.Read<Level>(item,owner,key,3).Revision!;item.ThrowSync=true;
Check(ItemDataApi.Stage(item,partialToken,new Level(10),3).Status==ItemDataStatus.Indeterminate,"post-write sync exception is indeterminate, never silently retried");item.ThrowSync=false;
var readAfterFailure=ItemDataApi.Read<Level>(item,owner,key,3);
Check(readAfterFailure.Status==ItemDataStatus.Present&&readAfterFailure.Value==new Level(10),"caller can reconcile a write whose sync result was unknown");
var replaceToken=readAfterFailure.Revision!;item.OnSync=()=>item.state=new TagSystem();
Check(ItemDataApi.Stage(item,replaceToken,new Level(11),3).Status==ItemDataStatus.Indeterminate,"state replacement during sync is reported indeterminate");item.OnSync=null;

var atLimit=ItemDataApi.Read<JsonElement>(item,owner,"huge").Revision!;
Check(ItemDataApi.Stage(item,atLimit,new string('中',ItemDataApi.MaxJsonUtf8Bytes),1).Status==ItemDataStatus.Invalid,"oversized encoded JSON is rejected before writes");
var preWrites=item.state!.dict.Writes;GameSession.OnThread=false;
Check(ItemDataApi.Read<JsonElement>(item,owner,key).Status==ItemDataStatus.Disabled&&ItemDataApi.Stage(item,null!,new Level(1)).Status==ItemDataStatus.Disabled&&item.state.dict.Writes==preWrites,"off-thread reads and writes do no native access");
GameSession.OnThread=true;

item.state=null;
Check(ItemDataApi.Read<JsonElement>(item,owner,"state-less").Status==ItemDataStatus.Unavailable,"item without native state is never initialized behind caller's back");
var detached=new GameItem{Pointer=IntPtr.Zero};
Check(ItemDataApi.Read<JsonElement>(detached,owner,"dead").Status==ItemDataStatus.Unavailable,"destroyed item is unavailable");
var buildItem=new GameItem();var buildToken=ItemDataApi.Read<JsonElement>(buildItem,owner,"missing").Revision!;
GameBuildFingerprint.IsKnown=false;
Check(ItemDataApi.Read<JsonElement>(buildItem,owner,"missing").Status==ItemDataStatus.Unavailable&&ItemDataApi.Stage(buildItem,buildToken,new Level(1)).Status==ItemDataStatus.Unavailable,"unknown game build fails closed for valid reads and writes");
GameBuildFingerprint.IsKnown=true;

// Failure cases exercise author callbacks and native wrapper failure boundaries.
ResetRun();var guarded=new GameItem();
ItemDataRevision Token(string keyName="guard")=>ItemDataApi.Read<JsonElement>(guarded,owner,keyName).Revision!;
var callbackToken=Token();var writesBefore=guarded.state!.dict.Writes;
Check(ItemDataApi.Stage(guarded,callbackToken,new GetterData(()=>InventoryApi.IsItemDragActive=true)).Status==ItemDataStatus.Conflict&&guarded.state.dict.Writes==writesBefore,"serialization starting a drag cannot bypass precommit guard");InventoryApi.IsItemDragActive=false;
Check(ItemDataApi.Stage(guarded,callbackToken,new GetterData(()=>InventoryFreezeApi.IsAvailable=false)).Status==ItemDataStatus.Conflict&&guarded.state.dict.Writes==writesBefore,"serialization disabling freeze gate cannot authorize writes");InventoryFreezeApi.IsAvailable=true;
Check(ItemDataApi.Stage(guarded,callbackToken,new GetterData(()=>InventoryFreezeApi.Frozen=guarded)).Status==ItemDataStatus.Conflict&&guarded.state.dict.Writes==writesBefore,"serialization freezing item refuses commit");InventoryFreezeApi.Frozen=null;
ItemDataWriteResult? inner=null;
Check(ItemDataApi.Stage(guarded,callbackToken,new GetterData(()=>inner=ItemDataApi.Stage(guarded,callbackToken,new Level(99)))).Status==ItemDataStatus.StagedInMemory&&inner?.Status==ItemDataStatus.Conflict,"recursive stage is refused while outer write can finish once");
var cyclic=new CycleData();cyclic.Next=cyclic;var cycleToken=Token("cycle");writesBefore=guarded.state.dict.Writes;
Check(ItemDataApi.Stage(guarded,cycleToken,cyclic).Status==ItemDataStatus.Invalid&&guarded.state.dict.Writes==writesBefore,"cyclic/deep data is an input error with no writes");
Check(ItemDataApi.Stage(guarded,cycleToken,new {callback=(Action)(()=>{})}).Status==ItemDataStatus.Invalid&&guarded.state.dict.Writes==writesBefore,"unsupported serialized type is invalid with no writes");
var typed=Token("typed");var typedWrite=ItemDataApi.Stage(guarded,typed,new {level="wrong type"});
var typedRead=ItemDataApi.Read<Level>(guarded,owner,"typed");
Check(typedRead.Status==ItemDataStatus.Invalid&&typedRead.Json==typedWrite.Json&&typedRead.Version==1&&typedRead.Revision is null,"type mismatch retains raw JSON/version without granting overwrite token");
var mutationToken=Token("constructor");ItemDataApi.Stage(guarded,mutationToken,new {level=2});
ReaderData.OnRead=()=>guarded.state!.dict[ItemDataApi.TagPrefix+owner+"|constructor"].SetString("{\"version\":1,\"data\":{\"level\":3}}");
Check(ItemDataApi.Read<ReaderData>(guarded,owner,"constructor").Status==ItemDataStatus.Conflict,"deserializer callback changing tag invalidates read token");ReaderData.OnRead=null;

foreach(var invalidJson in new[]{"[]","{\"version\":\"1\",\"data\":{}}","{\"version\":1.5,\"data\":{}}","{\"version\":0,\"data\":{}}","{\"version\":2147483648,\"data\":{}}","{\"version\":true,\"data\":{}}","{\"version\":1,\"data\":null}","{\"version\":1}","{\"data\":{}}"}){
    guarded.state!.GetTag(ItemDataApi.TagPrefix+owner+"|bad").Enable().SetString(invalidJson);
    var invalid=ItemDataApi.Read<JsonElement>(guarded,owner,"bad");
    Check(invalid.Status==ItemDataStatus.Invalid&&invalid.Json==invalidJson&&invalid.Revision is null,"bad envelope preserved: "+invalidJson);
}
var crossItemToken=Token("cross");
Check(ItemDataApi.Stage(new GameItem(),crossItemToken,new Level(1)).Status==ItemDataStatus.Conflict,"revision from another item cannot authorize write");
var movedToken=Token("moved");guarded.parentInventory=new();
Check(ItemDataApi.Stage(guarded,movedToken,new Level(1)).Status==ItemDataStatus.Conflict,"moving item to another inventory expires revision");guarded.parentInventory=null;
var stackedToken=Token("stack");guarded.unitCount=2;
Check(ItemDataApi.Stage(guarded,stackedToken,new Level(1)).Status==ItemDataStatus.Conflict,"stack count changes expire revision");guarded.unitCount=1;
var quotaItem=new GameItem();
for(var i=0;i<ItemDataApi.MaxOwnerKeys;i++)quotaItem.state!.GetTag(ItemDataApi.TagPrefix+owner+"|q"+i).Enable().SetString("{\"version\":1,\"data\":1}");
var quotaToken=ItemDataApi.Read<int>(quotaItem,owner,"q0").Revision!;
Check(ItemDataApi.Stage(quotaItem,quotaToken,2).Status==ItemDataStatus.StagedInMemory,"at key quota existing own key remains replaceable");
quotaToken=ItemDataApi.Read<int>(quotaItem,owner,"new").Revision!;writesBefore=quotaItem.state!.dict.Writes;
Check(ItemDataApi.Stage(quotaItem,quotaToken,2).Status==ItemDataStatus.Invalid&&quotaItem.state.dict.Writes==writesBefore,"new key above owner quota refuses commit");
var byteItem=new GameItem();
for(var i=0;i<5;i++)byteItem.state!.GetTag(ItemDataApi.TagPrefix+owner+"|bytes"+i).Enable().SetString(new string('x',14000));
var byteToken=ItemDataApi.Read<JsonElement>(byteItem,owner,"new").Revision!;writesBefore=byteItem.state!.dict.Writes;
Check(ItemDataApi.Stage(byteItem,byteToken,new Level(1)).Status==ItemDataStatus.Invalid&&byteItem.state.dict.Writes==writesBefore,"aggregate owner bytes limit applies to preserved existing entries");
var writeFail=new GameItem();var failToken=ItemDataApi.Read<JsonElement>(writeFail,owner,"fail").Revision!;writeFail.state!.dict.AfterWrite=()=>throw new Exception("post write");
var failResult=ItemDataApi.Stage(writeFail,failToken,new Level(1));var reconcileResult=ItemDataApi.Read<Level>(writeFail,owner,"fail");Check(failResult.Status==ItemDataStatus.Indeterminate&&reconcileResult.Status==ItemDataStatus.Invalid,"native tag creation failure preserves partial tag and reports indeterminate");
var repeatedCallback=new GameItem{ModifyCallbackCount=2};var repeatedToken=ItemDataApi.Read<JsonElement>(repeatedCallback,owner,"repeated").Revision!;
Check(ItemDataApi.Stage(repeatedCallback,repeatedToken,new Level(12)).Status==ItemDataStatus.Indeterminate&&ItemDataApi.Read<Level>(repeatedCallback,owner,"repeated").Value==new Level(12),"unexpected repeated native callback reports indeterminate and can be reconciled");
var guardToken=Token("availability");InventoryFreezeApi.IsAvailable=false;
Check(ItemDataApi.Stage(guarded,guardToken,new Level(1)).Status==ItemDataStatus.Unavailable,"unavailable inventory freeze gate refuses write");InventoryFreezeApi.IsAvailable=true;
var throwingToken=Token("getter-throw");writesBefore=guarded.state!.dict.Writes;
Check(ItemDataApi.Stage(guarded,throwingToken,new ThrowingGetterData()).Status==ItemDataStatus.Invalid&&guarded.state.dict.Writes==writesBefore,"throwing author serialization getter is invalid without native writes");
var throwingReadToken=Token("reader-throw");ItemDataApi.Stage(guarded,throwingReadToken,new {level=1});
Check(ItemDataApi.Read<ThrowingReaderData>(guarded,owner,"reader-throw").Status==ItemDataStatus.Invalid,"throwing author deserialization constructor is invalid and preserves stored data");
var maximalOwner="a."+new string('b',156)+"cc";var maximalKey=new string('k',120);var maxAddress=ItemDataApi.Read<Level>(guarded,maximalOwner,maximalKey);
Check(maximalOwner.Length==160&&maxAddress.Status==ItemDataStatus.Missing&&ItemDataApi.Stage(guarded,maxAddress.Revision!,new Level(1)).Status==ItemDataStatus.StagedInMemory,"maximum owner and key lengths are accepted");
var totalTagsItem=new GameItem();for(var i=0;i<4097;i++)totalTagsItem.state!.GetTag("foreign."+i);
var totalTagsToken=ItemDataApi.Read<JsonElement>(totalTagsItem,owner,"bounded").Revision!;writesBefore=totalTagsItem.state!.dict.Writes;
Check(ItemDataApi.Stage(totalTagsItem,totalTagsToken,new Level(1)).Status==ItemDataStatus.Invalid&&totalTagsItem.state.dict.Writes==writesBefore,"total native tag inspection limit refuses write");
GameSession.Identity=null;
Check(ItemDataApi.Read<JsonElement>(guarded,owner,"guard").Status==ItemDataStatus.Unavailable&&ItemDataApi.Stage(guarded,guardToken,new Level(1)).Status==ItemDataStatus.Unavailable,"unloaded run refuses native item access");ResetRun();

var report=new{passed=passed.Count,failed=0,cases=passed,scope="real ItemDataApi source with instrumented item/tag/run wrappers; no native persistence or save/reload claim"};
Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-item-data-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));

public sealed record Level(int level);
public sealed class GetterData(Action onGet){public int Value{get{onGet();return 1;}}}
public sealed class CycleData{public CycleData? Next{get;set;}}
public sealed class ReaderData{public static Action? OnRead;public int level{get;set;} public ReaderData(){OnRead?.Invoke();}}
public sealed class ThrowingGetterData{public int Value=>throw new InvalidOperationException("getter");}
public sealed class ThrowingReaderData{public ThrowingReaderData()=>throw new InvalidOperationException("constructor");}

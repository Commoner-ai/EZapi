using EZapi;
using Il2Cpp;
using System.Text.Json;

var passed=0;
void Check(bool yes,string label) { if(!yes) throw new Exception(label); passed++; }
GameItem Reset(bool stolen=false,int heat=0)
{
    GameSession.Run=new(5,"test",1); GameBuildFingerprint.IsKnown=true;
    InventoryFreezeApi.IsAvailable=true; InventoryApi.IsItemDragActive=false;
    return new(){Stolen=stolen,Heat=heat};
}
var item=Reset();
var read=ItemOriginApi.Read(item);
Check(read.Status==ItemOriginStatus.Read && read.Snapshot==new ItemOriginSnapshot(false,null) && item.ChanceReads==0,"ordinary read does not roll detection or request a heat tag");
item=Reset(true,55);Check(ItemOriginApi.Read(item).Snapshot==new ItemOriginSnapshot(true,55) && item.Writes==0,"stolen read preserves native integer percentage");
foreach(var heat in new[]{0,1,50,100})
{
    item=Reset();var staged=ItemOriginApi.StageStolen(item,heat);
    Check(staged.Status==ItemOriginStatus.StagedInMemory && staged.After==new ItemOriginSnapshot(true,heat) && staged.NativeWriteAttempted,"fixed heat from ordinary item including zero");
    Check(item.Syncs==1 && item.Writes==(heat==0?2:1),"fixed zero uses explicit SetHeat after native setup");
    var unchanged=ItemOriginApi.StageStolen(item,heat);Check(unchanged.Status==ItemOriginStatus.Unchanged && !unchanged.NativeWriteAttempted && item.Syncs==1,"same fixed state has no second write");
    item=Reset(true,heat==100?35:100);var old=ItemOriginApi.StageStolen(item,heat);
    Check(old.Status==ItemOriginStatus.StagedInMemory && old.After!.FoundChancePercent==heat && item.Writes==1,"already-stolen factory heat is replaced");
}
item=Reset(true,40);var clean=ItemOriginApi.StageClean(item);
Check(clean.Status==ItemOriginStatus.StagedInMemory && clean.Before!.IsStolen && clean.After==new ItemOriginSnapshot(false,null),"native cleaning read back without stale chance");
Check(ItemOriginApi.StageClean(item).Status==ItemOriginStatus.Unchanged && item.Writes==1,"repeated clean is a no-op");
item=Reset();Check(ItemOriginApi.InitializeNative(item).After==new ItemOriginSnapshot(true,37) && item.Writes==1,"native default remains native instead of fixed zero");
item=Reset();item.NativeLowValue=true;var low=ItemOriginApi.InitializeNative(item);
Check(low.Status==ItemOriginStatus.StagedInMemory && low.After==new ItemOriginSnapshot(false,null) && item.Writes==1,"native low-value ordinary result is returned honestly");
item=Reset(true,29);Check(ItemOriginApi.InitializeNative(item).Status==ItemOriginStatus.Unchanged && item.Heat==29 && item.Writes==0,"native initialize preserves existing stolen heat");
foreach(var heat in new[]{-1,101,int.MinValue,int.MaxValue}) { item=Reset();Check(ItemOriginApi.StageStolen(item,heat).Status==ItemOriginStatus.Invalid && item.Writes==0,"invalid percentage rejected before native write"); }
foreach(var operation in new Func<GameItem,ItemOriginWriteResult>[] {i=>ItemOriginApi.StageStolen(i),ItemOriginApi.StageClean,ItemOriginApi.InitializeNative})
{
    item=Reset(true,33);item.Frozen=true;Check(operation(item).Status==ItemOriginStatus.Frozen && item.Writes==0,"all writes respect freeze");
    item=Reset(true,33);InventoryApi.IsItemDragActive=true;Check(operation(item).Status==ItemOriginStatus.Conflict && item.Writes==0,"all writes respect active drag");
    item=Reset(true,33);InventoryFreezeApi.IsAvailable=false;Check(operation(item).Status==ItemOriginStatus.Unavailable && item.Writes==0,"missing freeze gate denies writes");
    item=Reset(true,33);item.parentInventory=new();item.Owned=false;Check(operation(item).Status==ItemOriginStatus.Conflict && item.Writes==0,"NPC inventory write denied");
}
item=Reset();item.parentInventory=new();Check(ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.StagedInMemory,"player-owned inventory accepted");
item=Reset();item.Owned=false;Check(ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.StagedInMemory,"caller-owned detached unowned factory result accepted");
item=Reset();GameBuildFingerprint.IsKnown=false;Check(ItemOriginApi.Read(item).Status==ItemOriginStatus.Unavailable && ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.Unavailable && item.Writes==0,"unknown build denied");
item=Reset();GameSession.Run=null;Check(ItemOriginApi.Read(item).Status==ItemOriginStatus.Unavailable && ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.Unavailable && item.Writes==0,"unloaded run denied");
item=Reset();var worker=Task.Run(()=> (ItemOriginApi.Read(item),ItemOriginApi.StageStolen(item))).Result;Check(worker.Item1.Status==ItemOriginStatus.Unavailable && worker.Item2.Status==ItemOriginStatus.Unavailable && item.Writes==0,"worker thread never calls native write");
foreach(var invalidate in new Action<GameItem>[] {i=>i.Pointer=IntPtr.Zero,i=>i.unitCount=0,i=>i.state=null,i=>i.state!.dict=null})
{
    item=Reset();invalidate(item);Check(ItemOriginApi.Read(item).Status==ItemOriginStatus.Unavailable && ItemOriginApi.StageStolen(item).Status!=ItemOriginStatus.StagedInMemory && item.Writes==0,"invalid native item state denied");
}
foreach(var heat in new[]{-1,101}) { item=Reset(true,heat);Check(ItemOriginApi.Read(item).Status==ItemOriginStatus.Unavailable && ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.Unavailable && item.Writes==0,"bad third-party heat not clamped or overwritten"); }
item=Reset(true,33);item.OnStolenRead=()=>GameSession.Run=new(5,"test",2);
Check(ItemOriginApi.Read(item).Status==ItemOriginStatus.Unavailable && item.ChanceReads==0,"reload during read stops before old-wrapper chance call");
item=Reset(true,33);item.OnChanceRead=()=>item.Stolen=false;Check(ItemOriginApi.Read(item).Status==ItemOriginStatus.Unavailable,"inconsistent native origin during read is not returned as valid snapshot");
foreach(var mutate in new Action<GameItem>[] {i=>GameSession.Run=new(5,"test",2),i=>i.uniqueId++,i=>i.identifier="different",i=>i.unitCount++,i=>i.parentInventory=new(){Pointer=new(2)},i=>i.state=new(){Pointer=new(2)},i=>i.state!.dict=new(){Pointer=new(2)}})
{
    item=Reset();item.OnWrite=()=>mutate(item);var uncertain=ItemOriginApi.StageStolen(item,0);
    Check(uncertain.Status==ItemOriginStatus.Indeterminate && uncertain.NativeWriteAttempted && uncertain.After is null && item.Writes==1 && item.Syncs==0,"mid-write identity change halts fallback and sync");
}
item=Reset();item.OnWrite=()=>item.Frozen=true;Check(ItemOriginApi.StageStolen(item,0).Status==ItemOriginStatus.Indeterminate && item.Writes==1 && item.Syncs==0,"freeze acquired during init prevents follow-up write");
item=Reset();item.OnWrite=()=>throw new Exception("sensitive");var partial=ItemOriginApi.StageStolen(item);
Check(partial.Status==ItemOriginStatus.Indeterminate && partial.NativeWriteAttempted && !partial.Reason!.Contains("sensitive") && item.Writes==1,"throw after partial native write is uncertain without leaking exception details");
item.OnWrite=null;Check(ItemOriginApi.Read(item).Snapshot!.IsStolen && ItemOriginApi.StageClean(item).Status==ItemOriginStatus.StagedInMemory,"exception releases lock; explicit reconciliation possible");
item=Reset();item.OnWrite=()=>item.Stolen=false;Check(ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.Indeterminate && item.Writes==2,"native refusal never reports false success or loops retries");
item=Reset();item.OnSync=()=>item.Heat=75;Check(ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.Indeterminate && item.Writes==1 && item.Syncs==1,"readback detects sync callback rewrite");
item=Reset();item.OnSync=()=>GameSession.Run=new(5,"test",2);Check(ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.Indeterminate,"reload during sync never reports confirmed write");
item=Reset();ItemOriginWriteResult? nested=null;item.OnWrite=()=>nested=ItemOriginApi.StageClean(item);
Check(ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.StagedInMemory && nested!.Status==ItemOriginStatus.Conflict && item.Writes==1,"recursive write refused without unlocking outer operation");
item=Reset();item.OnStolenRead=()=>nested=ItemOriginApi.StageClean(item);
Check(ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.StagedInMemory && nested!.Status==ItemOriginStatus.Conflict,"lock held before native read callbacks");
item=Reset();item.OnFreeze=()=>throw new Exception("gate");Check(ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.Unavailable && item.Writes==0,"preflight exception reports no attempted write");
item=Reset(true,100);var freezeCalls=0;item.OnFreeze=()=>{if(++freezeCalls==2)item.Heat=80;};
Check(ItemOriginApi.StageStolen(item).Status==ItemOriginStatus.Conflict && item.Writes==0,"changed heat during preflight cannot return stale Unchanged");
item=Reset();freezeCalls=0;item.OnFreeze=()=>{if(++freezeCalls==2)item.Stolen=true;};
Check(ItemOriginApi.StageClean(item).Status==ItemOriginStatus.Conflict && item.Writes==0,"changed clean status during preflight cannot return stale Unchanged");
Console.WriteLine(JsonSerializer.Serialize(new{passed,failed=0,scope="real ItemOriginApi source with instrumented native/run/freeze stubs; no native or disk persistence claim"}));

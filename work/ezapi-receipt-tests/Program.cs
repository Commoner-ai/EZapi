using EZapi;
using Il2Cpp;
using System.Text.Json;

var passed = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); passed++; }
const string owner = "author.delivery", key = "receipts";
var run = new GameRunIdentity(560, "test-run", 5);
var receipt = new DeliveryReceipt("parcel-01", "base.item");
NativeInventoryItem Native(long pointer, int id, string itemId = "base.item", int units = 1, bool owned = true) => new(pointer, id, itemId, units, owned, null);
DeliveryReceiptItem Entry(long pointer, int id, DeliveryReceipt? token = null, long parent = 900) => new(Native(pointer, id), parent, token);
DeliveryReceiptSnapshot Scan(params DeliveryReceiptItem[] items) => new(DeliveryReceiptCaptureStatus.Ready, run, owner, key, items);
var empty = Scan(); var received = Entry(100, -2, receipt); var full = Scan(received);
var stamp = new DeliveryReceiptStamp(run, owner, key, received.Item, receipt);
DeliveryReceiptResult Reconcile(DeliveryReceiptSnapshot s, DeliveryLedgerState state = DeliveryLedgerState.InFlight) => DeliveryReceiptAudit.Reconcile(s, receipt, state);
DeliveryReceiptResult Handoff(DeliveryReceiptSnapshot a, DeliveryReceiptSnapshot b) => DeliveryReceiptAudit.CheckHandoff(a, b, stamp);
Check(Reconcile(empty, DeliveryLedgerState.Pending).Decision == DeliveryReceiptDecision.CanAttempt, "only never-attempted ledger plus complete absence can preflight");
Check(Reconcile(empty).Decision == DeliveryReceiptDecision.Indeterminate, "inflight absence never authorizes resend");
Check(Reconcile(full).Decision == DeliveryReceiptDecision.Observed, "single owned exact receipt observed");
Check(Reconcile(full, DeliveryLedgerState.Pending).Decision == DeliveryReceiptDecision.Observed, "pending ledger with existing receipt must not issue");
Check(Reconcile(empty, DeliveryLedgerState.Delivered).Decision == DeliveryReceiptDecision.Recorded, "sold or consumed delivered gift not reissued");
Check(Reconcile(null!, DeliveryLedgerState.Delivered).Decision == DeliveryReceiptDecision.Recorded, "completed ledger prevents reward even without a scan");
Check(Reconcile(empty, (DeliveryLedgerState)99).Decision == DeliveryReceiptDecision.Invalid, "invalid ledger state refused");
Check(Handoff(empty, full).Decision == DeliveryReceiptDecision.Observed, "exact fresh handoff includes negative native instance ID");
Check(DeliveryReceiptAudit.CheckHandoff(empty, full, stamp, 900).Decision == DeliveryReceiptDecision.Observed, "exact target matches");
Check(DeliveryReceiptAudit.CheckHandoff(empty, full, stamp, 901).Decision == DeliveryReceiptDecision.Indeterminate, "fallback inventory does not match exact requested target");
Check(Handoff(empty, empty).Decision == DeliveryReceiptDecision.Indeterminate, "native reject/destroy cannot be called safe-to-retry");
Check(Handoff(full, full).Decision == DeliveryReceiptDecision.Conflict, "before contains token identity");
Check(Handoff(Scan(Entry(101, -2)), full).Decision == DeliveryReceiptDecision.Conflict, "before instance identity collision");
foreach (var changed in new[] { received with { ParentIdentity = 0 }, received with { Item = received.Item with { PlayerOwned = false } },
    received with { Item = received.Item with { ItemId = "base.other" } }, received with { Item = received.Item with { Units = 2 } },
    received with { Receipt = receipt with { ItemId = "base.other" } }, received with { Receipt = receipt with { Units = 2 } } })
    Check(Reconcile(Scan(changed)).Decision == DeliveryReceiptDecision.Conflict, "wrong result ownership/parent/id/count/payload conflicts");
Check(Reconcile(Scan(received, Entry(101, 3, receipt))).Decision == DeliveryReceiptDecision.Conflict, "duplicate receipt never aggregated");
Check(Reconcile(Scan(received with { Receipt = receipt with { OperationId = "PARCEL-01" } })).Decision == DeliveryReceiptDecision.Indeterminate, "case-sensitive operation IDs");
foreach (var changed in new[] { full with { Run = run with { Epoch = 6 } }, full with { Run = run with { SaveSlotId = 561 } },
    full with { Run = run with { RunId = "other" } }, full with { OwnerId = "author.other" }, full with { Key = "other" } })
    Check(Handoff(empty, changed).Decision == DeliveryReceiptDecision.Indeterminate, "before/after cannot cross load/slot/run/address");
var older = Entry(10, 0); // unique IDs may be zero.
Check(Handoff(Scan(older), Scan(received, older)).Decision == DeliveryReceiptDecision.Observed, "pre-existing records compared independent of ordering");
foreach (var changed in new[] { older with { ParentIdentity = 901 }, older with { Item = older.Item with { Units = 2 } },
    older with { Item = older.Item with { PlayerOwned = false } }, older with { Item = older.Item with { InstanceId = 8 } },
    older with { Item = older.Item with { ItemId = "base.other" } }, older with { Receipt = new("other-op", "base.item") } })
    Check(Handoff(Scan(older), Scan(received, changed)).Decision == DeliveryReceiptDecision.Indeterminate, "unrelated observed mutation prevents exact handoff confirmation");
Check(Handoff(Scan(older), full).Decision == DeliveryReceiptDecision.Indeterminate, "old inventory removal refused");
Check(Handoff(empty, Scan(received, older)).Decision == DeliveryReceiptDecision.Indeterminate, "extra new item refused");
Check(Handoff(empty, Scan(received with { Item = received.Item with { NativeIdentity = 101 } })).Decision == DeliveryReceiptDecision.Indeterminate, "token on copied wrapper is not exact handoff identity");
var coldLoad = full with { Run = run with { Epoch = 99 }, Items = new[] { received with { Item = received.Item with { NativeIdentity = 999, InstanceId = 999 } } } };
Check(Reconcile(coldLoad).Decision == DeliveryReceiptDecision.Observed, "stable operation reconciliation survives reload without old native identity");
foreach (var broken in new[] { full with { Status = DeliveryReceiptCaptureStatus.Invalid }, full with { Status = DeliveryReceiptCaptureStatus.Unavailable }, full with { Status = DeliveryReceiptCaptureStatus.LimitExceeded } })
    Check(Reconcile(broken, DeliveryLedgerState.Pending).Decision == DeliveryReceiptDecision.Indeterminate, "failed scan is never empty successful evidence");
foreach (var broken in new[] { full with { Run = null }, full with { Run = run with { Epoch = 0 } }, full with { OwnerId = "bad/owner" },
    full with { Key = "bad/key" }, full with { Items = null! }, Scan(received, received), Scan(received, Entry(101, -2)),
    Scan(received with { Item = received.Item with { NativeIdentity = 0 } }), Scan(received with { Item = received.Item with { Units = 0 } }),
    Scan(received with { ParentIdentity = 100 }), Scan(null!), Scan(received with { Receipt = receipt with { OperationId = "bad\n" } }) })
    Check(Reconcile(broken).Decision == DeliveryReceiptDecision.Invalid, "malformed snapshots fail closed");
foreach (var bad in new[] { receipt with { OperationId = "" }, receipt with { OperationId = new string('a', 129) }, receipt with { Units = 0 },
    receipt with { Units = -1 }, receipt with { ItemId = "" }, receipt with { ItemId = "bad\n" }, receipt with { ItemId = new string('a',161) } })
    Check(DeliveryReceiptAudit.Reconcile(empty, bad, DeliveryLedgerState.Pending).Decision == DeliveryReceiptDecision.Invalid, "invalid receipt refused before decision");
Check(Reconcile(Scan(Enumerable.Range(1,4097).Select(i => Entry(i,i)).ToArray())).Decision == DeliveryReceiptDecision.Invalid, "oversized public snapshots refused");

GameItem Reset()
{
    GameSession.OnThread = true; GameSession.Identity = run; PlayerStore.instance = new();
    GameBuildFingerprint.IsKnown = true; InventoryFreezeApi.IsAvailable = true; InventoryFreezeApi.Frozen = null;
    InventoryApi.IsItemDragActive = false; InventoryApi.Items = new(); InventoryApi.OnCapture = null; InventoryApi.Captures = 0;
    return new() { identifier = receipt.ItemId, unitCount = receipt.Units, Owned = true };
}
DeliveryReceiptStampResult Stamp(GameItem item) => ItemReceiptApi.StampDetached(item, owner, key, receipt);
var item = Reset(); var result = Stamp(item);
Check(result.Status == ItemDataStatus.StagedInMemory && result.Stamp?.Receipt == receipt && item.Modifications == 1, "real ItemDataApi stamps official tag envelope once");
Check(Stamp(item).Status == ItemDataStatus.Present && item.Modifications == 1, "same detached stamp is a read-only no-op");
Check(ItemReceiptApi.StampDetached(item, owner, key, receipt with { OperationId = "different" }).Status == ItemDataStatus.Conflict && item.Modifications == 1, "existing stamp preserved");
var managedStamp = result.Stamp!;
var beforeScan = ItemReceiptApi.Capture(owner,key);
item.parentInventory = new() { Pointer = new(900) }; InventoryApi.Items.Add(item);
var afterScan = ItemReceiptApi.Capture(owner,key);
Check(beforeScan.Status == DeliveryReceiptCaptureStatus.Ready && afterScan.Status == DeliveryReceiptCaptureStatus.Ready && InventoryApi.Captures == 4, "fresh two-pass scans, no cached items");
item.Dead = true;
Check(DeliveryReceiptAudit.CheckHandoff(beforeScan,afterScan,managedStamp,900).Decision == DeliveryReceiptDecision.Observed, "managed post-handoff audit never touches destroyed original wrapper");
foreach (var mutate in new Action<GameItem>[] { i=>i.parentInventory=new(), i=>i.Owned=false, i=>i.unitCount=2, i=>i.identifier="other" })
{ item=Reset(); mutate(item); Check(Stamp(item).Status == ItemDataStatus.Conflict && item.Modifications == 0, "bad detached factory shape/ownership blocked"); }
item=Reset(); InventoryFreezeApi.Frozen=item; Check(Stamp(item).Status == ItemDataStatus.Frozen && item.Modifications == 0, "freeze gate");
item=Reset(); InventoryApi.IsItemDragActive=true; Check(Stamp(item).Status == ItemDataStatus.Conflict && item.Modifications == 0, "drag gate");
item=Reset(); InventoryFreezeApi.IsAvailable=false; Check(Stamp(item).Status == ItemDataStatus.Unavailable && item.Modifications == 0, "missing freeze infrastructure refuses write");
item=Reset(); GameBuildFingerprint.IsKnown=false; Check(Stamp(item).Status == ItemDataStatus.Unavailable && ItemReceiptApi.Capture(owner,key).Status == DeliveryReceiptCaptureStatus.Unavailable, "unknown build gates both native APIs");
item=Reset(); GameSession.Identity=null; Check(Stamp(item).Status == ItemDataStatus.Unavailable && ItemReceiptApi.Capture(owner,key).Status == DeliveryReceiptCaptureStatus.Unavailable, "no run gates both native APIs");
item=Reset(); GameSession.OnThread=false; Check(Stamp(item).Status == ItemDataStatus.Disabled && ItemReceiptApi.Capture(owner,key).Status == DeliveryReceiptCaptureStatus.Unavailable && InventoryApi.Captures == 0, "wrong thread has no native scan or writes");
item=Reset(); Check(ItemReceiptApi.StampDetached(item,"bad/owner",key,receipt).Status == ItemDataStatus.Invalid && ItemReceiptApi.Capture(owner,"bad/key").Status == DeliveryReceiptCaptureStatus.Invalid, "address validation");
item=Reset(); item.ThrowSync=true; Check(Stamp(item).Status == ItemDataStatus.Indeterminate && item.Modifications == 1, "sync exception cannot be reported as unstamped");
item=Reset(); item.OnSync=()=>item.parentInventory=new(); Check(Stamp(item).Status == ItemDataStatus.Indeterminate, "parent mutation during tag staging");
item=Reset(); item.OnSync=()=>GameSession.Identity=run with { Epoch = 6 }; Check(Stamp(item).Status == ItemDataStatus.Indeterminate, "load mutation during staging");
item=Reset(); DeliveryReceiptStampResult? recursive=null; item.OnSync=()=>recursive=Stamp(item); Check(Stamp(item).Status == ItemDataStatus.StagedInMemory && recursive?.Status == ItemDataStatus.Conflict && item.Modifications == 1, "recursive stamp refused without poisoning outer success");
item=Reset(); item.ReturnEmptyReadonlyTagForMissing=true; InventoryApi.Items.Add(item); Check(ItemReceiptApi.Capture(owner,key).Status == DeliveryReceiptCaptureStatus.Ready, "native missing-tag sentinel yields no receipt");
item=Reset(); InventoryApi.Items.Add(item); item.state!.GetTag(ItemDataApi.TagPrefix+owner+"|"+key).Enable().SetString("broken");
Check(ItemReceiptApi.Capture(owner,key) is { Status: DeliveryReceiptCaptureStatus.Invalid, Items.Count:0 }, "malformed stored data yields no partial scan");
item=Reset(); Stamp(item); InventoryApi.Items.Add(item); item.OnRead=()=> { item.Owned=false; };
Check(ItemReceiptApi.Capture(owner,key).Status == DeliveryReceiptCaptureStatus.Unavailable, "intra-item ownership change invalidates scan");
item=Reset(); Stamp(item); InventoryApi.Items.Add(item); InventoryApi.OnCapture=()=> { if (InventoryApi.Captures==2) item.unitCount=2; };
Check(ItemReceiptApi.Capture(owner,key).Status == DeliveryReceiptCaptureStatus.Unavailable, "between-pass mutation invalidates scan");
item=Reset(); InventoryApi.Items.Add(item); InventoryApi.OnCapture=()=> { if (InventoryApi.Captures==2) GameSession.Identity=run with { Epoch=6 }; };
Check(ItemReceiptApi.Capture(owner,key).Status == DeliveryReceiptCaptureStatus.Unavailable, "between-pass reload invalidates scan");
item=Reset(); DeliveryReceiptSnapshot? nested=null; InventoryApi.OnCapture=()=>nested=ItemReceiptApi.Capture(owner,key);
Check(ItemReceiptApi.Capture(owner,key).Status == DeliveryReceiptCaptureStatus.Ready && nested?.Status == DeliveryReceiptCaptureStatus.Unavailable, "recursive scan refused and reentry gate restored");
item=Reset(); InventoryApi.Items=Enumerable.Range(0,4097).Select(_=>new GameItem()).ToList();
Check(ItemReceiptApi.Capture(owner,key) is { Status: DeliveryReceiptCaptureStatus.LimitExceeded, Items.Count:0 }, "item cap has no truncated evidence");
item=Reset(); var envelope="{\"version\":1,\"data\":"+JsonSerializer.Serialize(receipt)+new string(' ',15000)+"}";
for(var i=0;i<71;i++) { var entry=new GameItem(); entry.state!.GetTag(ItemDataApi.TagPrefix+owner+"|"+key).Enable().SetString(envelope); InventoryApi.Items.Add(entry); }
Check(ItemReceiptApi.Capture(owner,key) is { Status: DeliveryReceiptCaptureStatus.LimitExceeded, Items.Count:0 }, "aggregate receipt JSON budget has no truncated evidence");
item=Reset(); Check(ItemReceiptApi.Capture(owner,key).Status == DeliveryReceiptCaptureStatus.Ready && Stamp(item).Status == ItemDataStatus.StagedInMemory, "all exceptional branches release reentry gate");
// Exercise the distributed consumer, including exactly-once native boundary and lifetime discipline.
(GameItem Item, GameGridInventory Grid, SlotMarker Marker) Example()
{
    var gift=Reset(); var grid=new GameGridInventory(); var marker=new SlotMarker{item=gift,inventory=grid};grid.Marker=marker;
    marker.OnAccept=()=>{gift.parentInventory=grid;InventoryApi.Items.Add(gift);};
    return (gift,grid,marker);
}
var fixture=Example(); var stages=0;
var attempt=Author.Deliveries.ReceiptDelivery.AttemptOnce(fixture.Item,fixture.Grid,receipt,DeliveryLedgerState.Pending,()=>{stages++;return true;});
Check(attempt.NativeHandoffAttempted && attempt.Audit.Decision==DeliveryReceiptDecision.Observed && fixture.Marker.Calls==1 && stages==1,"consumer stages ledger and calls native once");
foreach(var block in new Action<GameGridInventory,SlotMarker>[] {(g,m)=>g.Locked=true,(g,m)=>g.MayInsert=false,(g,m)=>g.MayOwn=false,(g,m)=>m.Valid=false,(g,m)=>m.targetItem=new(),(g,m)=>m.numTransfer=0})
{
    fixture=Example();block(fixture.Grid,fixture.Marker);
    attempt=Author.Deliveries.ReceiptDelivery.AttemptOnce(fixture.Item,fixture.Grid,receipt,DeliveryLedgerState.Pending,()=>throw new Exception("must not stage"));
    Check(!attempt.NativeHandoffAttempted && fixture.Marker.Calls==0,"consumer respects native locks, ownership, whole slot and no merging");
}
fixture=Example(); attempt=Author.Deliveries.ReceiptDelivery.AttemptOnce(fixture.Item,fixture.Grid,receipt,DeliveryLedgerState.InFlight,()=>throw new Exception());
Check(!attempt.NativeHandoffAttempted && fixture.Marker.Calls==0,"consumer never repeats inflight absence");
fixture=Example(); attempt=Author.Deliveries.ReceiptDelivery.AttemptOnce(fixture.Item,fixture.Grid,receipt,DeliveryLedgerState.Pending,()=>false);
Check(!attempt.NativeHandoffAttempted && fixture.Marker.Calls==0,"consumer refuses unconfirmed ledger stage");
fixture=Example(); attempt=Author.Deliveries.ReceiptDelivery.AttemptOnce(fixture.Item,fixture.Grid,receipt,DeliveryLedgerState.Pending,()=>throw new Exception());
Check(!attempt.NativeHandoffAttempted && fixture.Marker.Calls==0,"consumer stops after partial/throwing ledger stage");
fixture=Example(); var changedGift=fixture.Item;
attempt=Author.Deliveries.ReceiptDelivery.AttemptOnce(fixture.Item,fixture.Grid,receipt,DeliveryLedgerState.Pending,()=>{changedGift.unitCount=2;return true;});
Check(!attempt.NativeHandoffAttempted && fixture.Marker.Calls==0,"consumer rechecks detached state after ledger callback");
fixture=Example(); fixture.Marker.OnAccept=()=>throw new Exception("native");
attempt=Author.Deliveries.ReceiptDelivery.AttemptOnce(fixture.Item,fixture.Grid,receipt,DeliveryLedgerState.Pending,()=>true);
Check(attempt.NativeHandoffAttempted && fixture.Marker.Calls==1 && attempt.Audit.Decision==DeliveryReceiptDecision.Indeterminate && attempt.NativeError=="Exception","native exception never triggers a second call");
fixture=Example();var delivered=fixture;fixture.Marker.OnAccept=()=>{delivered.Item.parentInventory=delivered.Grid;InventoryApi.Items.Add(delivered.Item);throw new Exception();};
attempt=Author.Deliveries.ReceiptDelivery.AttemptOnce(fixture.Item,fixture.Grid,receipt,DeliveryLedgerState.Pending,()=>true);
Check(attempt.NativeHandoffAttempted && attempt.Audit.Decision==DeliveryReceiptDecision.Observed && attempt.NativeError=="Exception","fresh admission can be observed despite post-admission native exception");
fixture=Example();var destroyed=fixture.Item;fixture.Marker.OnAccept=()=>{destroyed.Dead=true;};
attempt=Author.Deliveries.ReceiptDelivery.AttemptOnce(fixture.Item,fixture.Grid,receipt,DeliveryLedgerState.Pending,()=>true);
Check(attempt.NativeHandoffAttempted && fixture.Marker.Calls==1 && attempt.Audit.Decision==DeliveryReceiptDecision.Indeterminate,"native destruction leaves no old-wrapper reads or auto cleanup");
Console.WriteLine(JsonSerializer.Serialize(new { passed, failed=0, productionSources=true, nativeBoundaries="explicit stand-ins; not native game, save/reload or handoff acceptance" }));

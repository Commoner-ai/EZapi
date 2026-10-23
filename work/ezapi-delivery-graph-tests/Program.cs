using EZapi;
using Il2Cpp;
using System.Text.Json;
var passed=0;
void Check(bool value,string label){if(!value)throw new Exception(label);passed++;}
const string owner="author.mod",key="receipts";
var run=new GameRunIdentity(560,"test",7);var receipt=new DeliveryReceipt("box-01","sec_box");
DeliveryGraphNode Node(long id,bool inv,long[]? parents=null,long[]? children=null)=>new(id,inv,parents??Array.Empty<long>(),children??Array.Empty<long>());
DeliveryReceiptItem Item(long id,int instance,string name="sec_box",long parent=0,bool owned=true,DeliveryReceipt? token=null)=>new(new(id,instance,name,1,owned,null),parent,token);
DeliveryGraphSnapshot Graph(long root,DeliveryGraphNode[] nodes,params DeliveryReceiptItem[] items)=>new(DeliveryReceiptCaptureStatus.Ready,run,owner,key,root,nodes,items);
var before=Graph(0,new[]{Node(900,true),Node(901,true,children:new[]{10L}),Node(10,false,new[]{901L})},Item(10,1,"base.old",901));
var detached=Graph(100,new[]{Node(100,false,children:new[]{200L}),Node(200,true,new[]{100L},new[]{300L,301L}),Node(300,false,new[]{200L}),Node(301,false,new[]{200L})},
    Item(100,2,token:receipt),Item(300,0,"base.one",200,false),Item(301,0,"base.two",200,false));
var after=Graph(0,new[]{Node(900,true,children:new[]{100L}),before.Nodes[1],before.Nodes[2],Node(100,false,new[]{900L},new[]{200L}),detached.Nodes[1],detached.Nodes[2],detached.Nodes[3]},
    before.Items[0],detached.Items[0] with {ParentIdentity=900},detached.Items[1],detached.Items[2]);
DeliveryReceiptResult Audit(DeliveryGraphSnapshot? b=null,DeliveryGraphSnapshot? a=null,DeliveryGraphSnapshot? d=null,bool initialize=false)=>DeliveryGraphAudit.CheckHandoff(b??before,a??after,d??detached,900,initialize);
Check(Audit().Decision==DeliveryReceiptDecision.Observed,"whole filled crate graph observed including unowned children ID0");
Check(DeliveryReceiptAudit.CheckHandoff(new(DeliveryReceiptCaptureStatus.Ready,run,owner,key,before.Items),new(DeliveryReceiptCaptureStatus.Ready,run,owner,key,after.Items),new(run,owner,key,detached.Items[0].Item,receipt)).Decision!=DeliveryReceiptDecision.Observed,"old single-addition audit intentionally cannot confirm populated crate");
Check(DeliveryGraphAudit.Reconcile(before,receipt,DeliveryLedgerState.Pending).Decision==DeliveryReceiptDecision.CanAttempt,"complete pending absence preflight");
Check(DeliveryGraphAudit.Reconcile(before,receipt,DeliveryLedgerState.InFlight).Decision==DeliveryReceiptDecision.Indeterminate,"inflight absence cannot reissue");
Check(DeliveryGraphAudit.Reconcile(null!,receipt,DeliveryLedgerState.Delivered).Decision==DeliveryReceiptDecision.Recorded,"completed caller ledger survives consumed/sold/absent inventory");
Check(DeliveryGraphAudit.Reconcile(after,receipt,DeliveryLedgerState.InFlight).Decision==DeliveryReceiptDecision.Observed,"recovery sees exact root receipt");
Check(DeliveryGraphAudit.SameObservation(after,after with {Nodes=after.Nodes.Reverse().Select(n=>n with {Children=n.Children.Reverse().ToArray(),Parents=n.Parents.Reverse().ToArray()}).ToArray(),Items=after.Items.Reverse().ToArray()}),"snapshot comparison independent of enumeration ordering");
Check(!DeliveryGraphAudit.SameObservation(after,after with {Status=DeliveryReceiptCaptureStatus.Unavailable}),"failed scan never equals itself");
foreach(var changed in new[]{after with {Run=run with {Epoch=8}},after with {Run=run with {SaveSlotId=561}},after with {Run=run with {RunId="other"}},after with {OwnerId="other.mod"},after with {Key="other"}})
 Check(Audit(a:changed).Decision==DeliveryReceiptDecision.Indeterminate,"same epoch/slot/run/address required");
Check(DeliveryGraphAudit.CheckHandoff(before,after,detached,901).Decision==DeliveryReceiptDecision.Indeterminate,"exact destination mismatch refused");
Check(DeliveryGraphAudit.CheckHandoff(before,after,detached,null).Decision==DeliveryReceiptDecision.Observed,"optional destination accepts actual existing inventory");
Check(DeliveryGraphAudit.CheckHandoff(before,after,detached,0).Decision==DeliveryReceiptDecision.Invalid,"zero target not a wildcard");
foreach(var index in new[]{1,2})foreach(var change in new Func<DeliveryReceiptItem,DeliveryReceiptItem>[] {
 i=>i with {Item=i.Item with {ItemId="changed"}},i=>i with {Item=i.Item with {Units=2}},i=>i with {Receipt=new("different","base.one")},i=>i with {Item=i.Item with {InstanceId=42}}})
{
 var array=after.Items.ToArray();array[index+1]=change(array[index+1]);Check(Audit(a:after with {Items=array}).Decision!=DeliveryReceiptDecision.Observed,"child ID/units/receipt/instance mutation rejected");
}
var initialized=after with {Items=after.Items.Select(i=>i.Item.NativeIdentity>=300?i with {Item=i.Item with {PlayerOwned=true,InstanceId=(int)i.Item.NativeIdentity}}:i).ToArray()};
Check(Audit(a:initialized).Decision==DeliveryReceiptDecision.Indeterminate,"child ownership initialization requires explicit opt-in");
Check(Audit(a:initialized,initialize:true).Decision==DeliveryReceiptDecision.Observed,"explicit child ownership initialization permits assigned unique IDs");
var changedOwned=after with {Items=after.Items.Select(i=>i.Item.NativeIdentity==100?i with {Item=i.Item with {PlayerOwned=false}}:i).ToArray()};
Check(Audit(a:changedOwned,initialize:true).Decision!=DeliveryReceiptDecision.Observed,"option never permits root ownership downgrade");
var badUnits=initialized with {Items=initialized.Items.Select(i=>i.Item.NativeIdentity==300?i with {Item=i.Item with {Units=3}}:i).ToArray()};
Check(Audit(a:badUnits,initialize:true).Decision==DeliveryReceiptDecision.Indeterminate,"ownership option cannot hide content mutation");
Check(Audit(a:initialized with {Items=initialized.Items.Select(i=>i.Item.NativeIdentity==300?i with {Item=i.Item with {InstanceId=2}}:i).ToArray()},initialize:true).Decision==DeliveryReceiptDecision.Invalid,"initialized IDs cannot collide with root");
Check(Audit(a:after with {Items=after.Items.Where(i=>i.Item.NativeIdentity!=301).ToArray()}).Decision!=DeliveryReceiptDecision.Observed,"missing child item record not accepted as delivered");
Check(Audit(a:after with {Items=after.Items.Append(Item(500,500,"extra")).ToArray(),Nodes=after.Nodes.Append(Node(500,false)).ToArray()}).Decision==DeliveryReceiptDecision.Indeterminate,"extra new item refused");
Check(Audit(a:after with {Items=after.Items.Select(i=>i.Item.NativeIdentity==10?i with {Item=i.Item with {Units=2}}:i).ToArray()}).Decision==DeliveryReceiptDecision.Indeterminate,"pre-existing unrelated inventory item mutation refused");
Check(Audit(b:before with {Nodes=before.Nodes.Concat(detached.Nodes).ToArray(),Items=before.Items.Concat(detached.Items).ToArray()}).Decision==DeliveryReceiptDecision.Conflict,"detached graph cannot borrow existing node identity");
Check(Audit(b:before with {Items=new[]{before.Items[0] with {Receipt=receipt}}}).Decision==DeliveryReceiptDecision.Conflict,"before already holds operation token");
Check(DeliveryGraphAudit.Reconcile(after with {Items=after.Items.Select(i=>i.Item.NativeIdentity==300?i with {Receipt=receipt}:i).ToArray()},receipt,DeliveryLedgerState.InFlight).Decision==DeliveryReceiptDecision.Conflict,"duplicate root token anywhere in child graph conflicts");
foreach(var broken in new[]{after with {Nodes=null!},after with {Items=null!},after with {RootIdentity=100},after with {Nodes=after.Nodes.Append(after.Nodes[0]).ToArray()},
 after with {Items=after.Items.Append(after.Items[0]).ToArray()},after with {Nodes=after.Nodes.Select(n=>n.NativeIdentity==900?n with {Children=new[]{100L,100L}}:n).ToArray()},
 after with {Nodes=after.Nodes.Select(n=>n.NativeIdentity==900?n with {Children=new[]{99999L}}:n).ToArray()},
 after with {Nodes=after.Nodes.Select(n=>n.NativeIdentity==900?n with {Children=Array.Empty<long>()}:n).ToArray()},
 after with {Items=after.Items.Select(i=>i.Item.NativeIdentity==100?i with {ParentIdentity=123}:i).ToArray()},
 after with {Nodes=after.Nodes.Select(n=>n.NativeIdentity==200?n with {Children=n.Children.Append(200).ToArray()}:n).ToArray()},
 after with {Run=null},after with {Items=after.Items.Select(i=>i.Item.NativeIdentity==100?i with {Item=i.Item with {Units=0}}:i).ToArray()}})
 Check(Audit(a:broken).Decision==DeliveryReceiptDecision.Invalid,"malformed graph evidence refused");
foreach(var broken in new[]{detached with {Nodes=detached.Nodes.Append(Node(999,true)).ToArray()},detached with {Nodes=detached.Nodes.Select(n=>n.NativeIdentity==100?n with {Parents=new[]{999L}}:n).ToArray()},
 detached with {Items=detached.Items.Select(i=>i.Item.NativeIdentity==100?i with {Receipt=null}:i).ToArray()},detached with {RootIdentity=999},detached with {RootIdentity=200}})
 Check(Audit(d:broken).Decision==DeliveryReceiptDecision.Invalid,"detached must be exactly one closed receipt-bearing root graph");
var cycle=Graph(100,new[]{Node(100,false,children:new[]{200L}),Node(200,true,new[]{100L,300L},new[]{300L}),Node(300,false,new[]{200L},new[]{200L})},Item(100,2,token:receipt),Item(300,3,"child",200));
Check(Audit(d:cycle).Decision==DeliveryReceiptDecision.Invalid,"reciprocal cycle rejected without recursion");
Check(Audit(d:detached with {Nodes=Enumerable.Range(1,8193).Select(i=>Node(i,false)).ToArray()}).Decision==DeliveryReceiptDecision.Invalid,"node budget no partial success");
Check(Audit(d:detached with {Nodes=new[]{Node(100,false,children:Enumerable.Range(1,16385).Select(i=>(long)i).ToArray())}}).Decision==DeliveryReceiptDecision.Invalid,"reference budget before copying unbounded lists");
// Model many valid filled rewards, then corrupt exactly one child at a time.
for(var length=1;length<=40;length++)
{
 var dn=new List<DeliveryGraphNode>{Node(100,false,children:new[]{200L}),Node(200,true,new[]{100L},Enumerable.Range(0,length).Select(i=>300L+i).ToArray())};
 var di=new List<DeliveryReceiptItem>{Item(100,2,token:receipt)};
 for(var i=0;i<length;i++){dn.Add(Node(300+i,false,new[]{200L}));di.Add(Item(300+i,1000+i,"child."+i,200));}
 var d=Graph(100,dn.ToArray(),di.ToArray());var bn=Graph(0,new[]{Node(900,true)});
 var an=Graph(0,new[]{Node(900,true,children:new[]{100L}),dn[0] with {Parents=new[]{900L}}}.Concat(dn.Skip(1)).ToArray(),di.Select(i=>i.Item.NativeIdentity==100?i with {ParentIdentity=900}:i).ToArray());
 Check(DeliveryGraphAudit.CheckHandoff(bn,an,d,900).Decision==DeliveryReceiptDecision.Observed,"bounded generated graph handoff");
 Check(DeliveryGraphAudit.CheckHandoff(bn,an with {Items=an.Items.Select(i=>i.Item.NativeIdentity==300+length-1?i with {Item=i.Item with {Units=2}}:i).ToArray()},d,900).Decision==DeliveryReceiptDecision.Indeterminate,"generated graph corruption always caught");
}

GameItem Reset()
{
 GameSession.OnThread=true;GameSession.Identity=run;PlayerStore.instance=new();GameBuildFingerprint.IsKnown=true;
 InventoryApi.Items=new();InventoryApi.OnCapture=null;InventoryApi.Captures=0;InventoryApi.IsItemDragActive=false;InventoryFreezeApi.IsAvailable=true;InventoryFreezeApi.Frozen=null;
 return new(){identifier="sec_box",Owned=true};
}
var root=Reset();root.Attach(new GameInventory());var bag=(GameInventory)root.children![0];var loot=new GameItem{identifier="base.loot",Owned=false,uniqueId=0};bag.Attach(loot);
Check(ItemReceiptApi.StampDetached(root,owner,key,receipt).Stamp is not null,"real item-data receipt staging");
var capturedDetached=ItemGraphReceiptApi.CaptureDetached(root,owner,key);
var capturedBefore=ItemGraphReceiptApi.CaptureRun(owner,key);
Check(capturedDetached.Status==DeliveryReceiptCaptureStatus.Ready&&capturedDetached.Items.Count==2&&capturedDetached.Nodes.Count==3,"native boundary scan includes root inventory and child");
PlayerStore.instance!.gridInv!.Attach(root);InventoryApi.Items.Add(root);InventoryApi.Items.Add(loot);
var capturedAfter=ItemGraphReceiptApi.CaptureRun(owner,key);
root.Dead=true;loot.Dead=true;
Check(DeliveryGraphAudit.CheckHandoff(capturedBefore,capturedAfter,capturedDetached).Decision==DeliveryReceiptDecision.Observed,"post-handoff audit only managed data after all old wrappers invalidate");
root=Reset();Check(ItemGraphReceiptApi.CaptureDetached(root,owner,key) is {Status:DeliveryReceiptCaptureStatus.Invalid,Nodes.Count:0},"unstamped detached root rejected without partial evidence");
root=Reset();ItemReceiptApi.StampDetached(root,owner,key,receipt);root.parents!.Add(new GameInventory());
Check(ItemGraphReceiptApi.CaptureDetached(root,owner,key).Status==DeliveryReceiptCaptureStatus.Invalid,"external detached parent refused");
root=Reset();ItemReceiptApi.StampDetached(root,owner,key,receipt);InventoryApi.Items.Add(root);var calls=0;InventoryApi.OnCapture=()=>{if(++calls==2)root.unitCount++;};
Check(ItemGraphReceiptApi.CaptureRun(owner,key) is {Status:DeliveryReceiptCaptureStatus.Unavailable,Nodes.Count:0,Items.Count:0},"between-pass mutation yields no partial graph");
root=Reset();GameSession.OnThread=false;Check(ItemGraphReceiptApi.CaptureRun(owner,key).Status==DeliveryReceiptCaptureStatus.Unavailable&&InventoryApi.Captures==0,"wrong thread no native graph capture");
root=Reset();GameBuildFingerprint.IsKnown=false;Check(ItemGraphReceiptApi.CaptureRun(owner,key).Status==DeliveryReceiptCaptureStatus.Unavailable,"unknown build gated");
root=Reset();GameSession.Identity=null;Check(ItemGraphReceiptApi.CaptureRun(owner,key).Status==DeliveryReceiptCaptureStatus.Unavailable,"no loaded run gated");
root=Reset();Check(ItemGraphReceiptApi.CaptureDetached(null!,owner,key).Status==DeliveryReceiptCaptureStatus.Invalid&&ItemGraphReceiptApi.CaptureRun("bad/owner",key).Status==DeliveryReceiptCaptureStatus.Invalid,"null root and bad address refused");
root=Reset();DeliveryGraphSnapshot? nested=null;InventoryApi.OnCapture=()=>nested=ItemGraphReceiptApi.CaptureRun(owner,key);
Check(ItemGraphReceiptApi.CaptureRun(owner,key).Status==DeliveryReceiptCaptureStatus.Ready&&nested?.Status==DeliveryReceiptCaptureStatus.Unavailable,"recursive graph capture refused while outer remains usable");
root=Reset();InventoryApi.OnCapture=()=>throw new Exception("sensitive");var failed=ItemGraphReceiptApi.CaptureRun(owner,key);
Check(failed.Status==DeliveryReceiptCaptureStatus.Unavailable&&failed.Nodes.Count==0&&!failed.Reason!.Contains("sensitive"),"native exceptions fail closed without message leakage");
root=Reset();ItemReceiptApi.StampDetached(root,owner,key,receipt);root.children!.Add(null!);
Check(ItemGraphReceiptApi.CaptureDetached(root,owner,key).Status==DeliveryReceiptCaptureStatus.Invalid,"null native child invalidates whole capture");
root=Reset();ItemReceiptApi.StampDetached(root,owner,key,receipt);for(var i=0;i<8193;i++)root.Attach(new GameInventory());
Check(ItemGraphReceiptApi.CaptureDetached(root,owner,key) is {Status:DeliveryReceiptCaptureStatus.LimitExceeded,Nodes.Count:0},"native node/reference budget refuses whole graph");
root=Reset();Check(ItemGraphReceiptApi.CaptureRun(owner,key).Status==DeliveryReceiptCaptureStatus.Ready,"exception paths release graph gate");

// Exercise the distributed consumer workflow with real graph/receipt sources.
(GameItem Root, GameItem Loot, GameGridInventory Target, SlotMarker Marker) Reward()
{
 var r=Reset();var inv=new GameInventory();r.Attach(inv);
 var l=new GameItem{identifier="base.loot",Owned=false,uniqueId=0};inv.Attach(l);
 var t=new GameGridInventory();PlayerStore.instance!.gridInv=t;
 var m=new SlotMarker{item=r,inventory=t};t.Marker=m;
 return(r,l,t,m);
}
var reward=Reward();var stages=0;
reward.Marker.OnAccept=()=>{
 // Native admission invalidates the old wrappers; fresh run resolution returns new wrappers at the observed identities.
 var fresh=new GameItem{Pointer=reward.Root.Pointer,identifier=reward.Root.identifier,uniqueId=reward.Root.uniqueId,state=reward.Root.state};
 var oldBag=reward.Root.children![0];var newBag=new GameInventory{Pointer=oldBag.Pointer};fresh.Attach(newBag);
 var freshLoot=new GameItem{Pointer=reward.Loot.Pointer,identifier=reward.Loot.identifier,uniqueId=0,Owned=false,state=reward.Loot.state};newBag.Attach(freshLoot);
 reward.Target.Attach(fresh);InventoryApi.Items.Add(fresh);InventoryApi.Items.Add(freshLoot);reward.Root.Dead=true;reward.Loot.Dead=true;
};
var outcome=Author.GraphDeliveries.GraphDelivery.AttemptOnce(reward.Root,reward.Target,receipt,DeliveryLedgerState.Pending,()=>{stages++;return true;});
Check(outcome.NativeHandoffAttempted&&outcome.Audit.Decision==DeliveryReceiptDecision.Observed&&stages==1&&reward.Marker.Calls==1,"distributed filled reward confirms fresh graph without old-wrapper access");
reward=Reward();reward.Marker.targetItem=reward.Loot;
outcome=Author.GraphDeliveries.GraphDelivery.AttemptOnce(reward.Root,reward.Target,receipt,DeliveryLedgerState.Pending,()=>throw new Exception("should not stage"));
Check(!outcome.NativeHandoffAttempted&&reward.Marker.Calls==0,"distributed consumer rejects merge slot before ledger callback");
reward=Reward();
outcome=Author.GraphDeliveries.GraphDelivery.AttemptOnce(reward.Root,reward.Target,receipt,DeliveryLedgerState.Pending,()=>{reward.Loot.unitCount++;return true;});
Check(!outcome.NativeHandoffAttempted&&reward.Marker.Calls==0,"distributed consumer catches child mutation by ledger callback");
reward=Reward();reward.Marker.OnAccept=()=>throw new InvalidOperationException("private");
outcome=Author.GraphDeliveries.GraphDelivery.AttemptOnce(reward.Root,reward.Target,receipt,DeliveryLedgerState.Pending,()=>true);
Check(outcome.NativeHandoffAttempted&&reward.Marker.Calls==1&&outcome.Audit.Decision==DeliveryReceiptDecision.Indeterminate&&outcome.NativeError=="InvalidOperationException","distributed consumer attempted exception remains uncertain without retry");
reward=Reward();
outcome=Author.GraphDeliveries.GraphDelivery.AttemptOnce(reward.Root,reward.Target,receipt,DeliveryLedgerState.InFlight,()=>throw new Exception("no retry"));
Check(!outcome.NativeHandoffAttempted&&reward.Marker.Calls==0,"distributed consumer inflight absence does not stage or retry");
reward=Reward();
outcome=Author.GraphDeliveries.GraphDelivery.AttemptOnce(reward.Root,reward.Target,receipt,DeliveryLedgerState.Pending,()=>false);
Check(!outcome.NativeHandoffAttempted&&reward.Marker.Calls==0,"distributed consumer uncertain ledger stops before native handoff");
Console.WriteLine(JsonSerializer.Serialize(new{passed,failed=0,productionSources=true,scope="Real graph/receipt/item-data sources; explicit native discovery/graph/ownership stand-ins, not native admission/save acceptance"}));

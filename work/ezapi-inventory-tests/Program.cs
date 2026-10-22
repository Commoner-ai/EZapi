using EZapi;
using Il2Cpp;
using System.Text.Json;

var results=new List<object>(); var failures=new List<string>();
void Check(bool value,string message="assertion failed"){if(!value)throw new Exception(message);}
void Reject(Action action){try{action();}catch(ArgumentException){return;}catch(InvalidOperationException){return;}catch(OverflowException){return;}throw new Exception("expected rejection");}
void Case(string name,Action action){try{action();results.Add(new{name,passed=true});}catch(Exception e){failures.Add(name+": "+e);results.Add(new{name,passed=false,error=e.Message});}}
(GameGridInventory s,GameGridInventory t,GameItem item,PlayerStore player) World(int units=1){
    GameSession.Identity=new(1,"test",1);ItemMouseDragHandler.current=null;ItemMultiSelectHandler.current=null;
    var player=new PlayerStore();PlayerStore.instance=player;
    var s=new GameGridInventory();var t=new GameGridInventory();var item=new GameItem(){unitCount=units};s.Add(item);player.gridInv=s;player.Found.Add(item);
    return(s,t,item,player);
}
NativeInventorySnapshot Read(GameInventory inv)=>InventoryApi.CaptureDirect(inv, includeGeometry:true);

Case("run observation deduplicates roots",()=>{var w=World();w.player.Found.Add(w.item);Check(InventoryApi.CaptureRunItems(w.player).Count==1);});
Case("run traversal includes nested inventory",()=>{var w=World();var inner=new GameGridInventory();w.item.children.Add(inner);var child=new GameItem();inner.Add(child);Check(InventoryApi.CaptureRunItems(w.player).Count==2);});
Case("run traversal handles graph cycles",()=>{var w=World();w.item.children.Add(w.s);Check(InventoryApi.CaptureRunItems(w.player).Count==1);});
Case("save bag root included independently",()=>{var w=World();w.player.saveBags.Add("mod.root",new GameItem());Check(InventoryApi.CaptureRunItems(w.player).Count==2);});
Case("negative instance IDs supported",()=>{var w=World();w.item.uniqueId=-77;Check(Read(w.s).Items.Single().InstanceId==-77);});
Case("duplicate instance IDs rejected in direct snapshot",()=>{var w=World();var b=new GameItem(){uniqueId=w.item.uniqueId};w.s.Add(b,2);var r=Read(w.s);Check(r.Status==InventorySnapshotStatus.Invalid&&r.Items.Count==0);});
Case("duplicate instance IDs rejected in run graph",()=>{var w=World();w.player.saveBags["bad"]=new GameItem(){uniqueId=w.item.uniqueId};Reject(()=>InventoryApi.CaptureRunItems(w.player));});
Case("foreign PlayerStore rejected",()=>{World();Reject(()=>InventoryApi.CaptureRunItems(new PlayerStore()));});
Case("dead graph root rejected without partial list",()=>{var w=World();w.player.Found.Add(new GameItem(){Pointer=IntPtr.Zero});Reject(()=>InventoryApi.CaptureRunItems(w.player));});
Case("bounded roots rejected",()=>{var w=World();w.player.Found=Enumerable.Repeat(w.item,4097).ToList();Reject(()=>InventoryApi.CaptureRunItems(w.player));});
Case("owned filter counts units only",()=>{var w=World(7);var other=new GameItem(){unitCount=9,Owned=false};w.s.Add(other,2);Check(InventoryApi.CaptureOwnedItemCounts(w.player)[w.item.identifier]==7);});
Case("absent requested ID is zero",()=>{var w=World();var counts=InventoryApi.CaptureOwnedItemCounts(w.player,new[]{"missing"});Check(counts.Count==1&&counts["missing"]==0);});
Case("count filter duplicates coalesced",()=>{var w=World(3);var counts=InventoryApi.CaptureOwnedItemCounts(w.player,new[]{w.item.identifier,w.item.identifier});Check(counts.Count==1&&counts[w.item.identifier]==3);});
Case("count invalid IDs rejected",()=>{var w=World();Reject(()=>InventoryApi.CaptureOwnedItemCounts(w.player,new[]{" "}));});
Case("count overflow rejected",()=>{var w=World(int.MaxValue);w.s.Add(new GameItem(),2);Reject(()=>InventoryApi.CaptureOwnedItemCounts(w.player));});
Case("owned predicate reads native ownership",()=>{var w=World();w.item.Owned=false;Check(!InventoryApi.IsPlayerOwned(w.item));});
Case("snapshot detached from later quantity and shape changes",()=>{var w=World();var snap=Read(w.s);w.item.unitCount=4;w.item.shape.minX=3;Check(snap.Items.Single().Units==1&&snap.Items.Single().Shape!.X==0);});
Case("wrong parent causes empty invalid snapshot",()=>{var w=World();w.item.parentInventory=w.t;var snap=Read(w.s);Check(snap.Status==InventorySnapshotStatus.Invalid&&snap.Items.Count==0);});
Case("dead direct inventory rejected",()=>{var w=World();w.t.Pointer=IntPtr.Zero;Check(Read(w.t).Status==InventorySnapshotStatus.Invalid);});
Case("oversize finite grid rejected",()=>{World();Check(Read(new GameGridInventory(65,1)).Status==InventorySnapshotStatus.Invalid);});
Case("native boundary mask observed with outside one and inside zero",()=>{var w=World();var s=Read(w.s);Check(s.Status==InventorySnapshotStatus.Ready&&s.Grid!.OutsideBounds==1&&s.Grid.Cells.All(c=>c=='0'));});
Case("item infinite occupancy still rejected",()=>{var w=World();w.item.shape.Outside=1;Check(Read(w.s).Status==InventorySnapshotStatus.Invalid);});
Case("inventory unbounded exterior rejected",()=>{var w=World();w.s.inventoryShape.Outside=0;Check(Read(w.s).Status==InventorySnapshotStatus.Invalid);});
Case("unknown boundary sentinel rejected",()=>{var w=World();w.s.inventoryShape.Outside=2;Check(Read(w.s).Status==InventorySnapshotStatus.Invalid);});
Case("shape equality preserves outside sentinel",()=>{var a=new InventoryShape(0,0,1,1,0,false,"00");Check(a!=a with{OutsideBounds=1});});
Case("placement blocked boundary rejects without native write",()=>{World();var t=new GameGridInventory(1,1);t.inventoryShape.Data[0]=1;Reject(()=>InventoryPlacementApi.PlanWholeGrid(t,new[]{new GameItem()}));Check(t.AcceptCalls==0);});
Case("basic direct observation accepts grids beyond placement bound",()=>{World();Check(InventoryApi.CaptureDirect(new GameGridInventory(65,1)).Status==InventorySnapshotStatus.Ready);});
Case("read requires current run",()=>{var w=World();GameSession.Identity=null;Reject(()=>Read(w.s));});
Case("read requires main thread",()=>{var w=World();Check(Task.Run(()=>{try{Read(w.s);return false;}catch(InvalidOperationException){return true;}}).Result);});
Case("mouse drag detected",()=>{World();ItemMouseDragHandler.current=new(){IsDraggingItem=true};Check(InventoryApi.IsItemDragActive);});
Case("group drag detected",()=>{World();ItemMultiSelectHandler.current=new(){state=ItemMultiSelectHandler.State.GroupDragging};Check(InventoryApi.IsItemDragActive);});
Case("pending group drag detected",()=>{World();ItemMultiSelectHandler.current=new(){state=ItemMultiSelectHandler.State.PendingGroupDrag};Check(InventoryApi.IsItemDragActive);});
Case("selection alone is not drag",()=>{World();ItemMultiSelectHandler.current=new(){state=ItemMultiSelectHandler.State.Selected};Check(!InventoryApi.IsItemDragActive);});
Case("whole preview ready leaves inventories unchanged",()=>{var w=World(3);var a=Read(w.s);var b=Read(w.t);Check(InventoryApi.PreviewWholeGridTransfer(w.item,w.s,w.t).CanTransfer);Check(InventoryChecks.Equal(a,Read(w.s))&&InventoryChecks.Equal(b,Read(w.t))&&w.t.AcceptCalls==0);});
Case("preview detects source mismatch",()=>{var w=World();Check(InventoryApi.PreviewWholeGridTransfer(w.item,w.t,w.s).Status==TransferPreviewStatus.SourceMismatch);});
Case("preview denies partial removal",()=>{var w=World(4);w.item.RemoveLimit=2;Check(InventoryApi.PreviewWholeGridTransfer(w.item,w.s,w.t).Status==TransferPreviewStatus.Locked);});
Case("preview denies merging instead of whole empty slot",()=>{var w=World(4);w.t.Add(new GameItem(){unitCount=2});Check(InventoryApi.PreviewWholeGridTransfer(w.item,w.s,w.t).Status==TransferPreviewStatus.NoWholeSlot);});
Case("preview detects callback mutation",()=>{var w=World();w.t.BeforeFind=()=>w.item.unitCount=2;Check(InventoryApi.PreviewWholeGridTransfer(w.item,w.s,w.t).Status!=TransferPreviewStatus.Ready);});
Case("preview rejects self nesting",()=>{var w=World();w.item.children.Add(w.t);Check(!InventoryApi.PreviewWholeGridTransfer(w.item,w.s,w.t).CanTransfer);});

Case("whole item transfer keeps identity and native count",()=>{var w=World();var r=ItemTransferApi.Move(w.item,w.t,1);Check(r is {Moved:1,Indeterminate:false,Status:"Moved"}&&w.s.childItems.Count==0&&w.t.childItems.Single()==w.item&&w.t.AcceptCalls==1);});
Case("whole stack transfer quantity unchanged on instance",()=>{var w=World(6);var r=ItemTransferApi.Move(w.item,w.t,10);Check(r.Moved==6&&!r.Indeterminate&&w.item.unitCount==6);});
Case("split stack partial transfer verified",()=>{var w=World(6);var r=ItemTransferApi.Move(w.item,w.t,2);Check(r is {Moved:2,Indeterminate:false,Status:"Moved"}&&w.item.unitCount==4&&w.t.childItems.Single().unitCount==2);});
Case("merge stack transfer handles destroyed original",()=>{var w=World(3);var stack=new GameItem(){unitCount=2};w.t.Add(stack);var r=ItemTransferApi.Move(w.item,w.t,3);Check(r.Moved==3&&!r.Indeterminate&&stack.unitCount==5&&w.item.Pointer==IntPtr.Zero);});
Case("partial merge verified",()=>{var w=World(5);var stack=new GameItem(){unitCount=2};w.t.Add(stack);var r=ItemTransferApi.Move(w.item,w.t,2);Check(r.Moved==2&&!r.Indeterminate&&stack.unitCount==4&&w.item.unitCount==3);});
Case("native capacity limit reports Partial",()=>{var w=World(5);w.t.TransferLimit=2;var r=ItemTransferApi.Move(w.item,w.t,5);Check(r is {Moved:2,Status:"Partial",Indeterminate:false});});
Case("native removal limit reports Partial",()=>{var w=World(5);w.item.RemoveLimit=2;var r=ItemTransferApi.Move(w.item,w.t,5);Check(r is {Moved:2,Status:"Partial",Indeterminate:false});});
Case("zero maximum rejected before native accept",()=>{var w=World();Reject(()=>ItemTransferApi.Move(w.item,w.t,0));Check(w.t.AcceptCalls==0);});
Case("source equals destination rejected",()=>{var w=World();Check(ItemTransferApi.Move(w.item,w.s,1) is {Moved:0,Indeterminate:false}&&w.s.AcceptCalls==0);});
Case("remove lock prevents transfer",()=>{var w=World();w.s.RemoveLocked=true;Check(ItemTransferApi.Move(w.item,w.t,1).Moved==0&&w.t.AcceptCalls==0);});
Case("insert lock prevents transfer",()=>{var w=World();w.t.InsertLocked=true;Check(ItemTransferApi.Move(w.item,w.t,1).Moved==0&&w.t.AcceptCalls==0);});
Case("player insertion rejects",()=>{var w=World();w.t.PlayerInsert=false;Check(ItemTransferApi.Move(w.item,w.t,1).Moved==0&&w.t.AcceptCalls==0);});
Case("native ownership admission rejects",()=>{var w=World();w.t.OwnershipAccept=false;Check(ItemTransferApi.Move(w.item,w.t,1).Moved==0&&w.t.AcceptCalls==0);});
Case("foreign item rejects",()=>{var w=World();w.item.Owned=false;Check(ItemTransferApi.Move(w.item,w.t,1).Moved==0&&w.t.AcceptCalls==0);});
Case("active drag prevents native writes",()=>{var w=World();ItemMouseDragHandler.current=new(){IsDraggingItem=true};Check(ItemTransferApi.Move(w.item,w.t,1).Moved==0&&w.t.AcceptCalls==0);});
Case("nested target prevents native writes",()=>{var w=World();w.item.children.Add(w.t);Check(ItemTransferApi.Move(w.item,w.t,1).Moved==0&&w.t.AcceptCalls==0);});
Case("preflight mutation stops before accept",()=>{var w=World();w.t.BeforeFind=()=>w.item.unitCount=2;Check(ItemTransferApi.Move(w.item,w.t,1).Status=="Stale"&&w.t.AcceptCalls==0);});
Case("native no-op verified Rejected",()=>{var w=World();w.t.AcceptOverride=(_,_)=>0;Check(ItemTransferApi.Move(w.item,w.t,1) is {Moved:0,Indeterminate:false,Status:"Rejected"});});
Case("lying native return is indeterminate",()=>{var w=World();w.t.AcceptOverride=(_,_)=>1;Check(ItemTransferApi.Move(w.item,w.t,1).Indeterminate&&w.t.AcceptCalls==1);});
Case("source loss without destination gain detected",()=>{var w=World(3);w.t.AcceptOverride=(_,n)=>{w.item.unitCount-=n;return n;};Check(ItemTransferApi.Move(w.item,w.t,1).Indeterminate);});
Case("copy without source loss detected",()=>{var w=World();w.t.AcceptOverride=(_,n)=>{w.t.Add(new GameItem(),2);return n;};Check(ItemTransferApi.Move(w.item,w.t,1).Indeterminate);});
Case("native throws after successful write retains observed count",()=>{var w=World();w.t.AcceptOverride=(_,_)=>{w.s.Remove(w.item);w.t.Add(w.item);throw new Exception("after write");};var r=ItemTransferApi.Move(w.item,w.t,1);Check(r.Moved==1&&r.Indeterminate&&w.t.AcceptCalls==1);});
Case("native throws before write is still indeterminate and not retried",()=>{var w=World();w.t.AcceptOverride=(_,_)=>throw new Exception("native");Check(ItemTransferApi.Move(w.item,w.t,1).Indeterminate&&w.t.AcceptCalls==1);});
Case("run switches during native write no cross-run recovery",()=>{var w=World();w.t.AcceptOverride=(_,_)=>{GameSession.Identity=new(1,"test",2);return 1;};Check(ItemTransferApi.Move(w.item,w.t,1).Indeterminate&&w.t.AcceptCalls==1);});
Case("reentrant transfer blocked",()=>{var w=World();ItemTransferResult? inner=null;w.t.AcceptOverride=(_,_)=>{inner=ItemTransferApi.Move(w.item,w.t,1);return 0;};Check(!ItemTransferApi.Move(w.item,w.t,1).Indeterminate&&inner!.Status=="Reentrant"&&w.t.AcceptCalls==1);});
Case("native changes ownership detected",()=>{var w=World();w.t.AcceptOverride=(_,n)=>{w.s.Remove(w.item);w.t.Add(w.item);w.item.Owned=false;return n;};Check(ItemTransferApi.Move(w.item,w.t,1).Indeterminate);});
Case("unrelated source item mutation detected",()=>{var w=World();var unrelated=new GameItem(){identifier="other"};w.s.Add(unrelated,2);w.t.AcceptOverride=(_,n)=>{w.s.Remove(w.item);w.t.Add(w.item);unrelated.unitCount=3;return n;};Check(ItemTransferApi.Move(w.item,w.t,1).Indeterminate);});
Case("target lock change detected",()=>{var w=World();w.t.AcceptOverride=(_,n)=>{w.s.Remove(w.item);w.t.Add(w.item);w.t.InsertLocked=true;return n;};Check(ItemTransferApi.Move(w.item,w.t,1).Indeterminate);});
Case("duplicate IDs across inventories blocked before accept",()=>{var w=World();w.t.Add(new GameItem(){uniqueId=w.item.uniqueId,identifier="other"});Check(ItemTransferApi.Move(w.item,w.t,1).Moved==0&&w.t.AcceptCalls==0);});

Case("placement empty input returns empty without mutation",()=>{var w=World();var r=InventoryPlacementApi.PlanWholeGrid(w.t,Array.Empty<GameItem>());Check(r.Count==0&&w.t.AcceptCalls==0);});
Case("placement distinct products get non-overlapping shapes",()=>{World();var t=new GameGridInventory(2,1);var a=new GameItem();var b=new GameItem();var r=InventoryPlacementApi.PlanWholeGrid(t,new[]{a,b});Check(r.Count==2&&r[0].Shape.minX!=r[1].Shape.minX&&a.parentInventory is null&&b.parentInventory is null&&t.childItems.Count==0);});
Case("placement keeps existing items fixed",()=>{var w=World();w.t.Add(new GameItem(),0);var before=Read(w.t);var r=InventoryPlacementApi.PlanWholeGrid(w.t,new[]{new GameItem()});Check(r[0].Shape.minX==1&&InventoryChecks.Equal(before,Read(w.t)));});
Case("placement honors grid holes",()=>{World();var t=new GameGridInventory(2,1);t.inventoryShape.Data[0]=1;var r=InventoryPlacementApi.PlanWholeGrid(t,new[]{new GameItem()});Check(r[0].Shape.minX==1);});
Case("placement native denial tries alternative candidate",()=>{World();var t=new GameGridInventory(3,1);t.Admit=(_,s)=>s.minX!=0;var r=InventoryPlacementApi.PlanWholeGrid(t,new[]{new GameItem()});Check(r[0].Shape.minX==1);});
Case("placement uses native rotation",()=>{World();var t=new GameGridInventory(1,2);var p=new GameItem(){shape=new GridShape(2,1)};var r=InventoryPlacementApi.PlanWholeGrid(t,new[]{p});Check(r[0].Shape.globalWidth==1&&r[0].Shape.globalHeight==2&&p.shape.globalWidth==2);});
Case("placement duplicate products rejected",()=>{var w=World();var item=new GameItem();Reject(()=>InventoryPlacementApi.PlanWholeGrid(w.t,new[]{item,item}));});
Case("placement attached products rejected",()=>{var w=World();Reject(()=>InventoryPlacementApi.PlanWholeGrid(w.t,new[]{w.item}));});
Case("placement stacked products rejected",()=>{var w=World();Reject(()=>InventoryPlacementApi.PlanWholeGrid(w.t,new[]{new GameItem(){unitCount=2}}));});
Case("placement fails entire plan on no space",()=>{World();var t=new GameGridInventory(1,1);var a=new GameItem();var b=new GameItem();Reject(()=>InventoryPlacementApi.PlanWholeGrid(t,new[]{a,b}));Check(t.childItems.Count==0&&a.parentInventory is null&&b.parentInventory is null);});
Case("placement respects global grid origin",()=>{World();var t=new GameGridInventory(1,1);t.inventoryShape.minX=-2;t.inventoryShape.minY=3;var r=InventoryPlacementApi.PlanWholeGrid(t,new[]{new GameItem()});Check(r[0].Shape.minX==-2&&r[0].Shape.minY==3);});
Case("placement detects target mutation during admission",()=>{World();var t=new GameGridInventory(4,1);var once=false;t.Admit=(_,_)=>{if(!once){once=true;t.Add(new GameItem(),3);}return true;};Reject(()=>InventoryPlacementApi.PlanWholeGrid(t,new[]{new GameItem()}));});
Case("placement detects product mutation during admission",()=>{World();var t=new GameGridInventory(4,1);var item=new GameItem();t.Admit=(_,_)=>{item.unitCount=2;return true;};Reject(()=>InventoryPlacementApi.PlanWholeGrid(t,new[]{item}));});
Case("placement limit rejects before writing",()=>{var w=World();Reject(()=>InventoryPlacementApi.PlanWholeGrid(w.t,Enumerable.Range(0,65).Select(_=>new GameItem()).ToArray()));Check(w.t.AcceptCalls==0);});
Case("placement IsAvailable follows run state",()=>{World();Check(InventoryPlacementApi.IsAvailable);GameSession.Identity=null;Check(!InventoryPlacementApi.IsAvailable);});

Console.WriteLine(JsonSerializer.Serialize(new{suite="inventory",passed=results.Count-failures.Count,failed=failures.Count,limitations="offline stubs only; native admission, movement, UI and save/reload pending",cases=results},new JsonSerializerOptions{WriteIndented=true}));
if(failures.Count>0){foreach(var failure in failures)Console.Error.WriteLine(failure);Environment.Exit(1);}

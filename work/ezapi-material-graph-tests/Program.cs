using EZapi;
using Il2Cpp;
using System.Text.Json;
var checks=new List<object>();var failed=0;
void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(!ok){failed++;Console.Error.WriteLine(name);}}
(GameItem Root,GameItem Child,GameItem Grand,GameInventory Outer) World(){NativeAccess.Current=new(1);GameItem.OnDestroy=null;GameItem.DestroyOrder.Clear();InventoryFreezeApi.Refuse=false;var host=new GameItem{uniqueId=0};var source=new GameInventory(host,"source");var root=new GameItem{uniqueId=1};var child=new GameItem{uniqueId=2};var grand=new GameItem{uniqueId=3};source.Add(root);var grid=new GameInventory(root,"inner");grid.Add(child);new GameInventory(child,"grand").Add(grand);return(root,child,grand,source);}
var plain=new GameItem();Check(MachineMaterialGraph.Capture(plain) is null,"ordinary material has no nested journal");
var single=MachineMaterialGraph.CaptureState(plain);
Check(single.ItemCount==1&&single.Matches(),"read-only state journal includes plain root without descendants");
plain.state.Value++;Check(!single.Matches(),"plain root tag mutation detected by state journal");plain.state.Value--;
Check(single.Matches(),"read-only tag baseline remains intact after caller restores its own metadata");
plain.Shape="changed";Check(!single.Matches(),"read-only journal detects root geometry mutation");plain.Shape="original";
new GameInventory(new GameItem(),"other").Add(plain);Check(!single.Matches(),"read-only journal detects root parent mutation even when geometry matches");
var w=World();var graph=MachineMaterialGraph.Capture(w.Root)!;
Check(graph.ItemCount==3&&graph.Matches()&&graph.CanConsume(),"three-level native graph captured with ownership/removal eligibility");
var escrowOwner=new GameItem{uniqueId=4};var escrow=new GameInventory(escrowOwner,"escrow");escrow.Add(w.Root);w.Root.Shape="staged";
Check(graph.Matches(),"moving root and its source geometry preserves child graph");
w.Child.state.Value=9;w.Child.unitValue=99;Check(!graph.Matches(),"descendant state and value mutation detected");
Check(graph.Restore()&&w.Child.state.Value==0&&w.Child.unitValue==10,"descendant original state dictionary and value restored");
w.Outer.Add(w.Root);w.Root.Shape="original";Check(graph.Matches(),"original root placement allowed after return");
foreach(var kind in new[]{"parent","state","dict","shape","identity","window","topology","inventory-lock","unowned","frozen"}) {
 w=World();graph=MachineMaterialGraph.Capture(w.Root)!;
 switch(kind){case "parent":w.Grand.parentInventory=w.Outer;break;case "state":w.Child.state=new();break;case "dict":w.Child.state.dict=new();break;case "shape":w.Child.Shape="different";break;case "identity":w.Child.uniqueId++;break;case "window":w.Child.contentWindow=new();break;case "topology":w.Root.children.Add(new GraphNodeStorage());break;case "inventory-lock":w.Child.parentInventory!.Locked=true;break;case "unowned":w.Grand.Owned=false;break;case "frozen":w.Grand.Frozen=true;break;}
 if(kind=="frozen")Check(!graph.CanConsume(),"frozen descendant cannot be consumed");
 else if(kind=="inventory-lock")Check(!graph.Matches()&&!graph.CanConsume(),"nested inventory lock audited");
 else {Check(!graph.Matches(),kind+" change detected");if(kind is not "unowned")Check(!graph.Restore(),kind+" graph identity corruption cannot fake restoration");}
}
w=World();graph=MachineMaterialGraph.Capture(w.Root)!;graph.Consume(new Lease());
Check(GameItem.DestroyOrder.SequenceEqual(new[]{3,2,1}),"committed graph destroyed deepest first exactly once");
Check(graph.DestroyedInstanceIds.SequenceEqual(new[]{3,2,1}),"managed cleanup receipts record successful native completions in order");
Check(w.Outer.childItems.Count==0,"root removed after descendant cleanup");
w=World();graph=MachineMaterialGraph.Capture(w.Root)!;InventoryFreezeApi.Refuse=true;
try{graph.Consume(new Lease());Check(false,"refused expel must stop cleanup");}catch(InvalidOperationException){Check(GameItem.DestroyOrder.Count==0&&graph.DestroyedInstanceIds.Count==0&&w.Grand.parentInventory is not null,"failed expel preserves every material node without false completion receipts");}
w=World();graph=MachineMaterialGraph.Capture(w.Root)!;GameItem.OnDestroy=item=>{if(item.uniqueId==3)w.Child.unitValue++;};
try{graph.Consume(new Lease());Check(false,"changed remaining graph must stop cleanup");}catch(InvalidOperationException){Check(GameItem.DestroyOrder.SequenceEqual(new[]{3})&&w.Child.Pointer!=IntPtr.Zero,"native callback mutation stops before destroying changed remaining node");}
w=World();graph=MachineMaterialGraph.Capture(w.Root)!;NativeAccess.Current=new(2);Check(!graph.Matches()&&!graph.Restore(),"stale run never restores old graph");
// 熔炉根可被原版减为0并游离；子图仍保留并在成功后自内向外清理。
w=World();graph=MachineMaterialGraph.Capture(w.Root,observeRoot:false)!;
w.Root.unitCount=0;w.Outer.Remove(w.Root);
Check(graph.Matches(),"furnace contents remain auditable after root quantity zero and detachment");
w.Grand.state.Value=8;w.Child.unitValue=73;
Check(!graph.Matches()&&graph.Restore()&&w.Grand.state.Value==0&&w.Child.unitValue==10,"furnace child metadata restoration excludes consumed root");
Check(w.Root.unitCount==0&&w.Root.parentInventory is null,"contents restoration never rewrites consumed root custody or count");
Check(graph.Descendants.Select(i=>i.uniqueId).SequenceEqual(new[]{2,3})&&graph.ContainsDescendant(w.Grand.Pointer)&&!graph.ContainsDescendant(w.Root.Pointer),"furnace descendant identities available for exact freeze and destroy interception");
graph.ConsumeContents(new Lease(),item=>item.Destroy());
Check(GameItem.DestroyOrder.SequenceEqual(new[]{3,2})&&graph.DestroyedInstanceIds.SequenceEqual(new[]{3,2})&&w.Root.Pointer!=IntPtr.Zero,"furnace cleanup destroys descendants deepest first and leaves zero-count root to caller");
foreach(var kind in new[]{"frozen-expel","callback-metadata","callback-topology","callback-lock","stale"}) {
 w=World();graph=MachineMaterialGraph.Capture(w.Root,false)!;w.Root.unitCount=0;w.Outer.Remove(w.Root);
 if(kind=="frozen-expel")InventoryFreezeApi.Refuse=true;
 if(kind=="stale")NativeAccess.Current=new(2);
 GameItem.OnDestroy=item=>{if(item.uniqueId!=3)return;if(kind=="callback-metadata")w.Child.unitValue++;if(kind=="callback-topology")w.Root.children.Add(new GraphNodeStorage());if(kind=="callback-lock")w.Child.parentInventory!.Locked=true;};
 try{graph.ConsumeContents(new Lease(),item=>item.Destroy());Check(false,kind+": cleanup must stop");}
 catch(InvalidOperationException){Check(GameItem.DestroyOrder.SequenceEqual(kind is "stale" or "frozen-expel"?Array.Empty<int>():new[]{3}),kind+": cleanup stops without destroying changed remaining nodes");}
}
w=World();graph=MachineMaterialGraph.Capture(w.Root)!;
try{graph.ConsumeContents(new Lease(),item=>item.Destroy());Check(false,"ordinary escrow graph cannot use furnace-only cleanup");}catch(InvalidOperationException){Check(GameItem.DestroyOrder.Count==0,"ordinary escrow graph cannot use furnace-only cleanup");}
NativeAccess.Current=new(1);var emptyWindow=new GameItem{uniqueId=99,contentWindow=new()};emptyWindow.children.Add(emptyWindow.contentWindow);
graph=MachineMaterialGraph.Capture(emptyWindow,false)!;
Check(graph is not null&&graph.Matches(),"empty content window has a furnace topology journal");
emptyWindow.contentWindow=new();Check(!graph!.Matches()&&!graph.Restore(),"empty window replacement detected");
Console.WriteLine(JsonSerializer.Serialize(new{passed=checks.Count-failed,failed,checks,nativeRuntimeVerified=false,scope="production material graph journal; native graph/state/expel/destroy boundaries simulated"}));if(failed>0)Environment.Exit(1);
sealed class Lease:IDisposable{public void Dispose(){}}

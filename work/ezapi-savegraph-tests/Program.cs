using EZapi;using Il2Cpp;using System.Text.Json;using UnityEngine;
var results=new List<object>();var errors=new List<string>();
void Check(bool ok,string text="assertion failed"){if(!ok)throw new Exception(text);}
void Reject(Action a,string? code=null){try{a();}catch(Exception e){if(code is null||e.ToString().Contains(code,StringComparison.Ordinal))return;throw new Exception("wrong rejection: "+e);}throw new Exception("expected rejection");}
void Case(string name,Action a){try{Reset();a();results.Add(new{name,passed=true});}catch(Exception e){errors.Add(name+": "+e);results.Add(new{name,passed=false,error=e.Message});}}
void Reset(){GameSession.Identity=new(1,"test",1);PlayerStore.instance=new();InventoryApi.Live.Clear();DirectoryMaster.Missing.Clear();SaveManager.EncodeCalls=SaveManager.DecodeCalls=0;SaveManager.EncodeOverride=null;SaveManager.DecodeOverride=null;SaveManager.Callback=null;SaveManager.ThrowDecode=false;SaveManager.LastCreated=null;SaveManager.currentUUID=22;SaveManager.itemHeap=new();SaveManager.itemDict=new();SaveManager.saveJson="original";JsonUtility.ParseCalls=0;}
GameItem Root(){var r=new GameItem{uniqueId=10};r.Add(new GameItem{uniqueId=11,unitCount=3});return r;}
string Json(params SaveItemNode[] nodes)=>JsonUtility.ToJson(new SaveState{saveItems=new(nodes)});
SaveItemNode N(int id=10,long uuid=1)=>new(){identifier="test.item",uniqueId=id,uuid=uuid};
Case("items include root and nested child",()=>{var r=Root();Check(SaveGraphApi.Items(r).Count==2);});
Case("items reject repeated unique ID",()=>{var r=new GameItem{uniqueId=1};r.Add(new GameItem{uniqueId=1});Reject(()=>SaveGraphApi.Items(r),"IDENTITY");});
Case("items reject native graph cycle before encode",()=>{var r=Root();r.children[0].children.Add(r);Reject(()=>SaveGraphApi.Encode(r),"RELATION");Check(SaveManager.EncodeCalls==0);});
Case("items reject negative instance ID",()=>{Reject(()=>SaveGraphApi.Items(new GameItem{uniqueId=-1}),"IDENTITY");});
Case("observation permits separate unassigned temporary native instances",()=>{var r=new GameItem{uniqueId=-1};r.Add(new GameItem{uniqueId=-1});Check(SaveGraphApi.ItemsForObservation(r).Count==2);Reject(()=>SaveGraphApi.Items(r),"IDENTITY");Reject(()=>SaveGraphApi.Encode(r),"IDENTITY");Check(SaveManager.EncodeCalls==0);});
Case("observation rejects invalid ID below unassigned sentinel",()=>Reject(()=>SaveGraphApi.ItemsForObservation(new GameItem{uniqueId=-2}),"IDENTITY"));
Case("observation still rejects duplicate assigned IDs",()=>{var r=new GameItem{uniqueId=1};r.Add(new GameItem{uniqueId=1});Reject(()=>SaveGraphApi.ItemsForObservation(r),"IDENTITY");});
Case("observation still rejects cycles",()=>{var r=Root();r.children[0].children.Add(r);Reject(()=>SaveGraphApi.ItemsForObservation(r),"RELATION");});
Case("items reject dead node",()=>{var r=Root();r.children.Add(new GraphNodeStorage{Pointer=IntPtr.Zero});Reject(()=>SaveGraphApi.Items(r),"GRAPH");});
Case("items reject more than4096 items",()=>{var r=new GameItem{uniqueId=1};for(var i=2;i<=4097;i++)r.children.Add(new GameItem{uniqueId=i});Reject(()=>SaveGraphApi.Items(r),"LIMIT");});
Case("items reject background thread",()=>{Check(Task.Run(()=>{try{SaveGraphApi.Items(Root());return false;}catch(InvalidOperationException){return true;}}).Result);});
Case("items reject unloaded run",()=>{GameSession.Identity=null;Reject(()=>SaveGraphApi.Items(Root()));});
Case("encode preserves full native graph",()=>{var r=Root();var j=SaveGraphApi.Encode(r);using var d=JsonDocument.Parse(j);Check(d.RootElement.GetProperty("saveItems").GetArrayLength()==2);});
Case("encode restores global native scratch",()=>{var h=SaveManager.itemHeap;var d=SaveManager.itemDict;SaveGraphApi.Encode(Root());Check(ReferenceEquals(h,SaveManager.itemHeap)&&ReferenceEquals(d,SaveManager.itemDict)&&SaveManager.currentUUID==22&&SaveManager.saveJson=="original");});
Case("encode failure restores global scratch",()=>{var h=SaveManager.itemHeap;SaveManager.EncodeOverride=_=>throw new Exception("encode failed");Reject(()=>SaveGraphApi.Encode(Root()));Check(ReferenceEquals(h,SaveManager.itemHeap)&&SaveManager.currentUUID==22&&SaveManager.saveJson=="original");});
Case("encode detects missing nodes",()=>{SaveManager.EncodeOverride=_=>new(){N()};Reject(()=>SaveGraphApi.Encode(Root()),"INCOMPLETE");});
Case("encode rejects duplicate native UUID",()=>{SaveManager.EncodeOverride=_=>new(){N(),N(11)};Reject(()=>SaveGraphApi.Encode(Root()),"NODE");});
Case("encode rejects oversized explicit native shape",()=>{SaveManager.EncodeOverride=_=>new(){new SaveItemNode{uniqueId=10,uuid=1,itemShape=new GridShapeBuilder{width=100000}}};Reject(()=>SaveGraphApi.Encode(new GameItem{uniqueId=10}),"SHAPE");});
Case("encode rejects truncated explicit native shape",()=>{SaveManager.EncodeOverride=_=>new(){new SaveItemNode{uniqueId=10,uuid=1,itemShape=new GridShapeBuilder{width=2,height=2}}};Reject(()=>SaveGraphApi.Encode(new GameItem{uniqueId=10}),"SHAPE");});
Case("decode rejects exhausted instance ID before native creation",()=>{Reject(()=>SaveGraphApi.Decode(Json(N(int.MaxValue))),"NODE");Check(SaveManager.DecodeCalls==0);});
Case("encode detects input mutation",()=>{var r=Root();SaveManager.EncodeOverride=_=>{r.unitCount++;var n=N();n.unitCount=1;return new(){n};};Reject(()=>SaveGraphApi.Encode(r));});
Case("decode restores nested detached graph",()=>{var json=SaveGraphApi.Encode(Root());var decoded=SaveGraphApi.Decode(json);Check(decoded.parentInventory==null&&SaveGraphApi.Items(decoded).Count==2&&SaveGraphApi.Items(decoded).Single(i=>i.uniqueId==11).unitCount==3);});
Case("decode advances unique ID counter",()=>{var r=SaveGraphApi.Decode(Json(N(80)));Check(r.uniqueId==80&&PlayerStore.instance.currentUniqueId>=81);});
Case("decode never lowers unique ID counter",()=>{PlayerStore.instance.currentUniqueId=100;SaveGraphApi.Decode(Json(N(80)));Check(PlayerStore.instance.currentUniqueId==100);});
Case("decode rejects live instance conflict before native decode",()=>{InventoryApi.Live.Add(new GameItem{uniqueId=10});Reject(()=>SaveGraphApi.Decode(Json(N())),"CONFLICT");Check(SaveManager.DecodeCalls==0);});
Case("decode rejects external reserved instance conflict",()=>{Reject(()=>SaveGraphApi.Decode(Json(N()),new HashSet<int>{10}),"CONFLICT");Check(SaveManager.DecodeCalls==0);});
Case("decode rejects unavailable item definition",()=>{DirectoryMaster.Missing.Add("test.item");Reject(()=>SaveGraphApi.Decode(Json(N())),"NODE");Check(SaveManager.DecodeCalls==0);});
Case("decode rejects empty node set",()=>{Reject(()=>SaveGraphApi.Decode("{\"saveItems\":[]}"),"JSON");Check(JsonUtility.ParseCalls==0);});
Case("decode rejects duplicate JSON key before native parser",()=>{Reject(()=>SaveGraphApi.Decode("{\"saveItems\":[],\"saveItems\":[]}"),"JSON");Check(JsonUtility.ParseCalls==0);});
Case("decode rejects malformed JSON before native parser",()=>{Reject(()=>SaveGraphApi.Decode("not json"));Check(JsonUtility.ParseCalls==0);});
Case("decode rejects oversized JSON before native parser",()=>{Reject(()=>SaveGraphApi.Decode(new string('x',8*1024*1024+1)),"LIMIT");Check(JsonUtility.ParseCalls==0);});
Case("decode rejects disconnected graph",()=>{Reject(()=>SaveGraphApi.Decode(Json(N(),N(11,2))),"RELATION");Check(SaveManager.DecodeCalls==0);});
Case("decode rejects self reference",()=>{var n=N();n.childItems.Add(1);n.childItemInventoryNode.Add(0);Reject(()=>SaveGraphApi.Decode(Json(n)),"RELATION");});
Case("decode rejects unknown child",()=>{var n=N();n.childItems.Add(2);n.childItemInventoryNode.Add(0);Reject(()=>SaveGraphApi.Decode(Json(n)),"RELATION");});
Case("decode rejects child inventory length mismatch",()=>{var n=N();n.childItems.Add(2);Reject(()=>SaveGraphApi.Decode(Json(n,N(11,2))),"RELATION");});
Case("decode rejects multiple parents",()=>{var a=N();a.childItems.AddRange(new long[]{2,3});a.childItemInventoryNode.AddRange(new[]{0,0});var b=N(11,2);b.childItems.Add(3);b.childItemInventoryNode.Add(0);Reject(()=>SaveGraphApi.Decode(Json(a,b,N(12,3))),"RELATION");});
Case("decode rejects negative child inventory index",()=>{var n=N();n.childItems.Add(2);n.childItemInventoryNode.Add(-1);Reject(()=>SaveGraphApi.Decode(Json(n,N(11,2))),"RELATION");});
Case("decode invokes onLoaded once per restored node",()=>{var json=SaveGraphApi.Encode(Root());var count=0;SaveManager.Callback=_=>count++;SaveGraphApi.Decode(json);Check(count==2);});
Case("decode restores global scratch on success",()=>{var h=SaveManager.itemHeap;SaveGraphApi.Decode(Json(N()));Check(ReferenceEquals(h,SaveManager.itemHeap)&&SaveManager.currentUUID==22&&SaveManager.saveJson=="original");});
Case("decode partial native failure cleans created root and restores scratch",()=>{var h=SaveManager.itemHeap;SaveManager.ThrowDecode=true;Reject(()=>SaveGraphApi.Decode(Json(N())),"INDETERMINATE");Check(SaveManager.LastCreated!.Destroyed&&ReferenceEquals(h,SaveManager.itemHeap));});
Case("decode callback exception reports uncertainty and cleans detached graph",()=>{SaveManager.Callback=_=>throw new Exception("callback failure");Reject(()=>SaveGraphApi.Decode(Json(N())),"INDETERMINATE");Check(SaveManager.LastCreated!.Destroyed);});
Case("decode callback mutation is detected",()=>{SaveManager.Callback=i=>i.unitCount++;Reject(()=>SaveGraphApi.Decode(Json(N())),"INDETERMINATE");});
Case("decode reentrant operation is refused",()=>{var refused=false;SaveManager.Callback=i=>{try{SaveGraphApi.Encode(i);}catch(InvalidOperationException e){refused=e.Message.Contains("REENTRANT");}};SaveGraphApi.Decode(Json(N()));Check(refused);});
Case("decode callback placing root does not auto-destroy live placed item",()=>{SaveManager.Callback=i=>i.parentInventory=new GameInventory();Reject(()=>SaveGraphApi.Decode(Json(N())),"INDETERMINATE");Check(!SaveManager.LastCreated!.Destroyed);});
Case("failure releases reentrancy flag for a later operation",()=>{SaveManager.ThrowDecode=true;Reject(()=>SaveGraphApi.Decode(Json(N())));SaveManager.ThrowDecode=false;Check(SaveGraphApi.Decode(Json(N())).uniqueId==10);});
Case("decode capability reports matching exact loader",()=>Check(SaveGraphApi.CanDecode&&SaveGraphApi.DecodeUnavailableReason is null));
Case("current build loader signature accepted",()=>Check(SaveGraphLoader.Resolve(typeof(PlantTraitList),"LoadEdibleProduce").ReturnType==typeof(void)));
Case("missing exact loader rejected",()=>{Reject(()=>SaveGraphLoader.Resolve(typeof(string),"LoadEdibleProduce"),"UNSUPPORTED");Check(SaveManager.DecodeCalls==0);});
Case("unknown game build rejects before native parse and writes",()=>{GameBuildFingerprint.IsKnown=false;try{Check(!SaveGraphApi.CanDecode&&SaveGraphApi.DecodeUnavailableReason!.Contains("game build"));Reject(()=>SaveGraphApi.Decode(Json(N())),"UNSUPPORTED");Check(SaveManager.DecodeCalls==0&&JsonUtility.ParseCalls==0);}finally{GameBuildFingerprint.IsKnown=true;}});
Case("current loader order includes all fixups before one callback",()=>{LoaderTrace.Calls.Clear();SaveManager.Callback=i=>LoaderTrace.Add("callback",i);SaveGraphApi.Decode(Json(N()));Check(LoaderTrace.Calls.SequenceEqual(new[]{"upgrade","sprite","lock","produce","tool","waste","container","broken","machine","turbo","converter","callback"}.Select(s=>s+":10")));});
Case("all graph items receive complete current fixup sequence",()=>{var json=SaveGraphApi.Encode(Root());LoaderTrace.Calls.Clear();SaveManager.Callback=i=>LoaderTrace.Add("callback",i);SaveGraphApi.Decode(json);Check(LoaderTrace.Calls.Count==24&&LoaderTrace.Calls.Count(s=>s=="produce:11")==1&&LoaderTrace.Calls.Count(s=>s=="converter:11")==1);});
Case("wrong loader return type rejected",()=>Reject(()=>SaveGraphLoader.Resolve(typeof(BadLoader),"WrongReturn"),"UNSUPPORTED"));
Case("instance loader method rejected",()=>Reject(()=>SaveGraphLoader.Resolve(typeof(BadLoader),"Instance"),"UNSUPPORTED"));
Case("wrong loader parameter rejected",()=>Reject(()=>SaveGraphLoader.Resolve(typeof(BadLoader),"WrongArgument"),"UNSUPPORTED"));
var report=new{suite="save-graph",passed=results.Count-errors.Count,failed=errors.Count,cases=results,nativeRuntimeTest=false,limitations="real SaveGraph/NativeAccess source with offline native stubs; no IL2CPP detour, Unity JSON, actual save/reload or callback side-effect proof"};Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-savegraph-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed}));foreach(var e in errors)Console.Error.WriteLine(e);if(errors.Count>0)Environment.Exit(1);
sealed class BadLoader{public static int WrongReturn(GameItem item)=>0;public void Instance(GameItem item){}public static void WrongArgument(string item){}}

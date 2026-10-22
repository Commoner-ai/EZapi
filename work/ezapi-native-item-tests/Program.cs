using EZapi;
using Il2Cpp;
using System.Text;
using System.Text.Json;
var cases=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
void Throws<T>(Action action,string name) where T:Exception {try{action();throw new Exception("missing exception: "+name);}catch(T){Check(true,name);}}
GameItem Fresh(string id,string? type=null)=>new(){identifier=id,Types=type is null?new():new(){type}};
var bilingual=new LocalizedItemText("双语物品","Bilingual item");
Check(bilingual.ForLocale("zh-CN")=="双语物品"&&bilingual.ForLocale("zh-TW")=="双语物品","Chinese locales select Chinese item text");
Check(bilingual.ForLocale("en")=="Bilingual item"&&bilingual.ForLocale("fr")=="Bilingual item","non-Chinese locales safely select English item text");
Check(new LocalizedItemText(null,"English only").ForLocale("zh-CN")=="English only"&&new LocalizedItemText("仅中文",null).ForLocale("en")=="仅中文","missing translation falls back to provided locale text");
Check(new LocalizedItemText("","English").ForLocale("zh")=="","empty localized text explicitly clears the selected value");
var localizedOptions=new NativeItemOptions(Name:"Static fallback",LocalizedName:new("本地化名称","Localized name"),
    LocalizedShortDescription:new("本地化简介","Localized summary"),LocalizedFlavorText:new("本地化风味","Localized flavor"),
    LocalizedSourceName:new("本地化来源","Localized source"));
Check(localizedOptions.ResolveName("zh-CN")=="本地化名称"&&localizedOptions.ResolveName("en-US")=="Localized name"&&
    localizedOptions.ResolveShortDescription("zh")=="本地化简介"&&localizedOptions.ResolveFlavorText("en")=="Localized flavor"&&
    localizedOptions.ResolveSourceName("zh")=="本地化来源","native item options resolve all four bilingual fields");
Check(new NativeItemOptions(Name:"Static fallback",LocalizedName:new(null,"English name")).ResolveName("zh")=="English name","localized item options fall back to the static/value text");
ContentApi.Start();
Check(!ItemApi.RegisterItem("test.mod","test.mod.before",()=>Fresh("test.mod.before")).Accepted,"factory before initialization rejected");
ItemRegistry.Start();
Check(!ItemApi.RegisterItem("test.mod","test.mod.unbound_csv",()=>Fresh("test.mod.unbound_csv"),new(LocalizedName:new("中文","English"){Key="unbound"})).Accepted,"DLL options refuse unbound CSV keys instead of silently ignoring them");
Check(!ItemApi.RegisterItem("test.mod","test.mod.invalid_stock",()=>Fresh("test.mod.invalid_stock"),new(){NightShop=(NightShopStockPolicy)99}).Accepted,"unknown stock policy rejected at declaration before reserving ID");
Check(!ItemApi.RegisterItem("test.mod","test.mod.bad_localized_name",()=>Fresh("test.mod.bad_localized_name"),new(LocalizedName:new(null,null))).Accepted,"localized name must provide at least one nonblank language");
Check(!ItemApi.RegisterItem("test.mod","test.mod.long_localized_name",()=>Fresh("test.mod.long_localized_name"),new(LocalizedName:new(new string('名',201),"name"))).Accepted,"localized item name length is bounded");
Check(!ItemApi.RegisterItem("test.mod","test.mod.bad_localized_source",()=>Fresh("test.mod.bad_localized_source"),new(LocalizedSourceName:new("   ","source"))).Accepted,"localized source rejects blank provided text");
Check(!ItemApi.RegisterItem("test.mod","test.mod.long_localized_description",()=>Fresh("test.mod.long_localized_description"),new(LocalizedShortDescription:new(new string('文',4001),"summary"))).Accepted,"localized item description length is bounded");
Check(!ItemApi.RegisterItem("test.mod","other.mod.item",()=>new()).Accepted,"factory owner mismatch rejected");
Check(!ItemApi.RegisterItem("test.mod","test.mod.bad\n",()=>new()).Accepted,"trailing newline ID rejected");
Check(!ItemApi.RegisterItem("test.mod","test.mod.null",null!).Accepted,"null factory rejected without executing");
Check(!ItemApi.RegisterItem("test.mod","test.mod.text",()=>new(),new(Name:" ")).Accepted,"blank name rejected");
Check(!ItemApi.RegisterItem("test.mod","test.mod.text",()=>new(),new(SourceName:"\n")).Accepted,"control source rejected");
Check(!ItemApi.RegisterItem("test.mod","test.mod.text",()=>new(),new(ShortDescription:new('x',4001))).Accepted,"description length rejected");
Check(!ItemApi.RegisterModules("test.mod",Array.Empty<NativeModuleRegistration>()).Accepted,"empty factory batch rejected");
Throws<ArgumentNullException>(()=>ItemApi.RegisterModules("test.mod",null!),"null batch rejected");
Throws<InvalidOperationException>(()=>Task.Run(()=>ItemApi.RegisterItem("test.mod","test.mod.worker",()=>new())).GetAwaiter().GetResult(),"worker factory submission rejected");
var calls=0;
Check(ItemRegistry.Register(new(){Id="test.mod.json",Name="json"}).Accepted,"JSON declaration accepted");
Check(!ItemApi.RegisterItem("test.mod","test.mod.json",()=>new()).Accepted,"factory cannot overwrite queued JSON");
var definitions=new[]{new NativeModuleRegistration("test.mod.first",()=>{calls++;return Fresh("test.mod.first","MODULE");}),new NativeModuleRegistration("test.mod.second",()=>Fresh("test.mod.second","MODULE"))};
bool atomic=true;
using var lease=ContentApi.Subscribe("test.mod","atomic",v=>{if(v.ContentId=="test.mod.first")atomic&=ItemApi.FactorySnapshot().Count==2;});
Check(ItemApi.RegisterModules("test.mod",definitions).Accepted&&calls==0&&atomic,"factory batch staged atomically without executing factory");
definitions[0]=new("test.mod.changed",()=>new());
Check(ItemApi.FactorySnapshot().Any(v=>v.ItemId=="test.mod.first")&&!ItemApi.FactorySnapshot().Any(v=>v.ItemId=="test.mod.changed"),"caller array mutation does not replace entries");
lease.Dispose();
Check(!ItemRegistry.Register(new(){Id="test.mod.first",Name="collision"}).Accepted,"JSON cannot overwrite queued factory");
Check(!ItemApi.RegisterModules("test.mod",new[]{new NativeModuleRegistration("test.mod.fresh",()=>new()),new NativeModuleRegistration("test.mod.first",()=>new())}).Accepted&&!ItemApi.FactorySnapshot().Any(v=>v.ItemId=="test.mod.fresh"),"cross-batch collision does not reserve unrelated ID");
Check(!ItemApi.RegisterModules("test.mod",new[]{new NativeModuleRegistration("test.mod.dupe",()=>new()),new NativeModuleRegistration("test.mod.dupe",()=>new())}).Accepted,"duplicate in batch rejected atomically");
Check(ItemApi.RegisterNode("test.mod","test.mod.node",()=>Fresh("test.mod.node","NODE")).Accepted,"node declaration accepted");
Check(ItemApi.RegisterAmenity("test.mod","test.mod.amenity",()=>Fresh("test.mod.amenity")).Accepted,"amenity declaration accepted");
Check(ItemApi.RegisterModule("test.mod","test.mod.module",()=>Fresh("test.mod.module","MODULE")).Accepted,"single module declaration accepted");
Check(ItemApi.RegisterItem("test.mod","test.mod.item",()=>Fresh("test.mod.item"),new("new name","","flavor","source")).Accepted,"item and fixed text declared");
Check(ItemApi.RegisterItem("test.mod","test.mod.localized",()=>Fresh("test.mod.localized"),localizedOptions).Accepted,"bilingual item registration accepted");
Check(!NativeFactoryRegistry.StockDeclarations().Any(x=>x.Id=="test.mod.localized"),"options without stock policy do not enter stock declarations");
Check(!NativeFactoryRegistry.StockDeclarations().Any(x=>x.Id=="test.mod.node"),"null options do not enter stock declarations or dereference null");
Check(ItemApi.RegisterItem("test.mod","test.mod.conflict",()=>Fresh("test.mod.conflict")).Accepted,"native conflict staged before directory available");
Check(ItemApi.RegisterItem("test.mod","test.mod.add_fail",()=>Fresh("test.mod.add_fail")).Accepted,"native add failure staged");
var bad=(string?)null;
Check(ItemApi.RegisterItem("test.mod","test.mod.fail",()=>bad switch {
 "null"=>null!,"wrong_id"=>Fresh("someone.else"),"attached"=>new(){identifier="test.mod.fail",parentInventory=new()},
 "dead"=>new(){identifier="test.mod.fail",Pointer=IntPtr.Zero},"count"=>new(){identifier="test.mod.fail",unitCount=0},
 _=>throw new InvalidOperationException("author failure")}).Accepted,"bad factory staged without running it");
Check(ItemApi.RegisterNode("test.mod","test.mod.bad_node",()=>Fresh("test.mod.bad_node")).Accepted,"node kind checked at creation not staging");
Check(ItemApi.RegisterModule("test.mod","test.mod.bad_module",()=>Fresh("test.mod.bad_module")).Accepted,"module kind checked at creation not staging");
var reused=Fresh("test.mod.reuse");
ItemApi.RegisterItem("test.mod","test.mod.reuse",()=>reused);
WeakReference<GameItem>? recentInstance=null;
ItemApi.RegisterItem("test.mod","test.mod.pin_last",()=>{var item=Fresh("test.mod.pin_last");recentInstance=new(item);return item;});
ModItemDirectory? directory=null;
ItemApi.RegisterItem("test.mod","test.mod.reentrant",()=>directory!.Factories["test.mod.reentrant"].Invoke());
DirectoryMaster.NativeIds.Add("test.mod.conflict");
directory=new();directory.FailIds.Add("test.mod.add_fail");
ItemRegistry.Apply(directory);
Check(ItemApi.Snapshot().Count==ItemApi.FactorySnapshot().Count+1,"snapshot combines factory and JSON declarations");
Check(ItemRegistry.GetStatus("test.mod.conflict").StartsWith("Conflict"),"existing game directory ID not overwritten");
Check(ItemRegistry.GetStatus("test.mod.add_fail").StartsWith("Failed"),"false native Add reported as failed");
Check(ItemRegistry.GetStatus("test.mod.item")=="Applied","successful directory application reported");
var adds=directory.Adds;ItemRegistry.Apply(directory);
Check(directory.Adds==adds,"same directory pointer is idempotent");
Check(!ItemApi.RegisterItem("test.mod","test.mod.late",()=>new()).Accepted,"late factory submission rejected");
foreach(var id in new[]{"first","second","node","amenity","module","item","localized"}) {
 var a=directory.Factories["test.mod."+id].Invoke();var b=directory.Factories["test.mod."+id].Invoke();
 Check(a is not null&&b is not null&&a.Pointer!=b.Pointer,id+" independent instances");
}
Check(calls==2,"factory executes once per native request");
var named=directory.Factories["test.mod.item"].Invoke();
Check(named.name=="new name"&&named.shortDescription==""&&named.flavorText=="flavor","fixed text options applied including explicit empty string");
Check(ItemRegistry.TryGet("test.mod.item",out var presentation)&&presentation.Tooltip.SourceName=="source","presentation participates in existing official tooltip path");
Check(ItemApi.GetDisplayName("test.mod.localized")=="Localized name"&&ItemRegistry.TryGet("test.mod.localized",out var localizedPresentation)&&
    localizedPresentation.Name=="Localized name"&&localizedPresentation.Tooltip.SourceName=="Localized source","current-locale name and source flow into item API and official tooltip presentation");
foreach(var mode in new[]{"null","wrong_id","attached","dead","count","throw"}) {
 bad=mode;
 Check(directory.Factories["test.mod.fail"].Invoke() is null,"factory "+mode+" isolated at native callback boundary");
}
Check(ItemApi.FactorySnapshot().Single(v=>v.ItemId=="test.mod.fail").Failures==6,"all factory failures counted independently of directory status");
Check(ItemRegistry.GetStatus("test.mod.fail")=="Applied","factory failure does not falsely remove directory application");
foreach(var id in new[]{"bad_node","bad_module"})Check(directory.Factories["test.mod."+id].Invoke() is null,id+" rejects missing official kind");
Check(directory.Factories["test.mod.reuse"].Invoke()==reused&&directory.Factories["test.mod.reuse"].Invoke() is null,"immediate cached instance rejected without destroying borrowed object");
directory.Factories["test.mod.pin_last"].Invoke();
GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
Check(recentInstance!.TryGetTarget(out _),"latest returned wrapper remains rooted against native address reuse until next creation or registry stop");
Check(directory.Factories["test.mod.reentrant"].Invoke() is null&&!ItemApi.FactorySnapshot().Single(v=>v.ItemId=="test.mod.reentrant").Creating,"recursive native factory isolated and flag cleaned");
Check(Task.Run(()=>directory.Factories["test.mod.item"].Invoke()).GetAwaiter().GetResult() is null,"native factory worker call rejected before author callback");
DirectoryMaster.NativeIds.Add("test.mod.fail");
GameItem? sameIdFallback=null;
DirectoryMaster.Factory=(id,owned)=>{bad="null";directory.Factories[id].Invoke();sameIdFallback=Fresh(id);return sameIdFallback;};
Throws<InvalidOperationException>(()=>Items.Create("test.mod.fail",false),"native placeholder retaining requested ID is rejected by authoritative factory error");
Check(sameIdFallback?.Destroyed==true,"same-ID fallback is destroyed after a known factory failure");
var held=directory.Factories["test.mod.first"];
ItemRegistry.Stop();ItemRegistry.Start();
Check(held.Invoke() is null,"retained native delegate stays inert after stop and restart");
Check(ItemApi.FactorySnapshot().Count==0,"stop clears managed registry and counters");
Check(ItemApi.RegisterItem("test.mod","test.mod.first",()=>Fresh("test.mod.first")).Accepted,"restart permits ID registration again");
var rebuilt=new ModItemDirectory{Pointer=new(99)};ItemRegistry.Apply(rebuilt);
Check(rebuilt.Factories["test.mod.first"].Invoke() is not null,"new lifecycle uses fresh factory delegate");
// Current game returns an UNKNOWN placeholder for a failed native factory. Test the real public wrapper.
DirectoryMaster.NativeIds.Add("test.mod.wrapper");
var unknown=Fresh("UNKNOWN");DirectoryMaster.Factory=(_,_)=>unknown;
Throws<InvalidOperationException>(()=>ItemApi.Create("test.mod.wrapper",false),"public Create rejects native placeholder");
Check(unknown.Destroyed,"rejected detached placeholder is destroyed");
var attached=new GameItem{identifier="test.mod.wrapper",parentInventory=new()};DirectoryMaster.Factory=(_,_)=>attached;
Throws<InvalidOperationException>(()=>Items.Create("test.mod.wrapper"),"public Create rejects already attached result");
Check(!attached.Destroyed,"public Create preserves incorrectly returned player inventory object");
DirectoryMaster.Factory=(_,_)=>null;
Throws<InvalidOperationException>(()=>Items.Create("test.mod.wrapper"),"public Create rejects native null result");
var valid=Fresh("test.mod.wrapper");DirectoryMaster.Factory=(_,_)=>valid;
Check(Items.Create("test.mod.wrapper",false)==valid&&!valid.Destroyed,"public Create transfers valid detached instance responsibility to caller");
ItemRegistry.Stop();ContentApi.Stop();
foreach(var json in new[]{
 "[{\"id\":\"a.b\",\"name\":\"module\",\"module\":{\"performance\":100001}}]",
 "[{\"id\":\"a.b\",\"name\":\"module\",\"module\":{\"tier\":0}}]",
 "[{\"id\":\"a.b\",\"name\":\"module\",\"module\":{\"type\":\"not a type\"}}]",
 "[{\"id\":\"a.b\",\"name\":\"module\",\"node\":true,\"module\":{}}]",
 "[{\"id\":\"a.b\",\"name\":\"module\",\"module\":{},\"food\":{\"calories\":1}}]",
 "[{\"id\":\"a.b\",\"name\":\"module\",\"node\":true,\"container\":{\"width\":1,\"height\":1}}]",
 "[{\"id\":\"a.b\",\"name\":\"module\",\"node\":true,\"tags\":[{\"id\":\"NODE_TEST\"}]}]",
 "[{\"id\":\"a.b\",\"name\":\"module\",\"module\":{\"performance\":1,\"performance\":2}}]",
 "[{\"id\":\"a.b\",\"name\":\"module\",\"module\":{\"unknown\":1}}]",
 "[{\"id\":\"a.b\",\"name\":\"module\",\"module\":{\"type\":null}}]"}) {
 using var stream=new MemoryStream(Encoding.UTF8.GetBytes(json));
 Throws<InvalidDataException>(()=>JsonItems.Parse(stream),"invalid module JSON "+cases.Count);
}
using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("[{\"id\":\"a.b\",\"name\":\"module\",\"module\":{\"type\":\"a.type\",\"performance\":10,\"efficiency\":5,\"quality\":3,\"tier\":2,\"isShipModule\":false}}]"))) {
 var m=JsonItems.Parse(stream).Single().Module!;
 Check(m.Type=="a.type"&&m.Performance==10&&m.Efficiency==5&&m.Quality==3&&!m.IsShipModule&&m.Tier==2,"JSON module fields parsed intact");
}
var report=new{passed=cases.Count,failed=0,cases,nativeRuntimeTest=false,scope="real factory registry, shared IDs and JSON rules; native directory/delegate allocation are stand-ins"};
File.WriteAllText("work/ezapi-native-item-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(report));

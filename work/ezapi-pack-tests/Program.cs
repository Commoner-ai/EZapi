using EZapi;
using System.Text;
using System.Text.Json;
var cases=new List<string>();
void Check(bool condition,string name) {if(!condition)throw new Exception(name);cases.Add(name);}
PackManifest Parse(string text) {using var data=new MemoryStream(Encoding.UTF8.GetBytes(text));return ContentPacks.ParseManifest(data);}
var basic=new Dictionary<string,object>{{"id","test.pack"},{"name","包"},{"ezapiVersion","0.1"}};
string Json(object o)=>JsonSerializer.Serialize(o);
Check(Parse(Json(basic)).Version=="0.0.0","legacy three-field manifest defaults content version");
var extended=new Dictionary<string,object>(basic){{"version","1.2.3"},{"minEzapiVersion","0.1.6"},{"requires",new[]{"loot.tables"}},{"dependencies",new[]{new{id="test.base",minVersion="1.0.0"}}}};
Check(Parse(Json(extended)).Dependencies.Single().MinVersion=="1.0.0","version capability and dependency fields accepted");
Check(FrameworkInfo.Version=="0.1.74"&&Parse(Json(new Dictionary<string,object>(basic){{"minEzapiVersion","0.1.74"}})).MinEzapiVersion=="0.1.74","current framework and manifest boundary both report 0.1.74");
Check(Parse(Json(new Dictionary<string,object>(basic){{"requires",new[]{"runtime.modules"}}})).Requires.Contains("runtime.modules"),"module capability accepted by shared manifest parser");
void RejectManifest(string name,string json) {
    try {Parse(json);throw new Exception("accepted: "+name);}catch(Exception e)when(e is InvalidDataException or JsonException){Check(true,name);}
}
RejectManifest("next framework version is rejected",Json(new Dictionary<string,object>(basic){{"minEzapiVersion","0.1.75"}}));
foreach(var (field,value) in new (string,object)[]{("extra",true),("version","1.2"),("version","1.2.3-beta"),("version","01.2.3"),("version","1.2.3.4"),("version","1.9999999999.0"),("minEzapiVersion","9.0.0"),("requires",new[]{"network"}),("requires",new[]{"loot.tables","loot.tables"}),("requires",Enumerable.Repeat("items.json",65).ToArray()),("id","Bad.ID"),("name"," "),("ezapiVersion","0.2")}) {
    var row=new Dictionary<string,object>(basic){[field]=value};RejectManifest("manifest rejects "+field+"="+Json(value),Json(row));
}
RejectManifest("manifest rejects duplicate key","{\"id\":\"test.pack\",\"id\":\"x\",\"name\":\"n\",\"ezapiVersion\":\"0.1\"}");
RejectManifest("manifest rejects trailing newline in version",Json(new Dictionary<string,object>(basic){{"version","1.0.0\n"}}));
RejectManifest("manifest rejects trailing newline in owner",Json(new Dictionary<string,object>(basic){["id"]="test.pack\n"}));
RejectManifest("manifest rejects explicit null",Json(new Dictionary<string,object?>(basic.ToDictionary(p=>p.Key,p=>(object?)p.Value)){{"minEzapiVersion",null}}));
RejectManifest("manifest rejects unknown nested dependency field",Json(new Dictionary<string,object>(basic){{"dependencies",new[]{new{id="test.base",maybe=true}}}}));
RejectManifest("manifest rejects self dependency",Json(new Dictionary<string,object>(basic){{"dependencies",new[]{new{id="test.pack"}}}}));
RejectManifest("manifest rejects duplicate dependency",Json(new Dictionary<string,object>(basic){{"dependencies",new[]{new{id="test.base"},new{id="test.base"}}}}));
RejectManifest("manifest byte limit","{\"id\":\"test.pack\",\"name\":\""+new string('a',65536)+"\",\"ezapiVersion\":\"0.1\"}");
Check(ContentPacks.ParseVersion("1.10.0")>ContentPacks.ParseVersion("1.9.99"),"versions compare numerically");
Check(!FrameworkInfo.GetCapabilities().Any(c=>c.Id=="network"),"planned capabilities not advertised as implemented");

var scratch=Path.Combine(Path.GetTempPath(),"ezapi-packs-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
string Pack(string root,string folder,string id,string version="1.0.0",object[]? deps=null) {
    var path=Path.Combine(root,folder);Directory.CreateDirectory(path);
    File.WriteAllText(Path.Combine(path,"pack.json"),Json(new{id,name=id,ezapiVersion="0.1",version,dependencies=deps??Array.Empty<object>()}));
    File.WriteAllText(Path.Combine(path,"items.json"),Json(new[]{new{id=id+".item",modId=id,name="物品"}}));return path;
}
string Area(string n) {var p=Path.Combine(scratch,n);Directory.CreateDirectory(p);return p;}
var own=Pack(Area("single"),"pack","test.pack");
Check(ContentPacks.Check(own).Valid,"single valid pack accepted");
File.WriteAllText(Path.Combine(own,"items.json"),"[]");Check(ContentPacks.Check(own).Valid,"loot-only empty item array remains valid");
File.WriteAllText(Path.Combine(own,"items.json"),Json(new[]{new{id="other.pack.item",modId="test.pack",name="bad"}}));
Check(!ContentPacks.Check(own).Valid,"foreign item ownership rejected offline");
File.WriteAllText(Path.Combine(own,"items.json"),Json(new[]{new{id="test.pack.item",modId="test.pack",name="png",sprite="icon"}}));
Check(!ContentPacks.Check(own).Valid,"missing PNG rejected offline");
Directory.CreateDirectory(Path.Combine(own,"Textures","Items"));var png=Path.Combine(own,"Textures","Items","icon.png");
byte[] Header(int w,int h) {var b=new byte[24];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(b,0);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8,4),13);Encoding.ASCII.GetBytes("IHDR").CopyTo(b,12);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16,4),w);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20,4),h);return b;}
File.WriteAllBytes(png,Header(16,16));Check(ContentPacks.Check(own).Valid,"PNG structural check accepts valid bounded header without claiming decode");
File.WriteAllBytes(png,Header(2049,16));Check(!ContentPacks.Check(own).Valid,"PNG dimension limit enforced");
File.WriteAllBytes(png,new byte[24]);Check(!ContentPacks.Check(own).Valid,"PNG signature and IHDR validated");
File.WriteAllBytes(png,Header(16,16));
var loots=Path.Combine(own,"LootTables");Directory.CreateDirectory(loots);
File.WriteAllText(Path.Combine(loots,"01.json"),Json(new{tables=new[]{new{id="test.pack.table",entries=new[]{new{id="test.pack.item",weight=1}}}}}));
Check(ContentPacks.Check(own).LootFiles==1,"whole pack parses loot alongside items and PNG");
File.WriteAllText(Path.Combine(loots,"02.json"),"{");Check(!ContentPacks.Check(own).Valid,"bad later loot file rejects whole pack");File.Delete(Path.Combine(loots,"02.json"));
File.WriteAllText(Path.Combine(loots,"02.json"),File.ReadAllText(Path.Combine(loots,"01.json")));Check(!ContentPacks.Check(own).Valid,"cross-file duplicate loot list rejected offline");File.Delete(Path.Combine(loots,"02.json"));
File.WriteAllText(Path.Combine(loots,"01.json"),Json(new{tables=new[]{new{id="test.pack.table",entries=new[]{new{id="test.pack.missing",weight=1}}}}}));
Check(!ContentPacks.Check(own).Valid,"missing own loot item rejected offline");
for(int n=0;n<17;n++)File.WriteAllText(Path.Combine(loots,n+".json"),"{}");
Check(!ContentPacks.Check(own).Valid,"loot file count bounded");

var graph=Area("graph");
var nested=Area("nested");
var parent=Pack(nested,"a","test.parent",deps:new object[]{new{id="test.parent.child"}});
Pack(nested,"b","test.parent.child");Directory.CreateDirectory(Path.Combine(parent,"LootTables"));
File.WriteAllText(Path.Combine(parent,"LootTables","01.json"),Json(new{tables=new[]{new{id="test.parent.table",entries=new[]{new{id="test.parent.child.item",weight=1}}}}}));
Check(ContentPacks.CheckDirectory(nested).Valid,"nested dependency namespace is not mistaken for missing own item");
Pack(graph,"00-dependent","test.consumer",deps:new object[]{new{id="test.base",minVersion="1.0.0"}});Pack(graph,"99-base","test.base");Pack(graph,"50-independent","test.other");
var scan=ContentPacks.CheckDirectory(graph);Check(scan.Valid&&scan.LoadOrder.ToList().IndexOf("test.base")<scan.LoadOrder.ToList().IndexOf("test.consumer"),"dependency order overrides folder order");
Check(scan.LoadOrder.SequenceEqual(ContentPacks.CheckDirectory(graph).LoadOrder),"load order stable across scans");
var missing=Area("missing");var m=Pack(missing,"a","test.consumer",deps:new object[]{new{id="test.absent"}});Pack(missing,"b","test.other");
Check(ContentPacks.Check(m).Valid,"single pack check does not claim installed dependency resolution");
scan=ContentPacks.CheckDirectory(missing);Check(!scan.Valid&&scan.LoadOrder.SequenceEqual(new[]{"test.other"}),"missing dependency excludes consumer but preserves independent pack");
var old=Area("old");Pack(old,"a","test.base",version:"1.0.0");Pack(old,"b","test.consumer",deps:new object[]{new{id="test.base",minVersion="1.0.1"}});
Check(ContentPacks.CheckDirectory(old).LoadOrder.SequenceEqual(new[]{"test.base"}),"minimum dependency content version enforced");
var cycle=Area("cycle");Pack(cycle,"a","test.a",deps:new object[]{new{id="test.b"}});Pack(cycle,"b","test.b",deps:new object[]{new{id="test.a"}});Pack(cycle,"c","test.c",deps:new object[]{new{id="test.a"}});Pack(cycle,"d","test.d");
scan=ContentPacks.CheckDirectory(cycle);Check(scan.Packs.Count(p=>!p.Valid)==3&&scan.LoadOrder.SequenceEqual(new[]{"test.d"}),"cycle and transitive consumers rejected without blocking independent pack");
var duplicate=Area("duplicate");Pack(duplicate,"a","test.base");Pack(duplicate,"b","test.base");Pack(duplicate,"c","test.consumer",deps:new object[]{new{id="test.base"}});
scan=ContentPacks.CheckDirectory(duplicate);Check(scan.Packs.All(p=>!p.Valid)&&scan.LoadOrder.Count==0,"all duplicate ID copies and their consumers rejected");
var invalid=Area("invalid");var bad=Pack(invalid,"a","test.base");File.WriteAllText(Path.Combine(bad,"items.json"),"bad");Pack(invalid,"b","test.consumer",deps:new object[]{new{id="test.base"}});
Check(ContentPacks.CheckDirectory(invalid).LoadOrder.Count==0,"invalid dependency payload prevents consumer load");
var limit=Area("limit");for(int n=0;n<257;n++)Directory.CreateDirectory(Path.Combine(limit,n.ToString()));
try {ContentPacks.CheckDirectory(limit);throw new Exception("limit accepted");}catch(InvalidDataException){Check(true,"directory pack count bounded");}

ItemRegistry.Start();LootTables.Start();ContentPackLoader.Clear();
var runtimeErrors=new List<string>();ContentPackLoader.LoadAll(graph,runtimeErrors.Add);
Check(runtimeErrors.Count==0&&ItemRegistry.GetStatus("test.consumer.item")=="Queued","runtime queues dependency graph through shared plan");
Check(ItemRegistry.HasPack("test.base","1.0.0")&&!ItemRegistry.HasPack("test.base","1.0.1"),"runtime stores only accepted content pack version");
LootTables.Stop();ItemRegistry.Stop();ItemRegistry.Start();LootTables.Start();
Check(!ItemRegistry.HasPack("test.base","0.0.0"),"shutdown clears content pack versions");
var direct=Area("direct");var dependency=Pack(direct,"a","test.base");var consumer=Pack(direct,"b","test.consumer",deps:new object[]{new{id="test.base"}});
try {ItemRegistry.RegisterPack(consumer);throw new Exception("missing dependency accepted");}catch(InvalidDataException){Check(ItemRegistry.GetStatus("test.consumer.item")=="Unknown","direct registration refuses unqueued dependencies without reserving items");}
ItemRegistry.RegisterPack(dependency);Check(ItemRegistry.RegisterPack(consumer).All(r=>r.Accepted),"direct registration succeeds after dependency queued");
LootTables.Stop();ItemRegistry.Stop();ItemRegistry.Start();LootTables.Start();
ItemRegistry.Register(new ItemDefinition{Id="test.base.item",Name="preexisting"});runtimeErrors.Clear();ContentPackLoader.LoadAll(graph,runtimeErrors.Add);
Check(runtimeErrors.Count==2&&ItemRegistry.GetStatus("test.consumer.item")=="Unknown"&&ItemRegistry.GetStatus("test.other.item")=="Queued","runtime dependency commit failure blocks consumer while independent pack queues");
Check(!ItemRegistry.HasPack("test.base","0.0.0"),"rejected batch does not publish pack version");
LootTables.Stop();ItemRegistry.Stop();ContentPackLoader.Clear();
var locRoot=Area("localization");var locPack=Pack(locRoot,"localized","test.localized");
var locFolder=Path.Combine(locPack,"Localization");Directory.CreateDirectory(locFolder);
File.WriteAllText(Path.Combine(locPack,"items.json"),"""[{"id":"test.localized.item","modId":"test.localized","localizedName":{"key":"item_name"},"localizedShortDescription":{"key":"summary"},"localizedLongDescription":{"key":"long"},"localizedFlavorText":{"key":"flavor"},"localizedCustomText":{"key":"custom"},"types":["test.localized.category"],"typeNames":[{"id":"test.localized.category","localizedName":{"key":"category"}}],"container":{"width":2,"height":1,"localizedTitle":{"key":"box_title"}},"tooltip":{"localizedSourceName":{"key":"source"},"labels":[{"localizedText":{"key":"label"}}],"lines":[{"localizedText":{"key":"line"}}]}}]""");
File.WriteAllText(Path.Combine(locFolder,"en.csv"),"item_name,English item\nsummary,English summary\nlong,English long text\nflavor,English flavor\ncustom,English custom\ncategory,English category\nbox_title,English storage\nsource,English pack\nlabel,English label\nline,English line\n");
File.WriteAllText(Path.Combine(locFolder,"zh.csv"),"item_name,中文物品\nsummary,中文简介\nlong,中文长文\nflavor,中文风味\ncustom,中文自定义\ncategory,中文分类\nbox_title,中文储物\nsource,中文包名\nlabel,中文标签\nline,中文提示\n");
var localizedPack=ContentPacks.Read(locPack);var localizedItem=localizedPack.Items.Single();
Check(localizedPack.Localization.Index.Files==2,"whole pack exposes locale resource index");
Check(ItemText.Bindings(localizedItem).Count()==10,"ordinary JSON enumerates name, description, type, tooltip and container localization bindings");
ItemRegistry.Start();LootTables.Start();runtimeErrors.Clear();ContentPackLoader.LoadAll(locRoot,runtimeErrors.Add);
Check(ItemText.Name(localizedItem,"en-US")=="English item"&&ItemText.Name(localizedItem,"zh-CN")=="中文物品","whole pack resolves item name by current locale");
Check(ItemText.Description(localizedItem,"en")=="English summary"&&ItemText.Resolve(localizedItem,localizedItem.LocalizedLongDescription,localizedItem.LongDescription,"zh")=="中文长文","short and long descriptions resolve consistently");
Check(ItemText.Resolve(localizedItem,localizedItem.LocalizedFlavorText,localizedItem.FlavorText,"en")=="English flavor"&&ItemText.Resolve(localizedItem,localizedItem.LocalizedCustomText,localizedItem.CustomText,"zh")=="中文自定义","flavor and custom text resolve consistently");
Check(ItemText.Resolve(localizedItem,localizedItem.TypeNames.Single().LocalizedName,localizedItem.TypeNames.Single().Name,"en")=="English category","custom category display uses the owning CSV catalog");
Check(ItemText.Resolve(localizedItem,localizedItem.Container!.LocalizedTitle,localizedItem.Container.Title??"fallback","zh")=="中文储物","container title resolves from the owning CSV catalog");
Check(ItemText.Resolve(localizedItem,localizedItem.Tooltip.LocalizedSourceName,localizedItem.Tooltip.SourceName,"en")=="English pack"&&ItemText.Resolve(localizedItem,localizedItem.Tooltip.Labels.Single().LocalizedText,localizedItem.Tooltip.Labels.Single().Text,"zh")=="中文标签"&&ItemText.Resolve(localizedItem,localizedItem.Tooltip.Lines.Single().LocalizedText,localizedItem.Tooltip.Lines.Single().Text,"en")=="English line","source, tooltip labels and lines share the locale resolver");
Check(ContentPacks.Check(locPack).Valid,"all bound JSON fields require resolvable English CSV keys before pack acceptance");
Check(runtimeErrors.Count==0&&Localization.GetPack("test.localized")?.Resolve("zh","item_name","fallback").Text=="中文物品","accepted runtime pack publishes managed translations for JSON-bound keys");
LootTables.Stop();ItemRegistry.Stop();Check(Localization.GetPack("test.localized") is null,"registry shutdown clears locale snapshots");
File.AppendAllText(Path.Combine(locFolder,"zh.csv"),"item_name,重复\n");
Check(!ContentPacks.Check(locPack).Valid,"bad CSV rejects whole pack offline");
ItemRegistry.Start();LootTables.Start();runtimeErrors.Clear();ContentPackLoader.LoadAll(locRoot,runtimeErrors.Add);
Check(runtimeErrors.Count==1&&ItemRegistry.GetStatus("test.localized.item")=="Unknown"&&Localization.GetPack("test.localized") is null,"bad CSV cannot partially queue items or translations");
LootTables.Stop();ItemRegistry.Stop();ContentPackLoader.Clear();
File.WriteAllText(Path.Combine(locFolder,"zh.csv"),"item_name,中文物品\nsummary,中文简介\nlong,中文长文\nflavor,中文风味\ncustom,中文自定义\ncategory,中文分类\nbox_title,中文储物\nsource,中文包名\nlabel,中文标签\nline,中文提示\n");ItemRegistry.Start();LootTables.Start();
ItemRegistry.Register(new ItemDefinition{Id="test.localized.item",Name="conflict"});ContentPackLoader.LoadAll(locRoot,_=>{});
Check(Localization.GetPack("test.localized") is null,"rejected item batch does not publish locale catalog");
LootTables.Stop();ItemRegistry.Stop();ContentPackLoader.Clear();
var report=new{passed=cases.Count,cases,nativeRuntimeTest=false,scope="real shared manifest/content/PNG/graph and registry/loader sources; Unity/native hooks/assets are stubs; no native decode/directory/gameplay proof"};
Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-pack-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));
Directory.Delete(scratch,true);

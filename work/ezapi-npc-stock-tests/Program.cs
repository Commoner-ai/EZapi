using EZapi;
using System.Text;
using System.Text.Json;
var cases=new List<object>();int passed=0,failed=0;
void Test(string name,Func<bool> test){try{if(!test())throw new Exception("assertion false");cases.Add(new{name,passed=true});passed++;}catch(Exception ex){cases.Add(new{name,passed=false,error=ex.ToString()});failed++;}}
bool Throws(Action action,string code){try{action();return false;}catch(Exception ex){return ex.Message.Contains(code,StringComparison.Ordinal);}}
NpcStockCandidate C(NpcTradeStockOptions? options=null,bool owned=false)=>new("author.test","author.test.item",options??new(NpcTradeStockCategory.Material,6),owned);
NpcStockContext Context()=>new(NpcTradeStockCategory.Material,NpcTradeStockSupplier.Inventor,4);
NpcStockPool Pool(NpcTradeStockOptions? options=null,NpcStockContext? context=null,bool owned=false,IReadOnlyList<NpcStockEntry>? baseline=null)
    =>NpcStockPlanner.Build(context??Context(),baseline??new[]{new NpcStockEntry("iron",2),new NpcStockEntry("copper",4)},new[]{C(options,owned)});
ItemDefinition Parse(string npc){using var stream=new MemoryStream(Encoding.UTF8.GetBytes("[{\"id\":\"author.test.item\",\"name\":\"test\",\"npcTrade\":"+npc+"}]"));return JsonItems.Parse(stream).Single();}

Test("weighted pool adds candidate without changing base odds ratio",()=>{var p=Pool();return p.TotalWeight==12&&p.Entries.Count==3&&p.Pick(0)=="iron"&&p.Pick(1.0/6)=="copper"&&p.Pick(.5)=="author.test.item"&&p.Pick(double.BitDecrement(1))=="author.test.item";});
Test("baseline duplicates combine into one interval and retain first provenance",()=>{var p=Pool(baseline:new[]{new NpcStockEntry("iron",1,"first"),new NpcStockEntry("iron",3,"second")});return p.Entries.Count==2&&p.Entries[0].Weight==4&&p.Entries[0].SourceModId=="first";});
Test("zero baseline entries are never selected",()=>{var p=NpcStockPlanner.Build(Context(),new[]{new NpcStockEntry("zero",0),new NpcStockEntry("iron",1)},Array.Empty<NpcStockCandidate>());return p.Pick(0)=="iron"&&p.Pick(.9999)=="iron";});
Test("empty pool returns no ID",()=>NpcStockPlanner.Build(Context(),Array.Empty<NpcStockEntry>(),Array.Empty<NpcStockCandidate>()).Pick(.5)==null);
Test("snapshot does not retain mutable lists",()=>{var baseList=new List<NpcStockEntry>{new("iron",1)};var list=new List<NpcStockCandidate>{C()};var p=NpcStockPlanner.Build(Context(),baseList,list);baseList[0]=new("changed",999);list.Clear();return p.TotalWeight==7&&p.Entries[0].ItemId=="iron"&&p.Entries.Count==2;});
Test("output entries reject writes",()=>{var list=(IList<NpcStockEntry>)Pool().Entries;return Throws(()=>list[0]=new("changed",3),"read-only");});
Test("inputs retain original entries",()=>{var b=new[]{new NpcStockEntry("author.test.item",2)};var c=new[]{C(new(NpcTradeStockCategory.Material,6){PreserveLootTableWeight=false})};var p=NpcStockPlanner.Build(Context(),b,c);return b[0].Weight==2&&c[0].Options.Weight==6&&p.Entries[0].Weight==6;});
Test("preexisting item keeps base weight by default",()=>Pool(baseline:new[]{new NpcStockEntry("author.test.item",2)}).Entries.Single().Weight==2);
Test("explicit override changes weight and source",()=>{var p=Pool(new(NpcTradeStockCategory.Material,6){PreserveLootTableWeight=false},baseline:new[]{new NpcStockEntry("author.test.item",2)});return p.Entries.Single().Weight==6&&p.Entries.Single().SourceModId=="author.test";});
Test("before minimum day excludes new candidate",()=>Pool(new(NpcTradeStockCategory.Material){MinimumDay=5}).Entries.Count==2);
Test("minimum day is inclusive",()=>Pool(new(NpcTradeStockCategory.Material){MinimumDay=4}).Entries.Count==3);
Test("owned flag excluded when requested",()=>Pool(new(NpcTradeStockCategory.Material){SkipWhenOwned=true},owned:true).Entries.Count==2);
Test("owned item still eligible by default",()=>Pool(owned:true).Entries.Count==3);
Test("unowned unique candidate eligible",()=>Pool(new(NpcTradeStockCategory.Material){SkipWhenOwned=true}).Entries.Count==3);
Test("night shop switch excludes only at night",()=>Pool(new(NpcTradeStockCategory.Material){IncludeInNightShop=false},Context() with{NightShop=true}).Entries.Count==2&&Pool(new(NpcTradeStockCategory.Material){IncludeInNightShop=false}).Entries.Count==3);
Test("night shop allowed by default",()=>Pool(context:Context() with{NightShop=true}).Entries.Count==3);
Test("matching supplier accepts",()=>Pool(new(NpcTradeStockCategory.Material){Suppliers=NpcTradeStockSupplier.Inventor}).Entries.Count==3);
Test("different supplier rejects",()=>Pool(new(NpcTradeStockCategory.Material){Suppliers=NpcTradeStockSupplier.Thief}).Entries.Count==2);
Test("supplier flag combination accepts both",()=>Pool(new(NpcTradeStockCategory.Material){Suppliers=NpcTradeStockSupplier.Thief|NpcTradeStockSupplier.Inventor}).Entries.Count==3);
Test("Any declaration accepts Any context",()=>Pool(context:Context() with{Supplier=NpcTradeStockSupplier.Any}).Entries.Count==3);
Test("restricted declaration requires concrete supplier",()=>Pool(new(NpcTradeStockCategory.Material){Suppliers=NpcTradeStockSupplier.Inventor},Context() with{Supplier=NpcTradeStockSupplier.Any}).Entries.Count==2);
Test("miner uses independent weight even with supplier restriction",()=>Pool(new(NpcTradeStockCategory.Material,100){Suppliers=NpcTradeStockSupplier.Thief,MinerWeight=7},Context() with{Miner=true}).Entries.Last().Weight==7);
Test("miner omitted weight disables candidate",()=>Pool(context:Context() with{Miner=true}).Entries.Count==2);
Test("miner explicit zero disables candidate",()=>Pool(new(NpcTradeStockCategory.Material){MinerWeight=0},Context() with{Miner=true}).Entries.Count==2);
Test("miner obeys minimum day and ownership filters",()=>Pool(new(NpcTradeStockCategory.Material){MinerWeight=8,MinimumDay=5},Context() with{Miner=true}).Entries.Count==2&&Pool(new(NpcTradeStockCategory.Material){MinerWeight=8,SkipWhenOwned=true},Context() with{Miner=true},owned:true).Entries.Count==2);
Test("other category leaves original table weight untouched",()=>Pool(new(NpcTradeStockCategory.Food,9){SkipWhenOwned=true},owned:true,baseline:new[]{new NpcStockEntry("author.test.item",2)}).Entries.Single().Weight==2);
foreach(var reason in new[]{"day","owned","night","supplier","miner"})Test("same-category baseline item obeys "+reason+" exclusion",()=>{
 var o=new NpcTradeStockOptions(NpcTradeStockCategory.Material);var context=Context();var owned=false;
 switch(reason){case "day":o=o with{MinimumDay=5};break;case "owned":o=o with{SkipWhenOwned=true};owned=true;break;case "night":o=o with{IncludeInNightShop=false};context=context with{NightShop=true};break;case "supplier":o=o with{Suppliers=NpcTradeStockSupplier.Thief};break;case "miner":context=context with{Miner=true};break;}
 var p=Pool(o,context,owned,new[]{new NpcStockEntry("author.test.item",2)});return p.TotalWeight==0&&p.Pick(.5)==null;
});
foreach(var category in Enum.GetValues<NpcTradeStockCategory>().Where(c=>c!=NpcTradeStockCategory.None))Test("category "+category+" available to explicit planner",()=>Pool(new(category),Context() with{Category=category}).Entries.Count==3);
Test("multi-category candidate applies to either source",()=>Pool(new(NpcTradeStockCategory.Material|NpcTradeStockCategory.Ore)).Entries.Count==3);
Test("None declaration opts out",()=>Pool(new(NpcTradeStockCategory.None)).Entries.Count==2);
foreach(var sample in new[]{double.NaN,double.PositiveInfinity,-.001,1,2})Test("invalid random sample "+sample,()=>Throws(()=>Pool().Pick(sample),"EZAPI_NPC_STOCK_SAMPLE"));
foreach(var weight in new[]{float.NaN,float.PositiveInfinity,-1,0,float.BitIncrement(1000000000f),float.MaxValue})Test("invalid declared weight "+weight,()=>Throws(()=>Pool(new(NpcTradeStockCategory.Material,weight)),"EZAPI_NPC_STOCK_OPTIONS"));
foreach(var weight in new[]{float.NaN,float.PositiveInfinity,-1,float.BitIncrement(1000000000f)})Test("invalid miner weight "+weight,()=>Throws(()=>Pool(new(NpcTradeStockCategory.Material){MinerWeight=weight}),"EZAPI_NPC_STOCK_OPTIONS"));
Test("invalid category bits rejected",()=>Throws(()=>Pool(new((NpcTradeStockCategory)128)),"EZAPI_NPC_STOCK_OPTIONS"));
Test("invalid supplier bits rejected",()=>Throws(()=>Pool(new(NpcTradeStockCategory.Material){Suppliers=(NpcTradeStockSupplier)4}),"EZAPI_NPC_STOCK_OPTIONS"));
Test("negative minimum day rejected",()=>Throws(()=>Pool(new(NpcTradeStockCategory.Material){MinimumDay=-1}),"EZAPI_NPC_STOCK_OPTIONS"));
Test("context None rejected",()=>Throws(()=>Pool(context:Context() with{Category=NpcTradeStockCategory.None}),"EZAPI_NPC_STOCK_CONTEXT"));
Test("context multiple suppliers rejected",()=>Throws(()=>Pool(context:Context() with{Supplier=(NpcTradeStockSupplier)3}),"EZAPI_NPC_STOCK_CONTEXT"));
Test("context negative day rejected",()=>Throws(()=>Pool(context:Context() with{Day=-1}),"EZAPI_NPC_STOCK_CONTEXT"));
foreach(var weight in new[]{float.NaN,float.PositiveInfinity,-1})Test("invalid baseline weight "+weight,()=>Throws(()=>Pool(baseline:new[]{new NpcStockEntry("iron",weight)}),"EZAPI_NPC_STOCK_ENTRY"));
Test("duplicate weight overflow rejected",()=>Throws(()=>Pool(baseline:new[]{new NpcStockEntry("iron",float.MaxValue),new NpcStockEntry("iron",float.MaxValue)}),"EZAPI_NPC_STOCK_WEIGHT"));
Test("distinct native total overflow rejected before roll",()=>Throws(()=>Pool(baseline:new[]{new NpcStockEntry("iron",float.MaxValue),new NpcStockEntry("copper",float.MaxValue)}),"EZAPI_NPC_STOCK_WEIGHT"));
Test("duplicate candidate rejects whole plan",()=>Throws(()=>NpcStockPlanner.Build(Context(),Array.Empty<NpcStockEntry>(),new[]{C(),C()}),"EZAPI_NPC_STOCK_CANDIDATE"));
Test("wrong owner prefix rejected",()=>Throws(()=>NpcStockPlanner.Build(Context(),Array.Empty<NpcStockEntry>(),new[]{C() with{OwnerId="other"}}),"EZAPI_NPC_STOCK_CANDIDATE"));
Test("candidate limit rejected",()=>Throws(()=>NpcStockPlanner.Build(Context(),Array.Empty<NpcStockEntry>(),Enumerable.Repeat(C(),4097).ToArray()),"EZAPI_NPC_STOCK_LIMIT"));
Test("baseline limit rejected",()=>Throws(()=>Pool(baseline:Enumerable.Repeat(new NpcStockEntry("iron",1),4097).ToArray()),"EZAPI_NPC_STOCK_LIMIT"));
Test("baseline exactly limit supported",()=>Pool(baseline:Enumerable.Range(0,4096).Select(i=>new NpcStockEntry("base"+i,1)).ToArray()).Entries.Count==4097);
Test("defaults explicitly documented",()=>{var o=new NpcTradeStockOptions();return o.Category==NpcTradeStockCategory.None&&o.Weight==1&&o.Suppliers==NpcTradeStockSupplier.Any&&o.IncludeInNightShop&&!o.SkipWhenOwned&&o.MinimumDay==0&&o.PreserveLootTableWeight&&o.MinerWeight==null;});
Test("JSON flags and all option fields roundtrip",()=>{var o=Parse("{\"category\":\"Material, Ore\",\"weight\":7,\"suppliers\":\"Thief, Inventor\",\"includeInNightShop\":false,\"skipWhenOwned\":true,\"minimumDay\":3,\"preserveLootTableWeight\":false,\"minerWeight\":2}").NpcTrade!;var json=JsonSerializer.Serialize(o);return o.Category==(NpcTradeStockCategory.Material|NpcTradeStockCategory.Ore)&&o.Weight==7&&o.Suppliers==(NpcTradeStockSupplier)3&&!o.IncludeInNightShop&&o.SkipWhenOwned&&o.MinimumDay==3&&!o.PreserveLootTableWeight&&o.MinerWeight==2&&JsonSerializer.Deserialize<NpcTradeStockOptions>(json)==o;});
Test("JSON case-insensitive declared flags accepted",()=>Parse("{\"category\":\"material, ore\",\"suppliers\":\"inventor\"}").NpcTrade!.Category==(NpcTradeStockCategory.Material|NpcTradeStockCategory.Ore));
foreach(var flag in new[]{"1","\"1\"","\"Unknown\"","\"Material, Material\"","\"None, Ore\"","\"Material,\"","\"\"","null"})Test("JSON bad category "+flag,()=>Throws(()=>Parse("{\"category\":"+flag+"}"),"items[0]"));
foreach(var flag in new[]{"1","\"1\"","\"Unknown\"","\"Thief, Thief\"","\"Any, Inventor\"","null"})Test("JSON bad supplier "+flag,()=>Throws(()=>Parse("{\"category\":\"Material\",\"suppliers\":"+flag+"}"),"items[0]"));
Test("JSON unknown nested property rejected with path",()=>Throws(()=>Parse("{\"category\":\"Material\",\"typo\":7}"),"npcTrade.typo"));
Test("JSON duplicate nested property rejected with path",()=>Throws(()=>Parse("{\"category\":\"Material\",\"weight\":1,\"weight\":2}"),"npcTrade.weight"));
Test("JSON zero weight rejected",()=>Throws(()=>Parse("{\"category\":\"Material\",\"weight\":0}"),"EZAPI_NPC_STOCK_OPTIONS"));
Test("JSON null optional weight rejected",()=>Throws(()=>Parse("{\"category\":\"Ore\",\"minerWeight\":null}"),"EZAPI_JSON_NULL"));
Test("JSON property typo case rejected",()=>Throws(()=>Parse("{\"Category\":\"Material\"}"),"npcTrade.Category"));
Test("JSON undefined enum cannot serialize",()=>Throws(()=>JsonSerializer.Serialize((NpcTradeStockCategory)128),"EZAPI_NPC_STOCK_FLAGS"));
var result=new{passed,failed,cases,scope="real managed stock planner and strict item JSON parser; native RNG/hooks/delivery not exercised"};
Console.WriteLine(JsonSerializer.Serialize(result));Environment.ExitCode=failed==0?0:1;

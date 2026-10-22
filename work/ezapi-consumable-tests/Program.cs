using EZapi;
using Il2Cpp;
using System.Text;
using System.Text.Json;
var cases=new List<string>();
void Check(bool ok,string name) {if(!ok)throw new Exception(name);cases.Add(name);}
IReadOnlyList<ItemDefinition> Parse(string extra) {
    using var s=new MemoryStream(Encoding.UTF8.GetBytes("[{\"id\":\"test.food.item\",\"name\":\"test\","+extra+"}]"));return JsonItems.Parse(s);
}
void Reject(string extra,string name) {try {Parse(extra);throw new Exception("accepted "+name);}catch(InvalidDataException){Check(true,name);}}
var d=Parse("\"food\":{\"calories\":400,\"canned\":true,\"freshness\":10}").Single();
Check(d.Food!.Calories==400&&d.Food.Canned&&d.Food.Freshness==10,"JSON food fields preserve configured values");
Check(Parse("\"liquidContainer\":{}").Single().LiquidContainer is {Capacity:500,StartFull:true},"liquid defaults explicit and stable");
Check(Parse("\"liquidContainer\":{\"capacity\":1250,\"startFull\":false}").Single().LiquidContainer is {Capacity:1250,StartFull:false},"liquid capacity and empty flag preserve configured values");
foreach(var (s,n) in new[]{
    ("\"food\":{}","missing calories rejected"),("\"food\":{\"calories\":0}","zero calories rejected"),
    ("\"food\":{\"calories\":-1}","negative calories rejected"),("\"food\":{\"calories\":100001}","calorie upper bound"),
    ("\"food\":{\"calories\":1.5}","fractional calories rejected"),("\"food\":null","null food rejected"),
    ("\"food\":{\"calories\":1,\"freshness\":-1}","negative freshness rejected"),
    ("\"food\":{\"calories\":1,\"freshness\":101}","freshness upper bound"),
    ("\"food\":{\"calories\":1,\"freshness\":null}","explicit null freshness rejected"),
    ("\"food\":{\"calories\":1,\"calories\":2}","duplicate nested food field rejected"),
    ("\"food\":{\"calories\":1,\"heal\":1}","unknown nested food field rejected"),
    ("\"liquidContainer\":{\"capacity\":0}","zero capacity rejected"),
    ("\"liquidContainer\":{\"capacity\":100001}","capacity upper bound"),
    ("\"liquidContainer\":{\"capacity\":1.5}","fractional capacity rejected"),
    ("\"liquidContainer\":{\"startFull\":1}","wrong liquid boolean type rejected"),
    ("\"liquidContainer\":{\"liquidId\":\"water\"}","unsupported liquid composition rejected"),
    ("\"food\":{\"calories\":1},\"liquidContainer\":{}","two native behaviors rejected"),
    ("\"food\":{\"calories\":1},\"activation\":{\"kind\":\"healWounds\"}","food and healing conflict rejected"),
    ("\"liquidContainer\":{},\"activation\":{\"kind\":\"healWounds\"}","liquid and healing conflict rejected"),
    ("\"food\":{\"calories\":1},\"useCount\":{\"maximum\":3}","food and generic charge conflict rejected"),
    ("\"liquidContainer\":{},\"useCount\":{\"maximum\":3}","liquid and generic charge conflict rejected")})Reject(s,n);
foreach(var f in new[]{0,100})Check(Parse("\"food\":{\"calories\":100000,\"freshness\":"+f+"}").Single().Food!.Freshness==f,"freshness boundary "+f+" accepted");
try {DefinitionRules.Validate(new ItemDefinition{Id="test.item",Name="n",Food=new(){Calories=-1}});throw new Exception("bad C# accepted");}catch(ArgumentException){Check(true,"CSharp fields use same consumable validation");}

var plain=new ItemDefinition{Id="test.plain",Name="plain",Value=25};
var a=NativeItemFactory.Create(plain);
Check(FoodItemHelper.Calls==0&&LiquidContainerHelper.Calls==0,"old ordinary item never invokes new helpers");
Check(!a.activateDefault&&!a.forceDisableUse&&a.bonusAccuracy==0&&!a.canUseOutsideCombat&&a.Value==25,"old ordinary factory defaults retained");
var food=NativeItemFactory.Create(d);
Check(food.Food==(400,true)&&food.Freshness==10,"real factory calls native food and optional freshness helpers");
Check(food.activateDefault&&food.canUseOutsideCombat&&food.bonusAccuracy==7&&food.forceDisableUse,"omitted optional flags preserve helper defaults");
Check(food.Count==1&&!food.mayStackItemFunc!.Invoke(food,food),"native helper items retain single item guard");
var freshCalls=FoodItemHelper.FreshCalls;
var notFresh=NativeItemFactory.Create(new(){Id="test.stable",Name="n",Food=new(){Calories=10}});
Check(FoodItemHelper.FreshCalls==freshCalls&&notFresh.Freshness is null,"omitting freshness does not opt into perishable initialization");
var custom=NativeItemFactory.Create(new(){Id="test.override",Name="n",Value=42,Food=new(){Calories=100},ForceDisableUse=false,ActivateDefault=false,CanUseOutsideCombat=false,BonusAccuracy=0,
    Sounds=new(){Use="custom.use"},Tags=new[]{new ItemTagDefinition{Id="test.quality",IntValue=5}}});
Check(!custom.forceDisableUse&&!custom.activateDefault&&!custom.canUseOutsideCombat&&custom.bonusAccuracy==0,"explicit nullable flag overrides retain zero and false values");
Check(custom.Value==42&&custom.soundUse=="custom.use"&&custom.state!.GetTag("test.quality").Int==5,"declaration price sound and custom tags applied after helper");
Check(custom.state!.Tags.ContainsKey("native.food"),"custom tags do not replace native initialized state");
var second=NativeItemFactory.Create(d);Check(!ReferenceEquals(food,second)&&!ReferenceEquals(food.state,second.state),"separate factory calls never share native state");
var water=NativeItemFactory.Create(new(){Id="test.water",Name="n",LiquidContainer=new(){Capacity=750,StartFull=false}});
Check(water.Liquid==(750,false)&&water.Food is null,"liquid helper receives capacity and empty flag without food callback");
Check(water.activateDefault&&water.canUseOutsideCombat&&water.SyncCount==1,"liquid native defaults preserved then synchronized");
foreach(var stage in new[]{"food","fresh","liquid"}) {
    FoodItemHelper.ThrowFood=stage=="food";FoodItemHelper.ThrowFresh=stage=="fresh";LiquidContainerHelper.Throw=stage=="liquid";
    try {NativeItemFactory.Create(stage=="liquid"?new(){Id="test.fail",Name="n",LiquidContainer=new()}:d);throw new Exception("failure hidden");}
    catch(InvalidOperationException) {Check(ItemDirectory.Created.Last().DestroyCount==1,"factory destroys failed temporary instance: "+stage);}
}
Check(UseCountHelper.Calls==0,"native consumable factory never invokes conflicting generic count helper");
FoodItemHelper.ThrowFood=false;FoodItemHelper.ThrowFresh=false;LiquidContainerHelper.Throw=false;
var meat=Parse("\"food\":{\"calories\":100,\"meat\":true}").Single();
Check(meat.Food!.Meat&&NativeItemFactory.Create(meat).Meat,"JSON meat maps to fourth native isMeat parameter");
Check(!NativeItemFactory.Create(new(){Id="test.no_meat",Name="n",Food=new(){Calories=100}}).Meat,"omitted meat keeps native default false");
var fullWater=NativeItemFactory.Create(new(){Id="test.full_water",Name="n",Value=15,LiquidContainer=new(){Capacity=500,StartFull=true}});
Check(fullWater.Liquid==(500,true),"current WaterHelper initializes before optional native base-water fill");
var report=new{passed=cases.Count,cases,nativeRuntimeTest=false,scope="real item parser, validation and factory sources; native helper/Unity calls are instrumented stubs; no food/liquid effects, native state, freshness progression or persistence proof"};
Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-consumable-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));

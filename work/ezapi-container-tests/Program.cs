using EZapi;
using Il2Cpp;
using System.Text;
using System.Text.Json;
var cases=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
ItemDefinition Parse(string fields){using var f=new MemoryStream(Encoding.UTF8.GetBytes("[{\"id\":\"test.box.item\",\"name\":\"Box\","+fields+"}]"));return JsonItems.Parse(f).Single();}
void Reject(string text,string label){try{Parse(text);throw new Exception("accepted "+label);}catch(InvalidDataException){cases.Add(label);}}
var d=Parse("\"width\":2,\"height\":2,\"container\":{\"width\":4,\"height\":3}");
Check(d.Container is {Width:4,Height:3,Draggable:true}&&d.Width==2,"internal and external dimensions independent; movable default");
var box=NativeItemFactory.Create(d);var grid=box.contentWindow!.Grid!;
Check(grid.Width==4&&grid.Height==3&&grid.Shape is null,"rectangle uses native grid dimensions");
Check(grid.NativeInitialized&&grid.identifier=="test.box.item.storage","native helper and stable child key");
Check(grid.AllowedTags is null&&grid.AllowedIds is null,"omitted filters passed as native null defaults");
Check(box.contentWindow.Title=="Box"&&box.contentWindow.Draggable,"title fallback and movable window");
var second=NativeItemFactory.Create(d);
Check(!ReferenceEquals(box.contentWindow,second.contentWindow)&&!ReferenceEquals(grid,second.contentWindow!.Grid),"each factory creates independent window and grid");
Check(!ReferenceEquals(box.state,second.state),"native container state independent");
Check(box.activateDefault&&box.canUseOutsideCombat,"helper flags preserved when declaration omits flags");
var shaped=NativeItemFactory.Create(Parse("\"container\":{\"width\":2,\"height\":2,\"shapeRows\":[\"10\",\"11\"],\"title\":\"箱子\",\"draggable\":false,\"allowedIds\":[\"processed_cheese\",\"author.mod.item\"]}"));
Check(shaped.contentWindow!.Grid!.Shape=="0100","usable mask inverted into native blocked boundary mask");
Check(shaped.contentWindow.Title=="箱子"&&!shaped.contentWindow.Draggable,"configured title and movable flag retained");
var localizedTitle=NativeItemFactory.Create(Parse("\"container\":{\"width\":1,\"height\":1,\"title\":\"EZapi 储物箱\"}"));
Check(localizedTitle.contentWindow!.Title=="EZapi 储物箱"&&localizedTitle.contentWindow.widthPixels==68&&localizedTitle.contentWindow.titleText.text.fontSize<16,"long CJK container title fits by adapting only its label font");
Check(shaped.contentWindow.Grid.AllowedIds!.SequenceEqual(new[]{"processed_cheese","author.mod.item"})&&shaped.contentWindow.Grid.AllowedTags is {Length:0},"ID filtering closes the unconfigured native OR branch with empty tags");
var tagBox=NativeItemFactory.Create(Parse("\"container\":{\"width\":1,\"height\":1,\"allowedTags\":[\"MEDICAL\",\"native_tag\",\"author.mod.tag\"]}"));
Check(tagBox.contentWindow!.Grid!.AllowedTags!.SequenceEqual(new[]{"MEDICAL","native_tag","author.mod.tag"}),"native case-sensitive tags passed unchanged");
Check(tagBox.contentWindow.Grid.AllowedIds is {Length:0},"tag filtering closes the unconfigured native OR branch with empty IDs");
var callCount=ContainerHelper.Calls;NativeItemFactory.Create(new(){Id="test.plain",Name="plain"});
Check(ContainerHelper.Calls==callCount,"old plain item never calls container helper");
var windowCount=PixelWindow.Created;NativeItemFactory.Create(Parse("\"container\":{\"width\":1,\"height\":1,\"draggable\":false}"));
Check(PixelWindow.Created==windowCount+1,"one window allocation for immovable container");
var overridden=NativeItemFactory.Create(new(){Id="test.override",Name="n",Container=new(){Width=1,Height=1},ActivateDefault=false,CanUseOutsideCombat=false,Tags=new[]{new ItemTagDefinition{Id="test.quality",IntValue=7}},Sounds=new(){ContainerOpen="custom.open"}});
Check(!overridden.activateDefault&&!overridden.canUseOutsideCombat,"explicit false overrides preserved");
Check(overridden.state!.Tags.ContainsKey("container")&&overridden.state.GetTag("test.quality").Int==7&&overridden.soundContainerOpen=="custom.open","custom tags and sound applied after native initialization");
foreach(var (bad,name) in new[]{
 ("{}","required dimensions"),("{\"width\":0,\"height\":1}","zero dimension"),("{\"width\":13,\"height\":1}","dimension bound"),
 ("{\"width\":1.5,\"height\":1}","fractional width"),("{\"width\":1,\"height\":1,\"unknown\":true}","unknown nested field"),
 ("{\"width\":1,\"width\":2,\"height\":1}","duplicate nested field"),("null","explicit null container"),
 ("{\"width\":1,\"height\":1,\"title\":\" \"}","blank title"),("{\"width\":1,\"height\":1,\"title\":\"a\\nb\"}","control title"),
 ("{\"width\":2,\"height\":2,\"shapeRows\":[\"00\",\"00\"]}","empty shape"),
 ("{\"width\":2,\"height\":2,\"shapeRows\":[\"1\",\"11\"]}","mismatched shape width"),
 ("{\"width\":2,\"height\":2,\"shapeRows\":[\"11\"]}","mismatched shape height"),
 ("{\"width\":1,\"height\":1,\"shapeRows\":[\"x\"]}","invalid mask"),
 ("{\"width\":1,\"height\":1,\"allowedTags\":[\"x\",\"x\"]}","duplicate filter"),
 ("{\"width\":1,\"height\":1,\"allowedIds\":[\"../x\"]}","invalid ID filter"),
 ("{\"width\":1,\"height\":1,\"allowedTags\":[\"X\"],\"allowedIds\":[\"x\"]}","ambiguous filter combination"),
 ("{\"width\":1,\"height\":1,\"allowedIds\":null}","null filter"),
 ("{\"width\":1,\"height\":1,\"draggable\":1}","invalid boolean")})Reject("\"container\":"+bad,name);
foreach(var extra in new[]{"\"food\":{\"calories\":1}","\"liquidContainer\":{}","\"useCount\":{\"maximum\":1}","\"activation\":{\"kind\":\"healWounds\"}"})
    Reject("\"container\":{\"width\":1,\"height\":1},"+extra,"conflicting behavior: "+extra);
try{DefinitionRules.Validate(new(){Id="test.csharp",Name="n",Container=new(){Width=-1,Height=1}});throw new Exception("CSharp accepted");}catch(ArgumentException){cases.Add("CSharp uses same validation");}
foreach(var failure in new[]{"cast","attach","helper"}) {
    PixelWindow.BadCast=failure=="cast";PixelWindow.FailAttach=failure=="attach";ContainerHelper.Throw=failure=="helper";
    try{NativeItemFactory.Create(d);throw new Exception("failure hidden");}catch(InvalidOperationException){Check(ItemDirectory.Created.Last().DestroyCount==1,"factory cleans failed instance: "+failure);}
}
PixelWindow.BadCast=PixelWindow.FailAttach=ContainerHelper.Throw=false;
Check(NativeItemFactory.Create(d).contentWindow!.Grid!.NativeInitialized,"later factory can retry after failure");
var report=new{passed=cases.Count,cases,nativeRuntimeTest=false,scope="real parser/rules/factory/container adapter with instrumented native graph calls; no native UI, admission, ownership, nesting or persistence proof"};
Directory.CreateDirectory("work");File.WriteAllText("work/ezapi-container-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));

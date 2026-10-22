using System.Text;
using System.Text.Json;
using EZapi;
using Il2Cpp;

var cases=new List<string>();
void Check(bool condition,string name){if(!condition)throw new Exception(name);cases.Add(name);}
void Reject(Action action,string name){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or JsonException or InvalidDataException){cases.Add(name);return;}throw new Exception("accepted: "+name);}
MachineLayoutDefinition Parse(string json){using var stream=new MemoryStream(Encoding.UTF8.GetBytes(json));return JsonMachineLayout.Parse(stream);}
string Marker(GameItem item)=>item.state!.dict[MachineLayoutApi.LayoutTag].valueString;
int Writes(GameItem item,MachineInventory inventory)=>item.contentWindow!.Writes+(inventory.Battery?.Writes??0)+(inventory.Manual?.Writes??0)+(inventory.Output is GameSlotInventory output?output.Writes:0)+inventory.LiquidInputs.Values.Sum(s=>s.Writes);
var definition=Parse("""
{"localizedTitle":{"chinese":"装配台","english":"Assembler"},
 "output":{"kind":"container","size":{"width":2,"height":2},"localizedLabel":{"chinese":"产物桶","english":"Product vessel"}},
 "battery":{"size":{"width":2,"height":2}},"modules":{},"manualSlot":true,
 "localizedBatteryLabel":{"chinese":"动力槽","english":"Power source"},"localizedManualLabel":{"chinese":"工艺说明","english":"Instructions"},
 "liquidInputs":[{"slotId":"water","localizedLabel":{"chinese":"净水容器","english":"Water vessel"},"allowedIds":["WATER"]}]}
""");
var copy=JsonMachineLayout.Copy(definition);
Check(copy.LocalizedTitle==definition.LocalizedTitle&&copy.Output.LocalizedLabel==definition.Output.LocalizedLabel&&copy.LocalizedBatteryLabel==definition.LocalizedBatteryLabel&&copy.LocalizedManualLabel==definition.LocalizedManualLabel&&copy.LiquidInputs.Single().LocalizedLabel==definition.LiquidInputs.Single().LocalizedLabel,"defensive copy retains every localized declaration");
Check(JsonMachineLayout.Serialize(Parse(JsonMachineLayout.Serialize(copy)))==JsonMachineLayout.Serialize(copy),"localized JSON survives strict save-marker roundtrip");
const string legacy="{\"draggable\":true,\"itemInput\":{\"width\":4,\"height\":4},\"output\":{\"kind\":\"items\",\"size\":{\"width\":4,\"height\":4}},\"liquidInputs\":[],\"manualSlot\":false}";
Check(JsonMachineLayout.Serialize(Parse(legacy))==legacy,"old sparse layout serializes byte-identically without new null fields");
foreach(var key in new[]{"localizedTitle","localizedBatteryLabel","localizedManualLabel"}){
    Reject(()=>Parse("{\""+key+"\":{}}"),"empty localized object rejected: "+key);
    Reject(()=>Parse("{\""+key+"\":{\"english\":null}}"),"explicit null translation rejected: "+key);
    Reject(()=>Parse("{\""+key+"\":{\"english\":\"a\",\"english\":\"b\"}}"),"duplicate translation rejected: "+key);
    Reject(()=>Parse("{\""+key+"\":{\"en\":\"text\"}}"),"unknown locale field rejected: "+key);
}
Reject(()=>JsonMachineLayout.Validate(new(){LocalizedTitle=new(){Key="title"}}),"standalone layout rejects unresolved CSV key before native allocation");
Reject(()=>JsonMachineLayout.Validate(new(){LocalizedTitle=new("only\nlines","English")}),"localized window title rejects control characters");
Reject(()=>JsonMachineLayout.Validate(new(){Output=new(){LocalizedLabel=new(English:new string('a',201))}}),"localized output label enforces destination length");
Reject(()=>JsonMachineLayout.Validate(new(){LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",LocalizedLabel=new(English:" ")}}}),"blank localized liquid label rejected");
Reject(()=>JsonMachineLayout.Validate(new(){LocalizedManualLabel=new(English:"a\tbad")}),"localized default slot label rejects tabs");

MachineLayoutApi.Start();var item=new GameItem();NativeItemLocalization.Locale="en";NativeItemLocalization.Reads=0;
var inventory=MachineLayoutApi.Create(item,definition);
Check(NativeItemLocalization.Reads==1,"creation observes one locale for the whole window");
Check(item.contentWindow!.titleString=="Assembler"&&((GameSlotInventory)inventory.Output).Tooltip=="Product vessel"&&inventory.Battery!.Tooltip=="Power source"&&inventory.Manual!.Tooltip=="Instructions"&&inventory.LiquidInputs["water"].Tooltip=="Water vessel","English factory sets every localized title and slot label after native helpers");
Check(inventory.Battery!.NativeRole=="battery"&&inventory.Modules!.NativeRole=="modules"&&inventory.Manual!.NativeRole=="manual","native helper roles remain installed");
var marker=Marker(item);var syncs=item.Syncs;var window=item.contentWindow;var output=inventory.Output;var filter=inventory.LiquidInputs["water"].mayInventoryAddItemFunc;
NativeItemLocalization.Locale="zh-CN";NativeItemLocalization.Reads=0;
Check(MachineLayoutApi.RefreshLocalization(item),"marked machine refresh succeeds");
Check(NativeItemLocalization.Reads==1,"refresh observes one locale for the whole window");
Check(window.titleString=="装配台"&&((GameSlotInventory)inventory.Output).Tooltip=="产物桶"&&inventory.Battery!.Tooltip=="动力槽"&&inventory.Manual!.Tooltip=="工艺说明"&&inventory.LiquidInputs["water"].Tooltip=="净水容器","existing English machine switches all labels to Chinese");
Check(ReferenceEquals(window,item.contentWindow)&&ReferenceEquals(output,inventory.Output)&&ReferenceEquals(filter,inventory.LiquidInputs["water"].mayInventoryAddItemFunc),"language refresh preserves original window, inventory and admission delegate");
Check(Marker(item)==marker&&item.Syncs==syncs&&item.identifier=="test.mod.machine"&&inventory.RequiresBattery,"language switch leaves persistent marker, stable ID and required-power semantics unchanged");
NativeItemLocalization.Locale="en-US";var writes=Writes(item,inventory);NativeItemLocalization.Reads=0;
Check(MachineLayoutApi.TryGetInventory(item,out var read)&&read.Output==output,"readonly inventory lookup still validates existing graph");
Check(Writes(item,inventory)==writes&&NativeItemLocalization.Reads==0,"TryGetInventory remains a pure layout read without text writes or locale calls");
DeclarativeItemLocalization.Refresh(item);
Check(window.titleString=="Assembler"&&inventory.Battery.Tooltip=="Power source","display/reopen refresh reaches native machines outside declarative ItemRegistry");
Check(Marker(item)==marker&&item.Syncs==syncs,"automatic item refresh does not sync saved machine state");

var defaults=new GameItem();var defaultInventory=MachineLayoutApi.Create(defaults,new(){Output=new(){Kind="container"},Battery=new(new(2,2)),ManualSlot=true});
Check(defaults.contentWindow!.titleString==defaults.name&&((GameSlotInventory)defaultInventory.Output).Tooltip=="Output container"&&defaultInventory.Battery!.Tooltip=="Battery"&&defaultInventory.Manual!.Tooltip=="Manual","English defaults cover framework-owned title fallback and all fixed labels");
NativeItemLocalization.Locale="ZH-hans";MachineLayoutApi.RefreshLocalization(defaults);
Check(((GameSlotInventory)defaultInventory.Output).Tooltip=="产物容器"&&defaultInventory.Battery!.Tooltip=="电池"&&defaultInventory.Manual!.Tooltip=="说明书","Chinese aliases use Chinese framework defaults");
NativeItemLocalization.Locale="fr";MachineLayoutApi.RefreshLocalization(defaults);
Check(defaultInventory.Battery!.Tooltip=="Battery","other locales use English framework defaults");
var literal=new GameItem();var literalInventory=MachineLayoutApi.Create(literal,new(){Title="Fixed title",LiquidInputs=new[]{new MachineContainerSlot{SlotId="water",Label="Literal liquid"}}});
Check(literal.contentWindow!.titleString=="Fixed title"&&literalInventory.LiquidInputs["water"].Tooltip=="Literal liquid","legacy author literals retain their content");
NativeItemLocalization.Locale="zh";MachineLayoutApi.RefreshLocalization(literal);
Check(literal.contentWindow.titleString=="Fixed title"&&literalInventory.LiquidInputs["water"].Tooltip=="Literal liquid","author literals are not guessed or translated");
var singleLanguage=new GameItem();MachineLayoutApi.Create(singleLanguage,new(){LocalizedTitle=new(English:"English only")});
Check(singleLanguage.contentWindow!.titleString=="English only","one-sided title uses existing bilingual fallback contract");

var foreign=new GameItem();foreign.SetContentWindow(new(false,"Foreign window"));
Check(!MachineLayoutApi.RefreshLocalization(foreign)&&foreign.contentWindow!.Writes==0,"unmarked foreign window is untouched");
var broken=new GameItem();broken.state=new();broken.state.GetTag(MachineLayoutApi.LayoutTag).SetString("not JSON");broken.SetContentWindow(new(false,"Original"));
Reject(()=>MachineLayoutApi.RefreshLocalization(broken),"invalid saved marker fails before setters");
Check(broken.contentWindow!.Writes==0,"invalid saved marker preserves existing window text");
inventory.Manual!.ParentOverride=new GameItem();writes=Writes(item,inventory);
Reject(()=>MachineLayoutApi.RefreshLocalization(item),"foreign parent in graph rejects whole refresh");
Check(Writes(item,inventory)==writes,"complete parent validation precedes every title and slot text write");inventory.Manual.ParentOverride=null;
var root=(GridPixelElement)item.contentWindow!.childElement!;var right=(GridPixelElement)root.GetElement(1,0)!;var manual=(GameSlotInventory)right.GetElement(0,2)!;
right.AttachPos((PixelElement)inventory.Output,0,2);
Reject(()=>MachineLayoutApi.RefreshLocalization(item),"replaced manual slot rejects refresh");right.AttachPos(manual,0,2);
GameSession.OnThread=false;writes=Writes(item,inventory);
Reject(()=>MachineLayoutApi.RefreshLocalization(item),"explicit refresh refuses background thread");DeclarativeItemLocalization.Refresh(item);GameSession.OnThread=true;
Check(Writes(item,inventory)==writes,"automatic off-thread refresh performs no native writes");
var warnings=MelonLoader.MelonLogger.Warnings.Count;inventory.Battery.ThrowOnText=true;
DeclarativeItemLocalization.Refresh(item);DeclarativeItemLocalization.Refresh(item);
Check(MelonLoader.MelonLogger.Warnings.Count==warnings+1,"native setter exceptions remain within automatic refresh boundary with deduplicated diagnostic");inventory.Battery.ThrowOnText=false;
MachineLayoutApi.Stop();writes=Writes(item,inventory);
Check(!MachineLayoutApi.RefreshLocalization(item)&&Writes(item,inventory)==writes,"stopped machine layout refresh stays inert");
Console.WriteLine(JsonSerializer.Serialize(new{passed=cases.Count,failed=0,cases,scope="Production layout parser, graph verification, saved-marker handling and display refresh against instrumented native stubs. No actual Harmony installation, game UI, language switch or save/reload evidence."}));

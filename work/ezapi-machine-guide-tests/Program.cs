using EZapi;
using System.Text;
using System.Text.Json;
var cases=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);if(cases.Contains(name))throw new Exception("duplicate test");cases.Add(name);}
void Bad(Action act,string name){try{act();}catch(ArgumentException){Check(true,name);return;}catch(InvalidDataException){Check(true,name);return;}catch(JsonException){Check(true,name);return;}throw new Exception("accepted: "+name);}
MachineLayoutDefinition Layout(MachineGrid? grid=null,MachineOutputTemplate? output=null,IReadOnlyList<MachineContainerSlot>? inputs=null,MachineModuleTemplate? modules=null,string? title=null)=>new(){ItemInput=grid??new(3,4),Output=output??new(),LiquidInputs=inputs??Array.Empty<MachineContainerSlot>(),Modules=modules,Title=title};
MachineLayoutDefinition ParseLayout(string json){using var s=new MemoryStream(Encoding.UTF8.GetBytes(json));return JsonMachineLayout.Parse(s);}
IReadOnlyList<RecipeGuideEntry> ParseGuide(string json){using var s=new MemoryStream(Encoding.UTF8.GetBytes(json));return JsonRecipeGuide.Parse("test.mod",s);}
RecipeGuideEntry Entry(string suffix="recipe",IReadOnlyList<RecipeGuideFigure>? inputs=null)=>new("test.mod","test.mod."+suffix,"NATIVE_MACHINE","测试机器","synthesis",inputs??new[]{new RecipeGuideFigure("a",2)},new("b",1));
var minimal=ParseLayout("{}");Check(minimal.ItemInput==new MachineGrid(4,4)&&minimal.Output.Kind=="items"&&minimal.LiquidInputs.Count==0,"minimal layout defaults");
Check(ParseLayout(JsonMachineLayout.Serialize(minimal)).Battery is null,"minimal saved layout roundtrip omits nullable fields");
Check(ParseLayout(JsonMachineLayout.Serialize(new(){Battery=new(new(2,3)),ManualSlot=true})).Battery!.Size==new MachineGrid(2,3),"sparse saved battery layout roundtrip");
Bad(()=>JsonMachineLayout.Validate(Layout(inputs:Enumerable.Range(0,8).Select(n=>new MachineContainerSlot{SlotId="a"+n,AllowedIds=Enumerable.Range(0,64).Select(i=>new string('x',150)+i).ToArray()}).ToArray())),"aggregate serialized layout size limit before native allocation");
foreach(var n in new[]{1,12}){JsonMachineLayout.Validate(Layout(new(n,n)));Check(true,"layout dimension boundary "+n);}
foreach(var n in new[]{0,-1,13,int.MaxValue})Bad(()=>JsonMachineLayout.Validate(Layout(new(n,1))),"invalid grid "+n);
Bad(()=>JsonMachineLayout.Validate(new(){ItemInput=null!}),"null input grid");
Bad(()=>JsonMachineLayout.Validate(new(){Output=null!}),"null output");
Bad(()=>JsonMachineLayout.Validate(Layout(output:new(){Kind="other"})),"unknown output kind");
Bad(()=>JsonMachineLayout.Validate(new(){Battery=new(new(0,2))}),"invalid battery grid");
Bad(()=>JsonMachineLayout.Validate(Layout(modules:new(){Size=new(13,1)})),"invalid module grid");
Bad(()=>JsonMachineLayout.Validate(Layout(modules:new(){AllowedTypes=new[]{"MODULE\n"}})),"module type trailing newline");
Bad(()=>JsonMachineLayout.Validate(Layout(modules:new(){AllowedTypes=new[]{"x","x"}})),"duplicate module type");
Bad(()=>JsonMachineLayout.Validate(Layout(modules:new(){AllowedTypes=null!})),"null module types");
Bad(()=>JsonMachineLayout.Validate(Layout(title:"  ")),"blank layout title");
Bad(()=>JsonMachineLayout.Validate(Layout(title:"x\n")),"control layout title");
Bad(()=>JsonMachineLayout.Validate(Layout(title:new string('x',201))),"long layout title");
Bad(()=>JsonMachineLayout.Validate(new(){LiquidInputs=null!}),"null liquid slots");
foreach(var id in new[]{"","X","x\n","a.b",new string('a',41)})Bad(()=>JsonMachineLayout.Validate(Layout(inputs:new[]{new MachineContainerSlot{SlotId=id}})),"invalid liquid key "+JsonSerializer.Serialize(id));
Bad(()=>JsonMachineLayout.Validate(Layout(inputs:new[]{new MachineContainerSlot{SlotId="x"},new MachineContainerSlot{SlotId="x"}})),"duplicate liquid key");
Bad(()=>JsonMachineLayout.Validate(Layout(inputs:new[]{new MachineContainerSlot{SlotId="x",AllowedIds=new[]{"a"},AllowedTags=new[]{"TAG"}}})),"two liquid filter kinds");
Bad(()=>JsonMachineLayout.Validate(Layout(inputs:new[]{new MachineContainerSlot{SlotId="x",AllowedIds=new[]{"../x"}}})),"invalid liquid filter");
Bad(()=>JsonMachineLayout.Validate(Layout(inputs:new[]{new MachineContainerSlot{SlotId="x",AllowedTags=new[]{"TAG","TAG"}}})),"duplicate liquid filter");
Bad(()=>JsonMachineLayout.Validate(Layout(inputs:Enumerable.Range(0,9).Select(n=>new MachineContainerSlot{SlotId="a"+n}).ToArray())),"nine liquid slots");
foreach(var json in new[]{"[]","null","{\"title\":null}","{\"typo\":1}","{\"manualSlot\":true,\"manualSlot\":false}","{\"itemInput\":{\"width\":2,\"width\":3,\"height\":2}}","{\"modules\":{\"allowedTypes\":[null]}}","{\"output\":{\"condition\":true}}","{\"liquidInputs\":[null]}"})Bad(()=>ParseLayout(json),"strict layout "+json);
Bad(()=>ParseLayout(new string(' ',65537)),"layout byte limit");
var tags=new List<string>{"WATER"};var liquidSlots=new List<MachineContainerSlot>{new(){SlotId="water",AllowedTags=tags}};
var original=Layout(inputs:liquidSlots);var copied=JsonMachineLayout.Copy(original);tags.Clear();liquidSlots.Clear();
Check(copied.LiquidInputs.Count==1&&copied.LiquidInputs[0].AllowedTags.Single()=="WATER","layout copy isolates nested lists");
Check(copied.LiquidInputs is not MachineContainerSlot[],"layout snapshot cannot expose mutable array");
RecipeGuideApi.Start();Check(RecipeGuideApi.RegisterRecipes("test.mod",new[]{Entry()}).Accepted,"guide register");
var initial=RecipeGuideApi.Snapshot();Check(initial.Recipes.Count==1,"guide snapshot has entry");
var badBatch=RecipeGuideApi.RegisterRecipes("test.mod",new[]{Entry("valid"),Entry() with {RecipeId="wrong.mod.id"}});
Check(!badBatch.Accepted&&RecipeGuideApi.Snapshot().Revision==initial.Revision&&RecipeGuideApi.Snapshot().Recipes.Single().RecipeId=="test.mod.recipe","invalid later row leaves complete earlier catalog intact");
var figures=new List<RecipeGuideFigure>{new("x",7)};RecipeGuideApi.RegisterRecipes("test.mod",new[]{Entry(inputs:figures)});figures.Clear();
Check(RecipeGuideApi.Snapshot().Recipes.Single().Inputs.Count==1,"guide defensive input copy");
var retained=RecipeGuideApi.Snapshot();RecipeGuideApi.RegisterRecipes("test.mod",new[]{Entry("replacement")});
Check(retained.Recipes.Single().RecipeId=="test.mod.recipe"&&RecipeGuideApi.Snapshot().Recipes.Single().RecipeId=="test.mod.replacement","old snapshot remains immutable after replacement");
Check(RecipeGuideApi.RegisterRecipes("other.mod",new[]{Entry() with {OwnerId="other.mod",RecipeId="other.mod.recipe"}}).Accepted,"second owner independent");
RecipeGuideApi.RegisterRecipes("test.mod",Array.Empty<RecipeGuideEntry>());Check(RecipeGuideApi.Snapshot().Recipes.Single().OwnerId=="other.mod","empty batch removes only caller owner");
foreach(var owner in new[]{"A","../x","test.mod\n",new string('a',161)})Bad(()=>RecipeGuideRules.Validate(owner,Array.Empty<RecipeGuideEntry>()),"invalid guide owner "+JsonSerializer.Serialize(owner));
var invalidEntries=new Dictionary<string,RecipeGuideEntry>{
 ["wrong owner"]=Entry() with {OwnerId="other"},["no prefix"]=Entry() with {RecipeId="test.modx.id"},["newline recipe"]=Entry() with {RecipeId="test.mod.x\n"},
 ["invalid machine"]=Entry() with {MachineId="../x"},["empty category"]=Entry() with {CategoryId=""},["blank name"]=Entry() with {MachineName=" "},["control note"]=Entry() with {Note="x\0"},
 ["long note"]=Entry() with {Note=new string('x',4001)},["null inputs"]=Entry() with {Inputs=null!},["null output"]=Entry() with {Output=null!},
 ["zero output"]=Entry() with {Output=new("x",0)},["negative output"]=Entry() with {Output=new("x",-1)},["excess output"]=Entry() with {Output=new("x",1000000001)},
 ["invalid figure ID"]=Entry() with {Output=new("x\n",1)},["blank figure name"]=Entry() with {Output=new("x",1,Name:" ")},["long requirement"]=Entry() with {Output=new("x",1,Requirement:new string('x',1001))},
 ["null figure"]=Entry(inputs:new RecipeGuideFigure[]{null!}),["too many figures"]=Entry(inputs:Enumerable.Range(0,65).Select(_=>new RecipeGuideFigure("x",1)).ToArray())};
foreach(var pair in invalidEntries)Bad(()=>RecipeGuideRules.Validate("test.mod",new[]{pair.Value}),"guide "+pair.Key);
Bad(()=>RecipeGuideRules.Validate("test.mod",new[]{Entry(),Entry()}),"duplicate guide recipe");
Bad(()=>RecipeGuideRules.Validate("test.mod",Enumerable.Range(0,513).Select(i=>Entry("r"+i)).ToArray()),"guide owner quota");
RecipeGuideRules.Validate("test.mod",new[]{Entry(inputs:Array.Empty<RecipeGuideFigure>()) with {Output=new("x",1000000000),Note="多行\n制表\t文字"}});Check(true,"guide boundary amount and free recipe accepted");
var validJson=JsonSerializer.Serialize(new[]{Entry()},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull});Check(ParseGuide(validJson).Single().RecipeId==Entry().RecipeId,"guide JSON roundtrip");
foreach(var json in new[]{"{}","null","[null]",validJson.Replace("\"amount\":2","\"amount\":2,\"amount\":3"),validJson.Replace("\"machineName\":","\"typo\":1,\"machineName\":"),validJson.Replace("\"inputs\":[","\"inputs\":null,\"unused\":[")})Bad(()=>ParseGuide(json),"strict guide "+json);
Bad(()=>ParseGuide(new string(' ',2*1024*1024+1)),"guide byte limit");
RecipeGuideApi.Stop();RecipeGuideApi.Start();
foreach(var owner in new[]{"a","b","c","d"})Check(RecipeGuideApi.RegisterRecipes(owner,Enumerable.Range(0,512).Select(i=>Entry("r"+i) with {OwnerId=owner,RecipeId=owner+".r"+i}).ToArray()).Accepted,"fill guide quota "+owner);
var full=RecipeGuideApi.Snapshot();Check(full.Recipes.Count==2048,"global guide quota full");
Check(!RecipeGuideApi.RegisterRecipes("extra",new[]{Entry() with {OwnerId="extra",RecipeId="extra.x"}}).Accepted&&RecipeGuideApi.Snapshot().Revision==full.Revision,"global quota rejects atomically");
Check(RecipeGuideApi.RegisterRecipes("a",new[]{Entry() with {OwnerId="a",RecipeId="a.replaced"}}).Accepted&&RecipeGuideApi.Snapshot().Recipes.Count==1537,"owner replacement excludes previous quota usage");
GameSession.OnThread=false;try{RecipeGuideApi.Snapshot();throw new Exception("wrong-thread accepted");}catch(InvalidOperationException){cases.Add("wrong thread blocked");}finally{GameSession.OnThread=true;}
RecipeGuideApi.Open();RecipeGuideApi.Stop();Check(!RecipeGuideApi.IsVisible&&RecipeGuideApi.Snapshot().Recipes.Count==0,"stop closes browser and clears catalog");
Check(!RecipeGuideApi.RegisterRecipes("test.mod",new[]{Entry()}).Accepted&&!RecipeGuideApi.Open(),"stopped API cannot register or open");
Check(RecipeGuideText.Pages("",4,2).SequenceEqual(new[]{""}),"empty guide text has one page");
Check(RecipeGuideText.Pages("abcdefghijk",4,2).SequenceEqual(new[]{"abcd\nefgh\n","ijk"}),"text wraps and paginates within grid");
Check(string.Concat(RecipeGuideText.Pages("甲😀乙😀丙",1,1)).Replace("\n","")=="甲😀乙😀丙","text preserves Unicode scalar boundaries");
Check(RecipeGuideText.Pages("a\r\nb\rc",8,2).SequenceEqual(new[]{"a\nb\n","c"}),"text normalizes newline variants");
Check(RecipeGuideText.Pages(new string('\n',20),10,4).Length==5,"blank lines count toward page height");
Bad(()=>RecipeGuideText.Pages("x",0,1),"invalid text columns rejected");
Bad(()=>RecipeGuideText.Pages("x",1,0),"invalid text line count rejected");

var bilingual=Entry() with {
 LocalizedMachineName=new("压制机","Press"),LocalizedNote=new("说明","Notes"),
 Inputs=new[]{new RecipeGuideFigure("a",2){LocalizedName=new("材料","Material"),LocalizedRequirement=new("拥有","Owned"),LocalizedQuantity=new("两份","Two units")}},
 Output=new("b",1){LocalizedName=new("成品","Product")}
};
RecipeGuideRules.Validate("test.mod",new[]{bilingual});
foreach(var code in new[]{"zh","zh-CN","ZH-hans","en","en-US","fr","unknown"}){
 var view=RecipeGuideRules.ResolveLocale(bilingual,code);var zh=code.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
 Check(view.MachineName==(zh?"压制机":"Press")&&view.Note==(zh?"说明":"Notes")&&view.Inputs[0].Name==(zh?"材料":"Material")&&view.Inputs[0].Requirement==(zh?"拥有":"Owned")&&view.Inputs[0].Quantity==(zh?"两份":"Two units")&&view.Output.Name==(zh?"成品":"Product"),"all guide fields resolve "+code);
 Check(view.MachineId==bilingual.MachineId&&view.Inputs[0].Amount==2&&view.Output.ItemId=="b","locale preserves stable IDs/counts "+code);
}
Check(bilingual.MachineName=="测试机器"&&bilingual.Inputs[0].Name is null&&bilingual.LocalizedNote?.Chinese=="说明","resolve preserves catalog source and descriptors");
Check(RecipeGuideRules.ResolveLocale(Entry(),"en").MachineName=="测试机器","legacy fixed string remains literal in any language");
Check(RecipeGuideRules.ResolveLocale(Entry() with{LocalizedMachineName=new("中文")},"en").MachineName=="中文","missing English falls back to Chinese");
Check(RecipeGuideRules.ResolveLocale(Entry() with{LocalizedMachineName=new(English:"English")},"zh").MachineName=="English","missing Chinese falls back to English");
foreach(var text in new[]{new LocalizedItemText(),new(" ","English"),new("中文",new string('x',201)),new("bad\0","English")}){
 var capture=text;Bad(()=>RecipeGuideRules.Validate("test.mod",new[]{Entry() with{LocalizedMachineName=capture}}),"invalid typed guide text "+JsonSerializer.Serialize(text));
}
var jsonBilingual=JsonSerializer.Serialize(new[]{bilingual},new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull});
Check(RecipeGuideRules.ResolveLocale(ParseGuide(jsonBilingual)[0],"en").Output.Name=="Product","strict JSON bilingual roundtrip");
foreach(var bad in new[]{"{}","{\"english\":null}","{\"english\":\" \"}","{\"typo\":\"x\"}","{\"english\":\"x\",\"english\":\"y\"}"}){
 var json="[{\"ownerId\":\"test.mod\",\"recipeId\":\"test.mod.a\",\"machineId\":\"m\",\"machineName\":\"M\",\"localizedMachineName\":"+bad+",\"categoryId\":\"x\",\"inputs\":[],\"output\":{\"itemId\":\"x\",\"amount\":1}}]";
 Bad(()=>ParseGuide(json),"invalid localized JSON "+bad);
}

Check(string.Join("",RecipeGuideText.Pages("This entry is display only.",8,20)).Contains("\nentry",StringComparison.Ordinal)&&string.Join("",RecipeGuideText.Pages("This entry is display only.",8,20)).Contains("display",StringComparison.Ordinal),"English words move intact to next line");
Check(string.Join("",RecipeGuideText.Pages("abcdefghijklmnop",5,20)).Replace("\n","")=="abcdefghijklmnop","long identifier remains splittable without loss");
var report=new{passed=cases.Count,failed=0,cases,nativeRuntimeVerified=false,scope="real shared DTO/JSON rules and recipe catalog; game thread/browser are explicit offline stubs; no native layout execution"};
File.WriteAllText("work/ezapi-machine-guide-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));



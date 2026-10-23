using System.Text;
using System.Text.Json;
using EZapi;
using Il2Cpp;
var cases=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
void Reject(Action action,string name){try{action();throw new Exception("accepted: "+name);}catch(Exception e)when(e is ArgumentException or InvalidDataException){cases.Add(name);}}
ItemDefinition Parse(string json){using var stream=new MemoryStream(Encoding.UTF8.GetBytes(json));return JsonItems.Parse(stream).Single();}
LocalizationTable Table(string locale,string csv){using var stream=new MemoryStream(Encoding.UTF8.GetBytes(csv));return Localization.Parse(stream,locale);}
var catalog=new LocalizationCatalog(new Dictionary<string,LocalizationTable>{
    ["en"]=Table("en","name,Storage Box\nsummary,English summary\nquality,Quality\nline,English note\nsource,Sample Pack\ntitle,Box Contents\n"),
    ["zh"]=Table("zh","name,储物箱\nsummary,中文简介\nquality,品质\nline,中文提示\nsource,示例包\ntitle,箱内物品\n"),
    ["ptBR"]=Table("ptBR","name,Caixa\n")});
Localization.CommitPack("test.mod",catalog);
var titleWindow=new Il2Cpp.PixelWindow{titleString="EZapi"};
var titleHeight=titleWindow.heightPixels;
Check(NativeWindowTitles.Set(titleWindow,"EZapi 储物箱")&&titleWindow.titleString=="EZapi 储物箱"&&titleWindow.titleText.text.fontSize<16&&titleWindow.widthPixels==68&&titleWindow.heightPixels==titleHeight,"native title fit adapts only the owned label font and preserves window geometry");
Check(NativeWindowTitles.Set(titleWindow,"EZapi Storage Box")&&titleWindow.titleString=="EZapi Storage Box"&&titleWindow.titleText.text.fontSize>=6,"same window measures the current locale font afresh instead of inheriting the previous fit");
titleWindow.titleText.text.enableAutoSizing=true;titleWindow.titleText.text.fontSizeMax=40;
Check(NativeWindowTitles.Set(titleWindow,"EZapi Storage Box")&&titleWindow.titleString=="EZapi Storage Box"&&!titleWindow.titleText.text.enableAutoSizing&&titleWindow.titleText.text.fontSize<16,"loaded autosizing label measures fixed trial sizes rather than its previous oversized maximum");
titleWindow.widthPixels=40;
Check(NativeWindowTitles.Set(titleWindow,"EZapi Storage Box")&&titleWindow.titleText.rectTransform.localScale.x is >=.75f and <1f&&titleWindow.widthPixels==40,"bounded horizontal title fit at minimum font leaves native window width unchanged");
titleWindow.widthPixels=68;titleWindow.titleText.text.enableAutoSizing=true;
var oversizedTitle=new string('中',200);
Check(!NativeWindowTitles.Set(titleWindow,oversizedTitle)&&titleWindow.titleText.text.fontSize==16&&titleWindow.titleText.text.enableAutoSizing&&titleWindow.widthPixels==68&&titleWindow.heightPixels==titleHeight,"oversized localized title restores original font/autosizing and preserves native ellipsis");
Check(!NativeWindowTitles.Set(new Il2Cpp.PixelWindow{Pointer=IntPtr.Zero},"Title")&&!NativeWindowTitles.Set(null,"Title"),"missing and destroyed windows never invoke native setters");
var d=Parse("""
[{"id":"test.mod.box","modId":"test.mod","localizedName":{"key":"name"},
"localizedShortDescription":{"key":"summary"},"localizedLongDescription":{"chinese":"中文长简介","english":"English long description"},
"localizedFlavorText":{"chinese":"中文风味","english":"English flavor"},"localizedCustomText":{"chinese":"中文自定义","english":"English custom"},
"container":{"width":1,"height":1,"localizedTitle":{"key":"title"}},
"tags":[{"id":"test.mod.quality","intValue":3}],
"tooltip":{"localizedSourceName":{"key":"source"},"labels":[{"localizedText":{"key":"quality"},"tagId":"test.mod.quality","valueKind":"int"}],
"lines":[{"localizedText":{"key":"line"}}]}}]
""");
ItemText.ValidateCatalog(d,catalog);Check(ItemText.Bindings(d).Count()==9,"all ordinary item text surfaces enumerate their actual bindings");
Check(ItemText.Name(d,"en-US")=="Storage Box"&&ItemText.Name(d,"zh-CN")=="储物箱","CSV locale aliases resolve without changing ID");
Check(ItemText.Name(d,"pt-BR")=="Caixa"&&ItemText.Name(d,"pt_BR")=="Caixa","both Brazilian Portuguese locale aliases use the official ptBR table");
Check(ItemText.Name(d,"fr")=="Storage Box","missing translation uses required English key");
Check(ItemText.Description(d,"zh")=="中文简介","registration description lookup follows the same CSV path");
Reject(()=>Parse("""[{"id":"test.bad","localizedName":{"key":"name"}}]"""),"CSV item binding requires modId");
Reject(()=>Parse("""[{"id":"test.bad","name":"x","localizedName":{}}]"""),"empty localization binding rejects");
Reject(()=>Parse("""[{"id":"test.bad","name":"x","localizedName":{"english":" "}}]"""),"blank provided translation rejects");
Reject(()=>Parse("""[{"id":"test.bad","name":"x","localizedShortDescription":{"chinese":"","english":""}}]"""),"JSON description retains its readable-translation declaration contract");
Reject(()=>Parse("""[{"id":"test.bad","name":"x","localizedFlavorText":{}}]"""),"JSON empty binding remains invalid rather than gaining DLL no-override semantics");
Reject(()=>Parse("""[{"id":"test.bad","name":"x","localizedName":{"english":"a","english":"b"}}]"""),"duplicate language field rejects");
Reject(()=>Parse("""[{"id":"test.bad","name":"x","localizedName":{"en":"a"}}]"""),"unknown locale field rejects with strict parser");
Reject(()=>Parse("""[{"id":"test.bad","name":"x","localizedName":{"english":null}}]"""),"explicit null translation rejects");
Reject(()=>Parse("""[{"id":"test.bad","name":"x","localizedDescription":{"english":"a"},"localizedShortDescription":{"english":"b"}}]"""),"ambiguous localized short description aliases reject");
Reject(()=>ItemText.ValidateCatalog(d,new LocalizationCatalog(new Dictionary<string,LocalizationTable>{{"zh",Table("zh","name,仅中文")}})),"Chinese-only CSV does not silently satisfy English compatibility");
var missing=new ItemDefinition{Id="test.mod.missing",ModId="test.mod",LocalizedName=new(){Key="missing"}};
Reject(()=>ItemText.ValidateCatalog(missing,catalog),"missing English CSV key rejects before registration");
var formatCatalog=new LocalizationCatalog(new Dictionary<string,LocalizationTable>{{"en",Table("en","name,Name {0}")}});
Reject(()=>ItemText.ValidateCatalog(new(){Id="test.mod.format",ModId="test.mod",LocalizedName=new(){Key="name"}},formatCatalog),"item CSV fields reject unprovided format arguments");
var longCatalog=new LocalizationCatalog(new Dictionary<string,LocalizationTable>{{"en",Table("en","name,"+new string('n',201))}});
Reject(()=>ItemText.ValidateCatalog(new(){Id="test.mod.long",ModId="test.mod",LocalizedName=new(){Key="name"}},longCatalog),"CSV field length uses the destination property bound");
var typed=Parse("""[{"id":"test.typed","localizedName":{"chinese":"内联名称","english":"Inline name"},"types":["test.type"],"typeNames":[{"id":"test.type","localizedName":{"chinese":"类型","english":"Type"}}]}]""");
Check(ItemText.Name(typed,"en")=="Inline name"&&ItemText.Name(typed,"zh")=="内联名称","typed bilingual-only item does not require a dummy literal name or catalog");
ItemRegistry.Definitions[d.Id]=d;
var item=new GameItem{identifier=d.Id,identifierName="储物箱",name="储物箱",contentWindow=new(){titleString="箱内物品"}};
item.Tags.Add("test.mod.quality",new(){Value=3});
NativeItemLocalization.CurrentLocaleCode="en";DeclarativeItemLocalization.Refresh(item);
Check(item.name=="Storage Box"&&item.identifierName=="Storage Box"&&item.identifier==d.Id,"existing Chinese-generated name refreshes to English without changing stable ID");
Check(item.shortDescription=="English summary"&&item.longDescription=="English long description"&&item.flavorText=="English flavor"&&item.customText=="English custom","all native description fields refresh to English");
Check(item.contentWindow.titleString=="Box Contents","existing container title refreshes before reuse");
var writes=item.NameWrites;var titleWrites=item.contentWindow.Writes;DeclarativeItemLocalization.Refresh(item);
Check(item.NameWrites==writes&&item.contentWindow.Writes==titleWrites,"repeat refresh avoids duplicate name/title mutations");
var builder=new RichTextBuilder();ItemPresentation.Early(builder,item);ItemPresentation.Late(builder,item);
Check(builder.Lines.SequenceEqual(new[]{"Quality = 3","English note","Source: Sample Pack"}),"official tooltip path resolves CSV labels, notes and translated source exactly once");
item.Tags["test.mod.quality"].Value=913;builder=new();ItemPresentation.Late(builder,item);
Check(builder.Lines.Contains("Quality = 913"),"localization preserves live tag values");
item.Tags["test.mod.quality"].Enabled=false;builder=new();ItemPresentation.Late(builder,item);
Check(!builder.Lines.Any(l=>l.StartsWith("Quality")),"disabled tag remains hidden after localization");
NativeItemLocalization.CurrentLocaleCode="zh-CN";builder=new();ItemPresentation.Early(builder,item);ItemPresentation.Late(builder,item);
Check(item.name=="储物箱"&&item.shortDescription=="中文简介"&&item.contentWindow.titleString=="箱内物品"&&builder.Lines.Contains("来源：示例包"),"switch back restores Chinese native fields and source prefix");
item.name="Player's own box";item.identifierName="Player's own identifier name";NativeItemLocalization.CurrentLocaleCode="en";writes=item.NameWrites;
DeclarativeItemLocalization.Refresh(item);
Check(item.name=="Player's own box"&&item.identifierName=="Player's own identifier name"&&item.NameWrites==writes,"player custom name and its identifier-name field are preserved");
Check(item.shortDescription=="English summary","player rename does not freeze other localized description fields");
var foreign=new GameItem{identifier="other.mod.box",name="Foreign",shortDescription="Foreign description"};DeclarativeItemLocalization.Refresh(foreign);
Check(foreign.name=="Foreign"&&foreign.shortDescription=="Foreign description","foreign item fields remain untouched");
ItemRegistry.Applied=false;item.shortDescription="unapplied";DeclarativeItemLocalization.Refresh(item);
Check(item.shortDescription=="unapplied","queued or failed declarations do not mutate live items");ItemRegistry.Applied=true;
GameSession.OnThread=false;DeclarativeItemLocalization.Refresh(item);Check(item.shortDescription=="unapplied","off-thread refresh never calls native setters");GameSession.OnThread=true;
item.Pointer=IntPtr.Zero;DeclarativeItemLocalization.Refresh(item);Check(item.shortDescription=="unapplied","destroyed wrapper is ignored");item.Pointer=new(1);
var early=new ItemDefinition{Id="test.mod.early",ModId="test.mod",Name="Legacy name",LocalizedName=new("中文名称","English name"),DescriptionPlacement="early",LocalizedShortDescription=new("中文早提示","English early hint")};
ItemRegistry.Definitions[early.Id]=early;var earlyItem=new GameItem{identifier=early.Id,name="Legacy name"};builder=new();ItemPresentation.Early(builder,earlyItem);ItemPresentation.Late(builder,earlyItem);
Check(earlyItem.name=="English name"&&earlyItem.shortDescription==""&&builder.Lines.Count(l=>l=="English early hint")==1,"early placement stays in callback and never duplicates native description");
var module=new ItemDefinition{Id="test.mod.module",Name="Module",Module=new(){LocalizedAdditionalEffectDisplay=new("中文模块说明","English module description")},Tooltip=new(){ShowSource=false}};
ItemRegistry.Definitions[module.Id]=module;var moduleItem=new GameItem{identifier=module.Id};builder=new();ItemPresentation.Late(builder,moduleItem);
Check(builder.Lines.SequenceEqual(new[]{"English module description"}),"localized module description is emitted through the live official tooltip callback");
NativeItemLocalization.CurrentLocaleCode="zh";builder=new();ItemPresentation.Late(builder,moduleItem);
Check(builder.Lines.SequenceEqual(new[]{"中文模块说明"}),"module text follows a language change without recreating or reinitializing module stats");
GameBuildFingerprint.IsKnown=false;GameSession.OnThread=false;DeclarativeItemLocalization.DirectoryReady();
Check(!DeclarativeItemLocalization.Status().Ready,"off-thread directory callback does not install localization hooks");GameSession.OnThread=true;
NativeHookGroup.NativeTargetsAvailable=false;DeclarativeItemLocalization.DirectoryReady();
Check(!DeclarativeItemLocalization.Status().Ready,"unavailable required native targets keep localization hooks disabled");
NativeHookGroup.NativeTargetsAvailable=true;DeclarativeItemLocalization.DirectoryReady();
Check(DeclarativeItemLocalization.Status().Ready,"unknown wrapper fingerprint permits localization hooks after target checks succeed");
item.name="储物箱";item.identifierName="储物箱";item.RestoreContentWindowPosition();
Check(item.contentWindow.titleString=="箱内物品","native reopen compact-name overwrite is followed by the installed localized-title callback");
NativeItemLocalization.CurrentLocaleCode="en";item.RestoreContentWindowPosition();
Check(item.contentWindow.titleString=="Box Contents","same reopened container refreshes back to English after native title overwrite");
DeclarativeItemLocalization.Stop();Check(!DeclarativeItemLocalization.Status().Ready,"stop clears the group readiness");
Localization.ClearPacks();
var report=new{passed=cases.Count,failed=0,cases,nativeRuntimeTest=false,scope="Actual strict item parser, CSV lookup, existing-item refresh and official tooltip adapter with instrumented native fields. No actual Harmony, language switch, UI or save/reload proof."};
File.WriteAllText("work/ezapi-item-localization-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed,report.scope}));

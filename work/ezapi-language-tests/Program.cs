using EZapi;
using Il2Cpp;
using System.Text;
using System.Text.Json;
var cases=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);cases.Add(name);}
void Reject(Action run,string name){try{run();}catch(ArgumentException){cases.Add(name);return;}throw new Exception("Expected rejection: "+name);}
var text=new LocalizedItemText("维修顾问","Repair adviser");
var english=GameLanguage.Current;
Check(!english.IsFallback&&english.LocaleCode=="en"&&!english.IsChinese&&english.Reason is null,"native English snapshot");
Check(GameLanguage.Resolve(text)=="Repair adviser","dynamic text follows native English");
LocHelper.Locale="zh";var chinese=GameLanguage.Current;
Check(!chinese.IsFallback&&chinese.IsChinese&&chinese.Resolve(text)=="维修顾问","native Chinese snapshot and typed text");
Check(english.Resolve(text)=="Repair adviser"&&GameLanguage.Resolve(text)=="维修顾问","retained snapshot stable while dynamic resolver refreshes");
foreach(var locale in new[]{"zh-CN","ZH_hant","fr","ptBR","en-US"}) {
    LocHelper.Locale=locale;var current=GameLanguage.Current;
    Check(!current.IsFallback&&current.LocaleCode==locale&&current.Resolve(text)==(locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"维修顾问":"Repair adviser"),"native readable locale "+locale);
}
foreach(var locale in new string?[]{null,""," ","en us","en\n","汉语",new string('a',65)}) {
    LocHelper.Locale=locale;var current=GameLanguage.Current;
    Check(current.IsFallback&&current.LocaleCode=="en"&&current.Reason!.StartsWith("EZAPI_LANGUAGE_LOCALE")&&current.Resolve(text)=="Repair adviser","invalid native locale falls back "+cases.Count);
}
LocHelper.Locale="zh";LocHelper.Throw=true;
Check(GameLanguage.Current is{LocaleCode:"en",IsFallback:true,Reason:"EZAPI_LANGUAGE_READ: InvalidOperationException"},"native exception has reason and English fallback");LocHelper.Throw=false;
Check(GameLanguage.Current is{LocaleCode:"zh",IsFallback:false},"native exception is retried and Chinese recovers on the next read");
GameBuildFingerprint.IsKnown=false;
foreach(var locale in new[]{"zh-CN","en","fr"}) {
    LocHelper.Locale=locale;var current=GameLanguage.Current;
    Check(!current.IsFallback&&current.LocaleCode==locale,"unknown wrapper fingerprint still reads actual locale: "+locale);
}
var reads=LocHelper.Reads;
GameSession.Started=false;Check(GameLanguage.Current.IsFallback&&LocHelper.Reads==reads,"before initialization does not call native locale");GameSession.Started=true;
Check(Task.Run(()=>GameLanguage.Current).GetAwaiter().GetResult() is{IsFallback:true,LocaleCode:"en"}&&LocHelper.Reads==reads,"worker access does not call native locale");
foreach(var type in new[]{typeof(MissingGetter),typeof(InstanceGetter),typeof(WrongReturnGetter),typeof(ParameterGetter),typeof(GenericGetter),typeof(InheritedGetter),typeof(PrivateGetter)})
    Check(GameLanguage.BindLocaleGetter(type) is null,"reject incompatible locale getter: "+type.Name);
Check(GameLanguage.BindLocaleGetter(typeof(CompatibleGetter))!()=="zh-CN","bind only the exact public static string getter");
Check(english.Resolve(new LocalizedItemText("中文",null))=="中文"&&chinese.Resolve(new(null,"Only English"))=="Only English","typed text missing side uses provided language");
Reject(()=>GameLanguage.Resolve(new LocalizedItemText()),"typed text requires at least one language");
Reject(()=>GameLanguage.Resolve(new LocalizedItemText(" ","valid")),"provided blank text rejects");
Reject(()=>GameLanguage.Resolve((LocalizedItemText)null!),"typed text rejects null");
using var enStream=new MemoryStream(Encoding.UTF8.GetBytes("greeting,Welcome {0}\nonly_en,English fallback"));
using var zhStream=new MemoryStream(Encoding.UTF8.GetBytes("greeting,欢迎 {0}"));
using var frStream=new MemoryStream(Encoding.UTF8.GetBytes("greeting,Bonjour {0}"));
var tables=new Dictionary<string,LocalizationTable>{{"en",Localization.Parse(enStream,"en")},{"zh",Localization.Parse(zhStream,"zh")},{"fr",Localization.Parse(frStream,"fr")}};
var catalog=new LocalizationCatalog(tables);
LocHelper.Locale="en";var enResult=GameLanguage.Resolve(catalog,"greeting","Greeting unavailable",3);
Check(enResult.Text=="Welcome 3"&&enResult.RequestedLocale=="en"&&!enResult.UsedFallback,"CSV uses actual game English and formats");
LocHelper.Locale="zh";var zhResult=GameLanguage.Resolve(catalog,"greeting","问候不可用",3);
Check(zhResult.Text=="欢迎 3"&&zhResult.RequestedLocale=="zh"&&!zhResult.UsedFallback,"CSV uses actual game Chinese and formats");
LocHelper.Locale="fr";var frenchCsv=GameLanguage.Resolve(catalog,"greeting","Greeting unavailable",3);
Check(frenchCsv.Text=="Bonjour 3"&&frenchCsv.RequestedLocale=="fr"&&frenchCsv.ResolvedLocale=="fr"&&!frenchCsv.UsedFallback,"automatic game locale lookup preserves available French CSV translation");
var keyedText=new LocalizedItemText("欢迎","Welcome"){Key="greeting"};
Check(GameLanguage.Resolve(keyedText,catalog,"Greeting unavailable",3)=="Bonjour 3","CSV-backed typed text follows available game language translation");
var explicitFrench=catalog.Resolve("fr","greeting","Unavailable",3);
Check(explicitFrench.Text=="Bonjour 3"&&!explicitFrench.UsedFallback,"explicit catalog lookup retains parser compatibility");
LocHelper.Locale="de";var missingGerman=GameLanguage.Resolve(catalog,"greeting","Greeting unavailable",3);
Check(missingGerman.Text=="Welcome 3"&&missingGerman.RequestedLocale=="de"&&missingGerman.ResolvedLocale=="en"&&missingGerman.UsedFallback,"game locale without a CSV table falls back to English");
LocHelper.Locale="zh";
var missing=GameLanguage.Resolve(catalog,"unknown_key","Help unavailable");
Check(missing.Text=="Help unavailable"&&missing.MissingKey&&missing.UsedFallback,"missing key uses readable author fallback");
var partial=GameLanguage.Resolve(catalog,"only_en","Help unavailable");
Check(partial.Text=="English fallback"&&partial.ResolvedLocale=="en"&&partial.UsedFallback,"missing translation uses English CSV");
Check(GameLanguage.Resolve(catalog,"greeting","Help unavailable").FormatError is not null,"format error preserved in structured result");
Check(english.Resolve(catalog,"greeting","Unavailable",4).Text=="Welcome 4","CSV snapshot resolves coherently after native change");
Reject(()=>GameLanguage.Resolve((LocalizationCatalog)null!,"greeting","Unavailable"),"CSV rejects null catalog");
var report=new{passed=cases.Count,failed=0,cases,nativeRuntimeVerified=false,scope="Actual GameLanguage and CSV/typed text implementations with explicit thread/build/native locale stubs; actual language switching requires separate native evidence."};
File.WriteAllText("work/ezapi-language-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{report.passed,report.failed,report.scope}));
sealed class MissingGetter { }
sealed class InstanceGetter {public string GetCurrentLocaleCode()=>"zh";}
sealed class WrongReturnGetter {public static int GetCurrentLocaleCode()=>1;}
sealed class ParameterGetter {public static string GetCurrentLocaleCode(bool optional=false)=>"zh";}
sealed class GenericGetter {public static string GetCurrentLocaleCode<T>()=>"zh";}
class CompatibleGetter {public static string GetCurrentLocaleCode()=>"zh-CN";}
sealed class InheritedGetter:CompatibleGetter { }
sealed class PrivateGetter {private static string GetCurrentLocaleCode()=>"zh";}

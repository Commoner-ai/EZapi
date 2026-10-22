using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Text.Json;
namespace EZapi;

/// <summary>一次性中英实机回归；只在固定可丢弃档执行，使用Unity公开语言入口，结束/故障恢复原语言。</summary>
internal static class DebugGuideLocalization
{
    internal static object? LastReport {get;private set;}
    private static readonly List<object> Checks=new();
    private static readonly List<object> LocaleSamples=new();
    private static Locale? _original;
    private static GameRunIdentity? _run;
    private static int _stage,_passed,_failed;
    private static double _readyAt,_deadline;
    private static string? _error;
    private static string? _pendingLocale;
    private static GameLanguageSnapshot? _englishSnapshot;
    private static bool _allSurfaces;
    private static string? _surface;
    private static readonly List<object> SurfaceSamples=new();
    private static readonly List<AsyncOperationHandle<StringTable>> Preloads=new();
    private static void Check(bool ok,string name){Checks.Add(new{name,passed=ok});if(ok)_passed++;else _failed++;}
    internal static bool Start(bool allSurfaces=false)
    {
        if(_stage!=0||!VisibleStoreInventory.IsReady||InventoryApi.IsItemDragActive)return false;
        var run=NativeAccess.RequireRun();if(run.SaveSlotId!=534||run.RunId!="e39e848c-36b7-40e9-ab7e-b6adcb9881b8")return false;
        Checks.Clear();LocaleSamples.Clear();SurfaceSamples.Clear();_passed=_failed=0;_error=null;LastReport=null;_run=run;_englishSnapshot=null;_surface=null;_allSurfaces=allSurfaces;
        try {
            _original=LocalizationSettings.SelectedLocale??throw new InvalidOperationException("native original locale missing");
            Select("en");
            if(!RecipeGuideApi.Open()||!RecipeGuideBrowser.SelectRecipe("ezapi.debug.000_image_probe"))throw new InvalidOperationException("localized guide probe unavailable");
            _stage=1;Report();return true;
        }catch(Exception error){Fail(error);return false;}
    }
    private static void Select(string code)
    {
        var target=LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(code))??throw new InvalidOperationException("native locale missing: "+code);
        // 原生StoreCalendar.Update会同步取表。先异步预热已使用的字符串表，避免切换时同步等待尚未完成的组操作。
        var database=LocalizationSettings.StringDatabase;var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var key in database.TableOperations.Keys)names.Add(key.Item2);
        if(names.Count==0||names.Count>128)throw new InvalidOperationException("bounded native localization table set missing");
        Preloads.Clear();foreach(var name in names)Preloads.Add(database.GetTableAsync((TableReference)name,target));
        _pendingLocale=code;_readyAt=double.PositiveInfinity;_deadline=Time.realtimeSinceStartupAsDouble+25;
    }
    internal static void Update()
    {
        if(_stage==0)return;
        try {
            if(_run is null||!NativeAccess.SameRun(_run)||!VisibleStoreInventory.IsReady)throw new InvalidOperationException("language test run expired");
            if(_pendingLocale is {} pending){
                if(Preloads.Any(h=>!h.IsValid()||h.Status==AsyncOperationStatus.Failed))throw new InvalidOperationException("native language table preload failed");
                if(Preloads.Any(h=>!h.IsDone)){if(Time.realtimeSinceStartupAsDouble>_deadline)throw new TimeoutException("native language table preload");return;}
                (Il2Cpp.EmporiumEntry.Instance??throw new InvalidOperationException("native entry missing")).ChangeLanguage(pending);
                _pendingLocale=null;Preloads.Clear();_readyAt=Time.realtimeSinceStartupAsDouble+6;_deadline=_readyAt+20;Report();return;
            }
            if(Time.realtimeSinceStartupAsDouble<_readyAt)return;
            var locale=_stage==1?"en":_stage==2?"zh":_original!.Identifier.Code;
            if(NativeItemLocalization.CurrentLocaleCode!=locale){if(Time.realtimeSinceStartupAsDouble>_deadline)throw new TimeoutException("native locale readback: "+locale);return;}
            if(_stage is 1 or 2){
                var en=_stage==1;
                if(_surface is not null){UpdateSurfaces(locale,en);return;}
                var language=GameLanguage.Current;
                Check(!language.IsFallback&&language.LocaleCode==locale&&language.LocaleCode==Il2Cpp.LocHelper.GetCurrentLocaleCode(),locale+": public language snapshot reads actual native locale without fallback");
                Check(GameLanguage.Resolve(new LocalizedItemText("维修顾问","Repair adviser"))==(en?"Repair adviser":"维修顾问"),locale+": dynamic author text follows actual game language");
                var catalog=Localization.GetPack("ezapi.sample")??throw new InvalidOperationException("sample CSV catalog unavailable");
                var csv=GameLanguage.Resolve(catalog,"debug_ready","Readable fallback",catalog.Index.Files);
                Check(csv.Text==(en?$"EZapi: {catalog.Index.Files} locale files loaded":$"EZapi：已载入 {catalog.Index.Files} 份语言文件")&&!csv.UsedFallback&&!csv.MissingKey&&csv.FormatError is null,locale+": CSV text resolves current native language and formatting");
                var requirement=RequirementApi.Evaluate(new(){Kind="credits-at-least",Amount=0,Message=new("余额足够","Enough credits")});
                Check(requirement.Satisfied&&requirement.Run==_run&&requirement.Message==(en?"Enough credits":"余额足够"),locale+": native requirement message follows actual game language");
                LocaleSamples.Add(new{locale,language,typedText=GameLanguage.Resolve(new LocalizedItemText("维修顾问","Repair adviser")),csv,requirement});
                if(en)_englishSnapshot=language;
                else Check(_englishSnapshot is{LocaleCode:"en",IsFallback:false}&&_englishSnapshot.Resolve(new("中文","English"))=="English",locale+": retained language snapshot stays stable after real switch");
                Check(RecipeGuideBrowser.VerifyLocaleProbe(locale,en?"EZapi image probe":"EZapi图片检查机",en?"Field kit product":"PNG示意产物",
                    en?"Display only; no production":"示意，无实际加工",en?"1 unit":"1份",
                    en?"Images and text follow the game language. This entry is display only.":"图片和文字都随游戏语言显示；该条目只作展示。"),locale+": native locale drives rendered machine/output/requirement/quantity/note");
                Check(RecipeGuideApi.Snapshot().Recipes.Single(r=>r.RecipeId=="ezapi.debug.000_image_probe").MachineName=="EZapi图片检查机",locale+": resolving does not rewrite original catalog");
                var preview=RecipeGuideImages.Find("ezapi.sample.water_container");
                Check(NativeGuideLocale.IsReady&&preview?.Error is null&&preview?.Thumbnail!=null,locale+": liquid container preview waits for locale resources and has no cached failure");
                if(_allSurfaces){_surface="guide-hold";_readyAt=Time.realtimeSinceStartupAsDouble+18;_deadline=_readyAt+20;Report();return;}
                FinishLocale();return;
            }
            Check(!RecipeGuideApi.IsVisible,"native language test closes guide");
            Check(LocalizationSettings.SelectedLocale.Pointer==_original!.Pointer,"original native locale restored");
            DebugLocalizedItems.Clear();
            _stage=0;Report();_original=null;_run=null;_englishSnapshot=null;
        }catch(Exception error){Fail(error);}
    }
    private static void FinishLocale()
    {
        _surface=null;
        if(_stage==1){_stage=2;Select("zh");if(!RecipeGuideApi.Open()||!RecipeGuideBrowser.SelectRecipe("ezapi.debug.000_image_probe"))throw new InvalidOperationException("Chinese guide reopen failed");Report();return;}
        RecipeGuideApi.Close();Select(_original!.Identifier.Code);_stage=3;Report();
    }
    private static void Import(string locale,string surface,object? value)
    {
        if(value is null)throw new InvalidOperationException(surface+" report missing");
        var report=JsonSerializer.SerializeToElement(value);
        SurfaceSamples.Add(new{locale,surface,report});
        foreach(var row in report.GetProperty("checks").EnumerateArray())Check(row.GetProperty("passed").GetBoolean(),locale+": "+surface+": "+row.GetProperty("name").GetString());
        if(report.GetProperty("failed").GetInt32()!=0)throw new InvalidOperationException(surface+" native assertions failed");
    }
    private static void StartSurfaces(string locale)
    {
        RecipeGuideApi.Close();
        SurfaceSamples.Add(new{locale,surface="items",report=DebugLocalizedItems.Verify(locale,Check)});
        var diskItems=DebugDiskItems.VerifyLocale(locale,Check);
        if(diskItems is not null)SurfaceSamples.Add(new{locale,surface="disk-items",report=diskItems});
        DebugMachineLayouts.Run();Import(locale,"machine-layout",DebugMachineLayouts.LastReport);
        DebugReading.Run();Import(locale,"reading",DebugReading.LastReport);
        var result=ReadingApi.Open("ezapi.debug.locale","ezapi.debug.locale.note",_run!.RunId,_run.SaveSlotId,
            new LocalizedItemText("EZapi 双语阅读","EZapi bilingual reading"),
            new LocalizedItemText("中文标题、正文与署名。\n临时纸条会在关闭后释放。","English title, body and signature.\nThe temporary note is released on close."),
            new LocalizedItemText("EZapi 独立框架","EZapi independent framework"));
        if(result.Status!=PresentationStatus.Opened)throw new InvalidOperationException("localized reading preview: "+result);
        _surface="reading";_readyAt=Time.realtimeSinceStartupAsDouble+6;Report();
    }
    private static void UpdateSurfaces(string locale,bool english)
    {
        if(_surface=="guide-hold"){StartSurfaces(locale);return;}
        if(_surface=="reading") {
            var manager=Il2Cpp.HandnoteUIManager.Instance;
            Check(manager.panel.activeInHierarchy&&manager.title.text==(english?"EZapi bilingual reading":"EZapi 双语阅读")&&
                manager.body.text.Contains(english?"English title, body and signature.":"中文标题、正文与署名。",StringComparison.Ordinal)&&
                manager.sign.text==(english?"EZapi independent framework":"EZapi 独立框架"),locale+": actual localized reading preview title/body/signature");
            Check(ReadingApi.Close("ezapi.debug.locale","ezapi.debug.locale.note").Status==PresentationStatus.Closed&&ReadingApi.RetainedDocuments==0,locale+": localized preview closes and releases document");
            DebugWorkshop.Open();_surface="workshop";_readyAt=Time.realtimeSinceStartupAsDouble+6;Report();return;
        }
        if(_surface=="workshop") {
            Check(WorkshopBrowser.VerifyLocaleProbe(english),locale+": rendered workshop uses localized node, condition and reward text");
            SurfaceSamples.Add(new{locale,surface="workshop",report=WorkshopBrowser.Report()});WorkshopApi.CloseWindow();
            DebugDialogue.Run();if(!DebugDialogue.Running)throw new InvalidOperationException("native dialogue fixture did not start");
            _surface="dialogue";_readyAt=0;_deadline=Time.realtimeSinceStartupAsDouble+180;Report();return;
        }
        if(_surface=="dialogue") {
            if(DebugDialogue.Running){if(Time.realtimeSinceStartupAsDouble>_deadline)throw new TimeoutException("native dialogue fixture");return;}
            Import(locale,"dialogue",DebugDialogue.LastReport);
            DebugPhone.Run();if(!DebugPhone.Running)throw new InvalidOperationException("native phone fixture did not start");
            _surface="phone";_deadline=Time.realtimeSinceStartupAsDouble+120;Report();return;
        }
        if(_surface=="phone") {
            if(DebugPhone.Running){if(Time.realtimeSinceStartupAsDouble>_deadline)throw new TimeoutException("native phone fixture");return;}
            Import(locale,"phone",DebugPhone.LastReport);
            DebugSupplierLocalization.Start();_surface="supplier";_deadline=Time.realtimeSinceStartupAsDouble+180;Report();return;
        }
        if(_surface=="supplier") {
            DebugSupplierLocalization.Update();
            if(DebugSupplierLocalization.Running){if(Time.realtimeSinceStartupAsDouble>_deadline)throw new TimeoutException("native supplier fixture");return;}
            Import(locale,"supplier",DebugSupplierLocalization.LastReport);FinishLocale();return;
        }
        throw new InvalidOperationException("unknown localized surface");
    }
    private static void Fail(Exception error)
    {
        _error=error.GetType().Name+": "+error.Message;Check(false,"native language test: "+_error);
        try{DebugSupplierLocalization.Clear();RecipeGuideApi.Close();WorkshopApi.CloseWindow();ReadingApi.Close("ezapi.debug.locale","ezapi.debug.locale.note");DebugLocalizedItems.Clear();if(_original is not null)Il2Cpp.EmporiumEntry.Instance.ChangeLanguage(_original.Identifier.Code);}catch(Exception restore){_error+="; restore: "+restore.Message;}
        _stage=0;_pendingLocale=null;Preloads.Clear();Report();_original=null;_run=null;_englishSnapshot=null;
    }
    private static void Report()
    {
        LastReport=new{at=DateTimeOffset.Now,running=_stage!=0,stage=_stage,surface=_surface,allSurfaces=_allSurfaces,passed=_passed,failed=_failed,checks=Checks.ToArray(),
            originalLocale=_original?.Identifier.Code,currentLocale=NativeItemLocalization.CurrentLocaleCode,samples=LocaleSamples.ToArray(),surfaceSamples=SurfaceSamples.ToArray(),error=_error,calendar=NativeLocaleCalendarGuard.Report(),scope="native EmporiumEntry.ChangeLanguage en/zh; public language/CSV/requirements and guide; optional native item/tooltip/reopen, machine, reading, workshop, dialogue, phone and supplier fixtures; supplier arrival explicitly fixture-dispatched; original locale restored; no disk save/reload claim"};
        EZapiBootstrap.WriteDiagnostics();
    }
    internal static void Clear()
    {
        if(_original is not null)try{Il2Cpp.EmporiumEntry.Instance.ChangeLanguage(_original.Identifier.Code);}catch(Exception error){MelonLoader.MelonLogger.Warning("[EZapi] debug language restore: "+error.Message);}
        DebugSupplierLocalization.Clear();DebugLocalizedItems.Clear();_original=null;_run=null;_stage=0;_pendingLocale=null;_englishSnapshot=null;_surface=null;Preloads.Clear();Checks.Clear();LocaleSamples.Clear();SurfaceSamples.Clear();LastReport=null;
    }
}

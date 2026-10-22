using Il2Cpp;
using MelonLoader;
namespace EZapi;

/// <summary>显式调试按钮：调用真实公共 API，并直接核对原生 TMP 文本和共享面板。不会保存或交付物品。</summary>
internal static class DebugReading
{
    private const string Owner="ezapi.debug.reading",Id=Owner+".note";
    private static GameRunIdentity? _probe;
    private static DateTimeOffset _probeAt;
    internal static object? LastReport{get;private set;}
    internal static object? LifecycleReport{get;private set;}
    internal static void Clear(){LastReport=null;LifecycleReport=null;_probe=null;}
    internal static string Run()
    {
        if(!VisibleStoreInventory.IsReady||!GameSession.TryResolve(null,out var player))return "请在测试档店铺检查阅读面板。";
        if(InventoryApi.IsItemDragActive||HandnoteUIManager.Instance?.panel?.activeInHierarchy==true)return "先关闭现有纸条并放下拖拽物品。";
        var identity=GameSession.Identity!;var checks=new List<object>();int passed=0,failed=0;GameItem? foreign=null;
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        var locale=GameLanguage.Current.LocaleCode;
        string Text(string zh,string en)=>locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?zh:en;
        PresentationResult OpenLocalized(LocalizedItemText title,LocalizedItemText body,LocalizedItemText sign)
            =>ReadingApi.Open(Owner,Id,identity.RunId,identity.SaveSlotId,title,body,sign);
        PresentationResult Open()=>OpenLocalized(new("EZapi 原生阅读验证","EZapi native reading check"),
            new("第一行：中文正文。\n第二行：状态隔离与兼容性。","First line: English body.\nSecond line: state isolation and compatibility."),
            new("EZapi 测试署名","EZapi test signature"));
        string InventoryFingerprint()=>string.Join("|",InventoryApi.CaptureRunItems(player).Select(x=>x.Pointer.ToInt64()+":"+x.identifier+":"+x.unitCount));
        var inventoryBefore=InventoryFingerprint();
        try {
            var hooks=ReadingApi.GetRuntimeStatus();Check(ReadingApi.IsAvailable&&hooks.Ready&&hooks.Targets.Count==5,"five exact native hooks ready");
            Check(ReadingApi.Open(Owner,Id,identity.RunId,identity.SaveSlotId,"bad\0","body","").Status==PresentationStatus.Invalid,"invalid text rejected before native display");
            Check(ReadingApi.Open(Owner,Id,identity.RunId,identity.SaveSlotId+1,"title","body","").Status==PresentationStatus.Conflict,"wrong save slot rejected");
            Check(Open().Status==PresentationStatus.Opened,"public API opens actual native panel");
            var manager=HandnoteUIManager.Instance;Check(manager!=null&&manager.panel.activeInHierarchy,"native panel is visible");
            Check(manager!.title.text==Text("EZapi 原生阅读验证","EZapi native reading check"),"native localized title text exact");
            Check(manager.body.text==Text("第一行：中文正文。\n第二行：状态隔离与兼容性。","First line: English body.\nSecond line: state isolation and compatibility."),"native localized multiline body exact");
            Check(manager.sign.text==Text("EZapi 测试署名","EZapi test signature"),"native localized signature text exact");
            Check(ReadingApi.RetainedDocuments==1,"one temporary document retained while visible");
            Check(ReadingApi.Open("other.reader","other.reader.note",identity.RunId,identity.SaveSlotId,"other","body","").Status==PresentationStatus.Conflict,"other owner cannot replace visible note");
            Check(ReadingApi.Open(Owner,Owner+".other",identity.RunId,identity.SaveSlotId,"other","body","").Status==PresentationStatus.Conflict,"different document cannot replace visible note");
            Check(ReadingApi.Close("other.reader","other.reader.note").Status==PresentationStatus.Conflict&&manager.panel.activeInHierarchy,"other owner cannot close visible note");
            Check(OpenLocalized(new("更新标题","Updated title"),new("刷新后的内容","Refreshed content"),new("新署名","Updated signature")).Status==PresentationStatus.Opened,"same document localized refresh reads back success");
            Check(manager.title.text==Text("更新标题","Updated title")&&manager.body.text==Text("刷新后的内容","Refreshed content")&&manager.sign.text==Text("新署名","Updated signature"),"refresh updates all three native localized text fields");
            Check(ReadingApi.RetainedDocuments==1,"refresh retains only current document");
            Check(ReadingApi.Close(Owner,Id).Status==PresentationStatus.Closed&&!manager.panel.activeInHierarchy&&ReadingApi.RetainedDocuments==0,"public close hides native panel and releases owned note");
            Check(ReadingApi.Close(Owner,Id).Status==PresentationStatus.Closed,"repeated close idempotent");
            Open();manager.ClosePanel();ReadingApi.Maintain();Check(ReadingApi.RetainedDocuments==0,"native close button path releases owned note");
            Open();
            foreign=DirectoryMaster.Item("handnote",true);foreign.state.GetTag("HANDNOTE_TITLE").Enable().SetString("原版/其他 Mod 的纸条");
            foreign.state.GetTag("HANDNOTE_BODY").Enable().SetString("共享面板优先保留原生调用者。");foreign.SyncModifiedState();
            Check(foreign.parentInventory is null,"external fixture is detached from inventories");
            manager.OpenPanel(foreign);ReadingApi.Maintain();
            Check(manager.panel.activeInHierarchy&&manager.title.text=="原版/其他 Mod 的纸条"&&ReadingApi.RetainedDocuments==0,"external takeover frees only EZapi document");
            Check(Open().Status==PresentationStatus.Conflict,"external native panel has priority");
            Check(ReadingApi.Close(Owner,Id).Status==PresentationStatus.Closed&&manager.panel.activeInHierarchy&&manager.title.text=="原版/其他 Mod 的纸条","close without owned lease preserves external native panel");
            manager.ClosePanel();foreign.Destroy();foreign=null;
            for(int i=0;i<3;i++) {
                Check(OpenLocalized(new("循环 "+i,"Cycle "+i),new("正文 "+i,"Body "+i),new("署名 "+i,"Signature "+i)).Status==PresentationStatus.Opened,"reopen cycle "+i);
                Check(ReadingApi.Close(Owner,Id).Status==PresentationStatus.Closed&&ReadingApi.RetainedDocuments==0,"close cycle "+i);
            }
            Check(inventoryBefore==InventoryFingerprint(),"main inventory item identities and counts unchanged");
        }catch(Exception e){Check(false,"execution: "+e);MelonLogger.Error("[EZapi] Reading fixture: "+e);}
        finally {
            ReadingApi.Close(Owner,Id);
            // foreign 只由本调试调用创建；先关闭其面板，避免销毁仍显示的纸条。
            if(foreign is not null){HandnoteUIManager.Instance?.ClosePanel();foreign.Destroy();}
        }
        LastReport=new{at=DateTimeOffset.Now,locale,passed,failed,checks,identity,reading=ReadingApi.Report(),scope="actual installed DLL; real native panel/title/body/sign and external native takeover; no save writes; lifecycle reload is recorded separately"};
        EZapiBootstrap.WriteDiagnostics();return $"原生阅读：{passed}通过，{failed}失败。";
    }
    internal static string Show()
    {
        if(!VisibleStoreInventory.IsReady||!GameSession.TryResolve(null,out _))return "先进入测试档店铺。";
        var identity=GameSession.Identity!;
        var result=ReadingApi.Open(Owner,Id,identity.RunId,identity.SaveSlotId,"EZapi 原生阅读面板","这是游戏自己的纸条界面。\n标题、正文、署名均由 EZapi 设置。\n\n关闭纸条、返回菜单或重新读档后，临时实例会按实际面板状态清理。","EZapi · 独立框架");
        if(result.Status==PresentationStatus.Opened){_probe=identity;_probeAt=DateTimeOffset.Now;LifecycleReport=new{status="observing",identity,at=_probeAt};}
        EZapiBootstrap.WriteDiagnostics();return "阅读面板："+result.Status+" "+result.Reason;
    }
    internal static string Close(){var result=ReadingApi.Close(Owner,Id);Update();EZapiBootstrap.WriteDiagnostics();return "关闭阅读："+result.Status;}
    internal static void Update()
    {
        if(_probe is null||ReadingApi.RetainedDocuments!=0)return;
        LifecycleReport=new{status="released",beganAt=_probeAt,finishedAt=DateTimeOffset.Now,identityBefore=_probe,identityAfter=GameSession.Identity,reading=ReadingApi.Report()};
        _probe=null;EZapiBootstrap.WriteDiagnostics();
    }
}

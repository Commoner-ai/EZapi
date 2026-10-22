namespace EZapi;

// 此协调器不引用游戏；离线回归直接执行本文件，模拟宿主重入和第三方 UI 替换。
internal sealed record ReadingScope(string RunId,int SlotId,long Epoch);
internal sealed record ReadingEnvironment(bool Ready,ReadingScope? Scope,long Panel,bool Visible,bool Dragging=false,string? Title=null,string? Body=null,string? Signature=null);
internal interface IReadingDocument { long Token {get;} void Release(); }
internal interface IReadingHost
{
    ReadingEnvironment Inspect();
    IReadingDocument Create(string title,string body,string signature);
    void Open(long panel,IReadingDocument document);
    void Close(long panel);
    void Error(string message);
}

/// <summary>单个共享原生阅读面板的所有权协调。只保留本模块创建的临时纸条，关闭/替换后释放。</summary>
internal sealed class ReadingCoordinator
{
    private sealed record Lease(string Owner,string Id,ReadingScope Scope,long Panel,IReadingDocument Document,string Title,string Body,string Signature);
    private readonly IReadingHost _host;
    private Lease? _lease;
    // 原生写后读回暂时不可用时，最多保留新旧两份；不能遗失旧句柄或继续分配。
    private Lease? _retiring;
    private long _displayed,_panel,_revision;
    private bool _busy,_resetRequested;
    internal ReadingCoordinator(IReadingHost host)=>_host=host;
    internal bool HasDocument=>_lease is not null||_retiring is not null;
    internal int RetainedDocuments=>(_lease is null?0:1)+(_retiring is null?0:1);
    internal bool IsBusy=>_busy;
    internal int ReleaseFailures{get;private set;}
    internal string? Owner=>_lease?.Owner;
    internal string? DocumentId=>_lease?.Id;
    internal long Displayed=>_displayed;
    internal void Opened(long panel,long document){_panel=panel;_displayed=document;}
    internal void Closed(long panel){if(_panel==panel){_displayed=0;_panel=0;}}
    internal PresentationResult Open(string owner,string id,string run,int slot,string title,string body,string signature)
    {
        if(!PresentationRules.Owned(owner,id)||string.IsNullOrWhiteSpace(run)||slot<0||
            !PresentationRules.Text(title,512)||!PresentationRules.Text(body,131072)||!PresentationRules.Text(signature,4096))
            return new(PresentationStatus.Invalid,"EZAPI_READING_ARGUMENT: owned ID, identity and bounded text required.");
        if(_busy)return new(PresentationStatus.Conflict,"EZAPI_READING_REENTRANT");
        if(ReleaseFailures!=0)return new(PresentationStatus.Unavailable,"EZAPI_READING_CLEANUP_FAULT: restart required; no automatic destroy retry.");
        if(_retiring is not null)return new(PresentationStatus.Unavailable,"EZAPI_READING_READBACK_PENDING: retained documents await native inspection.");
        _busy=true;var revision=_revision;
        try {
            var env=_host.Inspect();
            if(!env.Ready||env.Scope is null||env.Panel==0)return new(PresentationStatus.Unavailable,"EZAPI_READING_NOT_READY");
            if(env.Scope.RunId!=run||env.Scope.SlotId!=slot)return new(PresentationStatus.Conflict,"EZAPI_READING_IDENTITY");
            if(env.Dragging)return new(PresentationStatus.Conflict,"EZAPI_READING_DRAGGING");
            var prior=_lease;
            if(env.Visible&&!(prior is not null&&prior.Owner==owner&&prior.Id==id&&prior.Scope==env.Scope&&OwnsDisplay(prior,env)))
                return new(PresentationStatus.Conflict,"EZAPI_READING_FOREIGN_PANEL");
            // 同作者/同文档更新也创建独立实例；原面板仍在读旧纸条时不能提前销毁。
            var document=_host.Create(title,body,signature);
            var next=new Lease(owner,id,env.Scope,env.Panel,document,title,body,signature);
            _lease=next;_retiring=prior;
                if(_revision!=revision||_resetRequested) return new(PresentationStatus.Indeterminate,"EZAPI_READING_RESET_DURING_CREATE");
                var current=_host.Inspect();
                if(!current.Ready||current.Scope!=env.Scope||current.Panel!=env.Panel||current.Dragging||
                    current.Visible&&!(prior is not null&&OwnsDisplay(prior,current)))
                    return new(PresentationStatus.Indeterminate,"EZAPI_READING_CHANGED_DURING_CREATE");
                _host.Open(env.Panel,document);
                var after=_host.Inspect();
                return _revision==revision&&after.Ready&&after.Scope==next.Scope&&OwnsDisplay(next,after)&&
                    (after.Title is null||after.Title==title&&after.Body==body&&after.Signature==signature)
                    ?new(PresentationStatus.Opened):new(PresentationStatus.Indeterminate,"EZAPI_READING_OPEN_READBACK");
        } catch(Exception e){_host.Error("EZAPI_READING_OPEN: "+e);return new(PresentationStatus.Indeterminate,e.Message);}
        finally{_busy=false;if(_resetRequested)Reset();else Maintain();}
    }
    internal PresentationResult Close(string owner,string id)
    {
        if(!PresentationRules.Owned(owner,id))return new(PresentationStatus.Invalid,"EZAPI_READING_ARGUMENT");
        if(_busy)return new(PresentationStatus.Conflict,"EZAPI_READING_REENTRANT");
        _busy=true;
        try {
            var env=_host.Inspect();var lease=_lease;
            if(!env.Ready)return new(PresentationStatus.Unavailable,"EZAPI_READING_NOT_READY");
            if(lease is null)return new(PresentationStatus.Closed);
            if(lease.Owner!=owner||lease.Id!=id)return new(PresentationStatus.Conflict,"EZAPI_READING_OWNER");
            if(!OwnsDisplay(lease,env))return env.Visible?new(PresentationStatus.Conflict,"EZAPI_READING_FOREIGN_PANEL"):new(PresentationStatus.Closed);
            _host.Close(lease.Panel);
            return !_host.Inspect().Visible?new(PresentationStatus.Closed):new(PresentationStatus.Indeterminate,"EZAPI_READING_CLOSE_READBACK");
        }catch(Exception e){_host.Error("EZAPI_READING_CLOSE: "+e);return new(PresentationStatus.Indeterminate,e.Message);}
        finally{_busy=false;if(_resetRequested)Reset();else Maintain();}
    }
    private bool OwnsDisplay(Lease lease,ReadingEnvironment env)=>env.Visible&&env.Panel==lease.Panel&&_panel==lease.Panel&&_displayed==lease.Document.Token;
    internal void Maintain()
    {
        if(_busy||!HasDocument)return;
        _busy=true;
        try {
            var env=_host.Inspect();ReconcileRetiring(env);var lease=_lease;
            if(lease is null)return;
            if(env.Scope==lease.Scope&&OwnsDisplay(lease,env))return;
            if(OwnsDisplay(lease,env)) {
                _host.Close(lease.Panel);
                if(OwnsDisplay(lease,_host.Inspect()))return; // 原生未关闭，继续持有，绝不销毁仍在展示的纸条。
            }
            _lease=null;SafeRelease(lease.Document);
        }catch(Exception e){_host.Error("EZAPI_READING_MAINTAIN: "+e);}
        finally{_busy=false;}
    }
    internal void Reset()
    {
        _revision++;
        if(_busy){_resetRequested=true;return;}
        _resetRequested=false;
        if(!HasDocument){_panel=_displayed=0;return;}
        _busy=true;
        try {
            var env=_host.Inspect();ReconcileRetiring(env);var lease=_lease;
            if(lease is null)return;
            if(OwnsDisplay(lease,env))_host.Close(lease.Panel);
            if(OwnsDisplay(lease,_host.Inspect()))return;
            _lease=null;_panel=_displayed=0;SafeRelease(lease.Document);
        }catch(Exception e){_host.Error("EZAPI_READING_RESET: "+e);}
        finally{_busy=false;}
    }
    private void ReconcileRetiring(ReadingEnvironment env)
    {
        var prior=_retiring;if(prior is null)return;
        // 原生拒绝刷新时仍保留旧纸条；只有确认已离开面板的实例才允许销毁。
        var next=_lease;_retiring=null;
        if(OwnsDisplay(prior,env)){_lease=prior;if(next is not null)SafeRelease(next.Document);}
        else SafeRelease(prior.Document);
    }
    private void SafeRelease(IReadingDocument document)
    {
        try{document.Release();}catch(Exception e){ReleaseFailures++;_host.Error("EZAPI_READING_RELEASE: "+e);}
    }
}

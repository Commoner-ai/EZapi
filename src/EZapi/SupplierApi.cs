using Il2Cpp;
using MelonLoader;

namespace EZapi;

/// <summary>Schedules dialogue-only suppliers through the official late customer hook.</summary>
public static class SupplierApi
{
    private const int MaxRequests=128;
    private static readonly Dictionary<(string Owner,string Request),Visit> Visits=new();
    private static bool _started,_fault,_busy;
    private static long _generation;

    private sealed class Visit
    {
        internal readonly string Owner,Request,RunId,ActorId,SpriteName;
        internal readonly int SlotId;
        internal readonly long Epoch,Generation;
        internal readonly SupplierDefinition Definition;
        internal SupplierPage? Page,PendingPage;
        internal DialogueScript? PendingScript;
        internal bool Finished,PendingDismiss,DismissInvoked,PendingContinue,Opened,AutoTriggered;
        internal DateTime? AutoVisibleSince;
        internal long ContinueStartedMs,RetryAtMs;
        internal string? Reason;

        internal Visit(string owner,string request,string runId,int slotId,long epoch,long generation,
            string actorId,string spriteName,SupplierDefinition definition)
            =>(Owner,Request,RunId,SlotId,Epoch,Generation,ActorId,SpriteName,Definition)=
                (owner,request,runId,slotId,epoch,generation,actorId,spriteName,definition);
    }

    /// <summary>True when the current build, official customer hook, and EZapi dialogue runtime are ready.</summary>
    public static bool IsAvailable=>_started&&!_fault&&ClientVisitApi.IsAvailable&&DialogueApi.IsAvailable;

    internal static void Start(){_started=true;_fault=false;_generation++;}

    /// <summary>
    /// Queues one custom dialogue-only supplier. Purchase callbacks own item delivery, payment, and rollback.
    /// </summary>
    public static PresentationResult Queue(string ownerId,string requestId,string runId,int slotId,
        string actorId,string spriteName,SupplierDefinition definition)
    {
        if(!GameSession.OnThread||!IsAvailable)return new(PresentationStatus.Unavailable,"EZAPI_SUPPLIER_UNAVAILABLE");
        if(!PresentationRules.Owned(ownerId,requestId)||!PresentationRules.Text(runId,160)||string.IsNullOrWhiteSpace(runId)||
            slotId<0||!Token(actorId)||!Token(spriteName)||definition is null)
            return new(PresentationStatus.Invalid,"EZAPI_SUPPLIER_REQUEST");
        if(_busy)return new(PresentationStatus.Conflict,"EZAPI_SUPPLIER_REENTRY");
        if(!GameSession.TryResolve(null,out _)||GameSession.Identity is not { } identity)
            return new(PresentationStatus.Unavailable,"EZAPI_SUPPLIER_RUN");
        if(identity.RunId!=runId||identity.SaveSlotId!=slotId)
            return new(PresentationStatus.Conflict,"EZAPI_SUPPLIER_SCOPE");
        var key=(ownerId,requestId);
        if(Visits.ContainsKey(key))return new(PresentationStatus.Conflict,"EZAPI_SUPPLIER_DUPLICATE");
        Prune();
        if(Visits.Count>=MaxRequests)return new(PresentationStatus.Conflict,"EZAPI_SUPPLIER_LIMIT");

        _busy=true;
        try
        {
            SupplierDefinition frozen;
            try{frozen=SupplierContract.Freeze(ownerId,definition);}
            catch(ArgumentException error){return new(PresentationStatus.Invalid,error.Message);}
            var visit=new Visit(ownerId,requestId,runId,slotId,identity.Epoch,++_generation,actorId,spriteName,frozen);
            if(!TryBuildMenu(visit,out var page,out var script))
                return new(PresentationStatus.Indeterminate,"EZAPI_SUPPLIER_CAPTURE");
            var result=ClientVisitApi.QueueManaged(ownerId,requestId,runId,slotId,actorId,spriteName,script,
                ()=>CreateClient(actorId,spriteName,SupplierContract.ResolveText(frozen.LocalizedSpeaker,frozen.Speaker,NativeItemLocalization.CurrentLocaleCode)),
                ()=>OnClientDismissed(visit),
                ()=>OnDialogueOpened(visit));
            if(result.Status==PresentationStatus.Queued)
            {
                visit.Page=page;
                Visits.Add(key,visit);
            }
            return result;
        }
        catch(Exception error)
        {
            Warn("EZAPI_SUPPLIER_QUEUE",error);
            return new(PresentationStatus.Indeterminate,"EZAPI_SUPPLIER_QUEUE");
        }
        finally{_busy=false;}
    }

    /// <summary>Reads this process's supplier request status; it does not imply that an offer was purchased.</summary>
    public static PresentationResult Status(string ownerId,string requestId)
    {
        if(!GameSession.OnThread||!_started)return new(PresentationStatus.Unavailable,"EZAPI_SUPPLIER_UNAVAILABLE");
        if(!PresentationRules.Owned(ownerId,requestId))return new(PresentationStatus.Invalid,"EZAPI_SUPPLIER_REQUEST");
        if(!Visits.TryGetValue((ownerId,requestId),out var visit))return new(PresentationStatus.Closed,"EZAPI_SUPPLIER_UNKNOWN");
        if(visit.Finished)return new(PresentationStatus.Closed,visit.Reason);
        return ClientVisitApi.Status(ownerId,requestId);
    }

    /// <summary>Runs deferred menu refresh and automatic-offer selection outside native dialogue callbacks.</summary>
    internal static void Maintain()
    {
        if(!_started||!GameSession.OnThread||_busy)return;
        _busy=true;
        try
        {
            foreach(var visit in Visits.Values.ToArray())
            {
                if(visit.PendingDismiss)InvokeDismiss(visit);
                if(visit.Finished)continue;
                if(!Current(visit)){visit.Finished=true;visit.Reason="EZAPI_SUPPLIER_SCOPE_ENDED";continue;}

                var status=ClientVisitApi.Status(visit.Owner,visit.Request);
                if(status.Status==PresentationStatus.Dismissed)
                {
                    visit.Finished=true;visit.Reason=status.Reason;visit.PendingDismiss=true;InvokeDismiss(visit);continue;
                }
                if(status.Status is PresentationStatus.Invalid or PresentationStatus.Indeterminate or PresentationStatus.Closed)
                {visit.Finished=true;visit.Reason=status.Reason;continue;}
                if(status.Status!=PresentationStatus.Arrived)continue;

                if(visit.PendingContinue)
                {
                    ProcessContinue(visit);
                    if(visit.PendingContinue||visit.Finished)continue;
                }
                ProcessAutomaticOffer(visit);
            }
        }
        catch(Exception error){Warn("EZAPI_SUPPLIER_MAINTAIN",error);}
        finally{_busy=false;}
    }

    private static bool TryBuildMenu(Visit visit,out SupplierPage page,out DialogueScript script)
    {
        page=null!;script=null!;
        try
        {
            var captureContext=Context(visit,string.Empty);
            page=SupplierContract.FreezePage(visit.Definition.Capture(captureContext));
            var canContinue=visit.Definition.AllowMenuRefresh;
            if(canContinue&&visit.Definition.CanContinue is not null)
            {
                try{canContinue=visit.Definition.CanContinue(captureContext);}
                catch(Exception error){canContinue=false;Warn("EZAPI_SUPPLIER_CAN_CONTINUE",error);}
            }
            var choices=page.Offers.Select(offer=>new DialogueChoice(offer.Id,offer.Label,offer.Preview,
                context=>Purchase(visit,offer,context.LocaleCode),offer.ConfirmLabel){LocalizedLabel=offer.LocalizedLabel,LocalizedPreview=offer.LocalizedPreview,LocalizedConfirmLabel=offer.LocalizedConfirmLabel}).ToArray();
            var dialogue=new DialogueDefinition(visit.Definition.Id,visit.Definition.Speaker,page.Body,choices,
                visit.Definition.ConfirmLabel,visit.Definition.BackLabel,visit.Definition.LeaveLabel,
                visit.Definition.LeaveLabel,visit.Definition.FailureText)
            {
                RequireConfirmation=visit.Definition.RequireConfirmation,
                AllowLater=true,
                NextPageLabel=visit.Definition.NextPageLabel,
                PreviousPageLabel=visit.Definition.PreviousPageLabel,
                LocalizedSpeaker=visit.Definition.LocalizedSpeaker,LocalizedBody=page.LocalizedBody,
                LocalizedConfirmLabel=visit.Definition.LocalizedConfirmLabel,LocalizedBackLabel=visit.Definition.LocalizedBackLabel,
                LocalizedLeaveLabel=visit.Definition.LocalizedLeaveLabel,LocalizedLaterLabel=visit.Definition.LocalizedLeaveLabel,LocalizedFailureText=visit.Definition.LocalizedFailureText,
                LocalizedNextPageLabel=visit.Definition.LocalizedNextPageLabel,LocalizedPreviousPageLabel=visit.Definition.LocalizedPreviousPageLabel
            };
            var frozen=DialogueContract.Freeze(visit.Owner,dialogue);
            script=new DialogueScript(visit.Owner,frozen.Definition,
                canContinue?visit.Definition.ContinueLabel:null,
                canContinue?(_=>RequestContinue(visit)):null,
                _=>RequestDismiss(visit),page.AutomaticOfferId,canContinue?visit.Definition.LocalizedContinueLabel:null);
            return true;
        }
        catch(Exception error)
        {
            Warn("EZAPI_SUPPLIER_CAPTURE",error);
            return false;
        }
    }

    private static string Purchase(Visit visit,SupplierOffer offer,string locale)
    {
        if(_busy||visit.Finished||!Current(visit))return Failure(visit,locale);
        _busy=true;
        try{return offer.Purchase(Context(visit,offer.Id,locale));}
        catch(Exception error){Warn("EZAPI_SUPPLIER_PURCHASE",error);return Failure(visit,locale);}
        finally{_busy=false;}
    }

    private static string Failure(Visit visit,string locale)=>SupplierContract.ResolveText(visit.Definition.LocalizedFailureText,
        visit.Definition.FailureText,locale);

    private static void ProcessContinue(Visit visit)
    {
        var now=Environment.TickCount64;
        if(now<visit.RetryAtMs)return;
        if(now-visit.ContinueStartedMs>30000)
        {visit.PendingContinue=false;visit.Finished=true;visit.Reason="EZAPI_SUPPLIER_REFRESH_TIMEOUT";return;}
        if(visit.PendingScript is null)
        {
            if(!TryBuildMenu(visit,out var page,out var script))
            {
                page=new SupplierPage(visit.Definition.FailureText,Array.Empty<SupplierOffer>());
                script=BuildFailureScript(visit);
            }
            visit.PendingPage=page;visit.PendingScript=script;
        }

        var close=DialogueApi.Close(visit.Owner,visit.Request);
        if(close.Status is not PresentationStatus.Closed)
        {
            visit.PendingContinue=false;visit.Finished=true;visit.Reason=close.Reason??"EZAPI_SUPPLIER_REFRESH_CLOSE";
            Warn("EZAPI_SUPPLIER_REFRESH_CLOSE",new InvalidOperationException(visit.Reason));return;
        }
        var opened=DialogueApi.Open(visit.Owner,visit.Request,visit.RunId,visit.SlotId,visit.PendingScript);
        if(opened.Status==PresentationStatus.Opened)
        {
            visit.Page=visit.PendingPage;visit.PendingPage=null;visit.PendingScript=null;
            visit.PendingContinue=false;visit.AutoTriggered=false;visit.AutoVisibleSince=null;visit.Opened=true;
            visit.Reason=null;return;
        }
        if(opened.Status==PresentationStatus.Conflict&&opened.Reason=="EZAPI_DIALOGUE_OCCUPIED")
        {visit.RetryAtMs=now+400;return;}
        visit.PendingContinue=false;visit.Finished=true;visit.Reason=opened.Reason??"EZAPI_SUPPLIER_REFRESH_OPEN";
        Warn("EZAPI_SUPPLIER_REFRESH_OPEN",new InvalidOperationException(visit.Reason));
    }

    private static DialogueScript BuildFailureScript(Visit visit)
    {
        var dialogue=new DialogueDefinition(visit.Definition.Id,visit.Definition.Speaker,visit.Definition.FailureText,
            Array.Empty<DialogueChoice>(),visit.Definition.ConfirmLabel,visit.Definition.BackLabel,
            visit.Definition.LeaveLabel,visit.Definition.LeaveLabel,visit.Definition.FailureText){AllowLater=true,
                LocalizedSpeaker=visit.Definition.LocalizedSpeaker,LocalizedBody=visit.Definition.LocalizedFailureText,
                LocalizedConfirmLabel=visit.Definition.LocalizedConfirmLabel,LocalizedBackLabel=visit.Definition.LocalizedBackLabel,
                LocalizedLeaveLabel=visit.Definition.LocalizedLeaveLabel,LocalizedLaterLabel=visit.Definition.LocalizedLeaveLabel,LocalizedFailureText=visit.Definition.LocalizedFailureText,
                LocalizedNextPageLabel=visit.Definition.LocalizedNextPageLabel,LocalizedPreviousPageLabel=visit.Definition.LocalizedPreviousPageLabel};
        return new DialogueScript(visit.Owner,DialogueContract.Freeze(visit.Owner,dialogue).Definition,
            onLeave:_=>RequestDismiss(visit));
    }

    private static void ProcessAutomaticOffer(Visit visit)
    {
        var page=visit.Page;
        var offerId=page?.AutomaticOfferId;
        if(!visit.Opened||visit.AutoTriggered||string.IsNullOrEmpty(offerId))return;
        if(!DialogueApi.IsChoiceVisibleById(visit.Owner,visit.Request,offerId))
        {visit.AutoVisibleSince=null;return;}
        visit.AutoVisibleSince??=DateTime.UtcNow;
        if((DateTime.UtcNow-visit.AutoVisibleSince.Value).TotalSeconds<page!.AutomaticDelaySeconds)return;
        if(DialogueApi.TrySelectChoiceById(visit.Owner,visit.Request,offerId))
        {visit.AutoTriggered=true;visit.AutoVisibleSince=null;}
    }

    private static StoreClient CreateClient(string actorId,string spriteName,string speaker)
    {
        var client=new StoreClient();
        client.identifier=actorId;
        client.displayName=speaker;
        client.spriteName=spriteName;
        client.clientFaction=StoreClient.FACTION_LOWER;
        client.clientIntent=StoreClient.ClientIntent.DIALOGUE;
        client.dismissable=true;
        client.arrestable=false;
        client.nonShootable=true;
        client.isRegularSellDisable=true;
        client.isBargainAvailable=false;
        client.isMultiBuyDisabled=true;
        client.isNoContributeToEvidence=true;
        return client;
    }

    private static void RequestContinue(Visit visit)
    {
        if(visit.Finished||!Current(visit))return;
        visit.PendingContinue=true;visit.ContinueStartedMs=Environment.TickCount64;visit.RetryAtMs=0;
        visit.PendingPage=null;visit.PendingScript=null;
    }

    private static void RequestDismiss(Visit visit)
    {
        if(visit.Finished&&visit.DismissInvoked)return;
        visit.Finished=true;visit.PendingContinue=false;visit.PendingDismiss=true;
    }

    private static void OnClientDismissed(Visit visit)
    {
        visit.Finished=true;visit.PendingContinue=false;visit.PendingDismiss=true;
    }

    private static void OnDialogueOpened(Visit visit)
    {
        if(visit.Finished||!Current(visit))return;
        visit.Opened=true;visit.AutoTriggered=false;visit.AutoVisibleSince=null;
    }

    private static void InvokeDismiss(Visit visit)
    {
        visit.PendingDismiss=false;
        if(visit.DismissInvoked)return;
        visit.DismissInvoked=true;
        try{visit.Definition.Dismiss?.Invoke(Context(visit,string.Empty));}
        catch(Exception error){Warn("EZAPI_SUPPLIER_DISMISS",error);}
    }

    private static DialogueContext Context(Visit visit,string choiceId,string? locale=null)
        =>new(visit.Generation,visit.RunId,visit.SlotId,visit.Owner,visit.Request,visit.Definition.Id,choiceId)
            {LocaleCode=locale??NativeItemLocalization.CurrentLocaleCode};

    private static bool Current(Visit visit)
        =>_started&&GameSession.TryResolve(visit.Epoch,out _)&&GameSession.Identity is { } identity&&
            identity.Epoch==visit.Epoch&&identity.RunId==visit.RunId&&identity.SaveSlotId==visit.SlotId;

    internal static void SceneChanged(){Visits.Clear();_generation++;}

    internal static void Stop(){Visits.Clear();_started=false;_fault=false;_busy=false;_generation++;}

    private static bool Token(string? value)=>value is {Length:>0 and <=160}&&value.All(c=>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-' or ':');

    private static void Prune()
    {
        if(Visits.Count<MaxRequests)return;
        foreach(var key in Visits.Where(p=>p.Value.Finished||
            ClientVisitApi.Status(p.Key.Owner,p.Key.Request).Status is PresentationStatus.Dismissed or PresentationStatus.Invalid or PresentationStatus.Closed)
            .Select(p=>p.Key).Take(Math.Max(1,Visits.Count-MaxRequests+1)).ToArray())Visits.Remove(key);
    }

    private static void Warn(string code,Exception error)=>MelonLogger.Warning("[EZapi] "+code+": "+error);

    internal static object Report()=>new
    {
        ready=IsAvailable,started=_started,faulted=_fault,generation=_generation,
        queued=Visits.Values.Count(v=>ClientVisitApi.Status(v.Owner,v.Request).Status==PresentationStatus.Queued),
        arrived=Visits.Values.Count(v=>ClientVisitApi.Status(v.Owner,v.Request).Status==PresentationStatus.Arrived),
        active=Visits.Values.Count(v=>!v.Finished),
        requests=Visits.Values.Select(v=>new{v.Owner,v.Request,v.RunId,v.SlotId,v.ActorId,finished=v.Finished,
            pendingContinue=v.PendingContinue,automaticOffer=v.Page?.AutomaticOfferId,reason=v.Reason}).ToArray()
    };
}

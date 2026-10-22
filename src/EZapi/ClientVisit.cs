using Il2Cpp;
using Il2CppInterop.Runtime;
using MelonLoader;

namespace EZapi;

/// <summary>
/// Schedules one native customer blueprint for the next customer-generation hook and opens an
/// EZapi dialogue when that customer arrives. The factory blueprint retains its vanilla trade rules.
/// </summary>
public static class ClientVisitApi
{
    private const int MaxRequests = 128;
    private static readonly Dictionary<(string Owner, string Request), Visit> Visits = new();
    private static readonly Queue<Visit> Pending = new();
    private static bool _started, _fault, _busy;
    private static long _generation;
    private static long _lastDialogueAttemptMs;

    private sealed class Visit
    {
        internal readonly string Owner, Request, RunId, ActorId, SpriteName;
        internal readonly int SlotId;
        internal readonly long Epoch;
        internal DialogueScript? Script;
        internal PresentationStatus State = PresentationStatus.Queued;
        internal string? Reason;
        internal StoreClient? Client;
        internal Il2CppSystem.Action? ArrivalCallback, DismissCallback;
        internal Func<StoreClient>? ClientFactory;
        internal Action? OnDismissed, OnOpened;
        internal bool DialoguePending, DialogueOpened, ClosePending;
        internal long ArrivedAtMs;

        internal Visit(string owner, string request, string run, int slot, long epoch, string actor,
            string sprite, DialogueScript script,Func<StoreClient>? clientFactory,Action? onDismissed,Action? onOpened)
            => (Owner, Request, RunId, SlotId, Epoch, ActorId, SpriteName, Script,ClientFactory,OnDismissed,OnOpened) =
                (owner, request, run, slot, epoch, actor, sprite, script,clientFactory,onDismissed,onOpened);
    }

    /// <summary>True only while the exact game build, official hook, dialogue adapter and owner are ready.</summary>
    public static bool IsAvailable => _started && !_fault && GameSession.OnThread &&
        GameBuildFingerprint.IsKnown && OfficialHookSubscriptions.ClientVisitReady && DialogueApi.IsAvailable;

    internal static void Start()
    {
        if (_started) return;
        _started = true;
        _fault = false;
        _generation++;
    }

    /// <summary>
    /// Queues one customer visit. actorId must be accepted by the game's StoreClientListDict factory;
    /// spriteName is the owner mod's loaded customer sprite key, without a path or extension.
    /// </summary>
    public static PresentationResult Queue(string ownerId, string requestId, string runId, int slotId,
        string actorId, string spriteName, DialogueScript script)
        =>QueueCore(ownerId,requestId,runId,slotId,actorId,spriteName,script,null,null,null);

    /// <summary>Internal path used by owned EZapi presentations that need a dialogue-only client instance.</summary>
    internal static PresentationResult QueueManaged(string ownerId,string requestId,string runId,int slotId,
        string actorId,string spriteName,DialogueScript script,Func<StoreClient> clientFactory,Action onDismissed,Action onOpened)
    {
        if(clientFactory is null||onDismissed is null||onOpened is null)
            return new(PresentationStatus.Invalid,"EZAPI_CLIENT_VISIT_MANAGED_CALLBACK");
        return QueueCore(ownerId,requestId,runId,slotId,actorId,spriteName,script,clientFactory,onDismissed,onOpened);
    }

    private static PresentationResult QueueCore(string ownerId,string requestId,string runId,int slotId,
        string actorId,string spriteName,DialogueScript script,Func<StoreClient>? clientFactory,Action? onDismissed,Action? onOpened)
    {
        if (!GameSession.OnThread || !IsAvailable)
            return new(PresentationStatus.Unavailable, "EZAPI_CLIENT_VISIT_UNAVAILABLE");
        if (!PresentationRules.Owned(ownerId, requestId) || script is null || script.OwnerId != ownerId ||
            !PresentationRules.Text(runId, 160) || string.IsNullOrWhiteSpace(runId) || slotId < 0 ||
            !Token(actorId, 160) || !Token(spriteName, 160))
            return new(PresentationStatus.Invalid, "EZAPI_CLIENT_VISIT_REQUEST");
        if (_busy) return new(PresentationStatus.Conflict, "EZAPI_CLIENT_VISIT_REENTRY");
        if (!GameSession.TryResolve(null, out _) || GameSession.Identity is not { } identity)
            return new(PresentationStatus.Unavailable, "EZAPI_CLIENT_VISIT_RUN");
        if (identity.RunId != runId || identity.SaveSlotId != slotId)
            return new(PresentationStatus.Conflict, "EZAPI_CLIENT_VISIT_SCOPE");

        var key = (ownerId, requestId);
        if (Visits.ContainsKey(key)) return new(PresentationStatus.Conflict, "EZAPI_CLIENT_VISIT_DUPLICATE");
        PruneHistory();
        if (Visits.Count >= MaxRequests) return new(PresentationStatus.Conflict, "EZAPI_CLIENT_VISIT_LIMIT");

        var visit = new Visit(ownerId, requestId, runId, slotId, identity.Epoch, actorId, spriteName, script,clientFactory,onDismissed,onOpened);
        Visits.Add(key, visit);
        Pending.Enqueue(visit);
        return new(PresentationStatus.Queued);
    }

    /// <summary>Reads the in-memory status for a request in the current process and game run.</summary>
    public static PresentationResult Status(string ownerId, string requestId)
    {
        if (!GameSession.OnThread || !_started)
            return new(PresentationStatus.Unavailable, "EZAPI_CLIENT_VISIT_UNAVAILABLE");
        if (!PresentationRules.Owned(ownerId, requestId))
            return new(PresentationStatus.Invalid, "EZAPI_CLIENT_VISIT_REQUEST");
        if (!Visits.TryGetValue((ownerId, requestId), out var visit))
            return new(PresentationStatus.Closed, "EZAPI_CLIENT_VISIT_UNKNOWN");
        if (!Current(visit)) return new(PresentationStatus.Dismissed, "EZAPI_CLIENT_VISIT_SCOPE_ENDED");
        return new(visit.State, visit.Reason);
    }

    /// <summary>Official OnGenerateCustomerVeryLate hook. Handles at most one queued visit per generation.</summary>
    internal static void OnGenerateCustomer(StoreClientManager? manager)
    {
        if (!_started || _fault || !GameSession.OnThread || _busy || manager is null || manager.Pointer == IntPtr.Zero)
            return;
        if (Pending.Count == 0) return;

        _busy = true;
        try
        {
            var visit = DequeueCurrent();
            if (visit is null) return;
            if (!Current(visit)) { Finish(visit, PresentationStatus.Dismissed, "EZAPI_CLIENT_VISIT_SCOPE_ENDED"); return; }

            StoreClient? client;
            try { client = visit.ClientFactory is null?StoreClientListDict.CreateStoreClient(visit.ActorId):visit.ClientFactory(); }
            catch (Exception error)
            {
                Finish(visit, PresentationStatus.Indeterminate, "EZAPI_CLIENT_VISIT_FACTORY: " + error.GetType().Name);
                Warn("EZAPI_CLIENT_VISIT_FACTORY", error);
                return;
            }
            if (client is null || client.Pointer == IntPtr.Zero || client.identifier != visit.ActorId)
            {
                Finish(visit, PresentationStatus.Invalid, "EZAPI_CLIENT_VISIT_ACTOR");
                return;
            }

            visit.Client = client;
            if (!PrepareNativeClient(visit, client))
            {
                ReleaseNativeClient(visit);
                Finish(visit, PresentationStatus.Indeterminate, "EZAPI_CLIENT_VISIT_PREPARE");
                return;
            }
            // Set before calling native code because AddNextClient may synchronously fire lifecycle callbacks.
            visit.State = PresentationStatus.Queued;
            try { manager.AddNextClient(client); }
            catch (Exception error)
            {
                var observed = Contains(manager.clientStack, client.Pointer);
                if (!observed && visit.State == PresentationStatus.Queued)
                    Finish(visit, PresentationStatus.Indeterminate, "EZAPI_CLIENT_VISIT_ADD: " + error.GetType().Name);
                Warn("EZAPI_CLIENT_VISIT_ADD", error);
                return;
            }

            if (visit.State == PresentationStatus.Queued && !Contains(manager.clientStack, client.Pointer))
                Finish(visit, PresentationStatus.Indeterminate, "EZAPI_CLIENT_VISIT_ADD_READBACK");
        }
        catch (Exception error)
        {
            Warn("EZAPI_CLIENT_VISIT_GENERATE", error);
        }
        finally { _busy = false; }
    }

    /// <summary>Runs deferred dialogue and cleanup work outside native customer callbacks.</summary>
    internal static void Maintain()
    {
        if (!_started || !GameSession.OnThread || _busy) return;
        foreach (var visit in Visits.Values.ToArray())
        {
            if (visit.ClosePending)
            {
                visit.ClosePending = false;
                if (Current(visit))
                {
                    var close = DialogueApi.Close(visit.Owner, visit.Request);
                    if (close.Status == PresentationStatus.Indeterminate)
                        Warn("EZAPI_CLIENT_VISIT_DIALOGUE_CLOSE", new InvalidOperationException(close.Reason ?? "close indeterminate"));
                }
            }
            if (visit.State is PresentationStatus.Dismissed or PresentationStatus.Invalid or PresentationStatus.Closed)
            {
                ReleaseNativeClient(visit);
                continue;
            }
            if (!Current(visit))
            {
                Finish(visit, PresentationStatus.Dismissed, "EZAPI_CLIENT_VISIT_SCOPE_ENDED");
                ReleaseNativeClient(visit);
                continue;
            }
            if (!visit.DialoguePending || visit.State != PresentationStatus.Arrived || _busy) continue;

            var now = Environment.TickCount64;
            if (now - _lastDialogueAttemptMs < 400) continue;
            _lastDialogueAttemptMs = now;
            if (visit.Script is null)
            {
                Finish(visit, PresentationStatus.Indeterminate, "EZAPI_CLIENT_VISIT_SCRIPT_RELEASED");
                continue;
            }
            if (now - visit.ArrivedAtMs > 30000)
            {
                visit.DialoguePending = false;
                visit.Script = null;
                Finish(visit, PresentationStatus.Indeterminate, "EZAPI_CLIENT_VISIT_DIALOGUE_TIMEOUT");
                continue;
            }
            try
            {
                _busy = true;
                var result = DialogueApi.Open(visit.Owner, visit.Request, visit.RunId, visit.SlotId, visit.Script);
                if (result.Status == PresentationStatus.Opened)
                {
                    visit.DialoguePending = false;
                    visit.DialogueOpened = true;
                    visit.Script = null; // DialogueApi now owns the active native graph and callback lifetime.
                    var opened=visit.OnOpened;visit.OnOpened=null;
                    try{opened?.Invoke();}catch(Exception error){Warn("EZAPI_CLIENT_VISIT_OPENED_CALLBACK",error);}
                }
                else if (result.Status != PresentationStatus.Conflict || result.Reason != "EZAPI_DIALOGUE_OCCUPIED")
                {
                    visit.DialoguePending = false;
                    visit.Script = null;
                    Finish(visit, PresentationStatus.Indeterminate, "EZAPI_CLIENT_VISIT_DIALOGUE: " + (result.Reason ?? result.Status.ToString()));
                }
            }
            catch (Exception error)
            {
                visit.DialoguePending = false;
                visit.Script = null;
                Finish(visit, PresentationStatus.Indeterminate, "EZAPI_CLIENT_VISIT_DIALOGUE: " + error.GetType().Name);
                Warn("EZAPI_CLIENT_VISIT_DIALOGUE", error);
            }
            finally { _busy = false; }
        }
    }

    internal static void SceneChanged()
    {
        if (!_started) return;
        if (GameSession.OnThread)
            foreach (var visit in Visits.Values.ToArray()) ReleaseNativeClient(visit);
        CancelAll("EZAPI_CLIENT_VISIT_SCENE_CHANGED");
    }

    internal static void Stop()
    {
        if (GameSession.OnThread)
            foreach (var visit in Visits.Values.ToArray()) ReleaseNativeClient(visit);
        CancelAll("EZAPI_CLIENT_VISIT_STOPPED");
        Visits.Clear();
        Pending.Clear();
        _started = false;
        _fault = false;
        _generation++;
    }

    private static bool PrepareNativeClient(Visit visit, StoreClient client)
    {
        try
        {
            client.spriteName = visit.SpriteName;
            if (client.spriteName != visit.SpriteName) return false;

            visit.ArrivalCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() => Arrived(visit)))
                ?? throw new InvalidOperationException("arrival delegate conversion failed");
            visit.DismissCallback = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() => Dismissed(visit)))
                ?? throw new InvalidOperationException("dismiss delegate conversion failed");

            client.OnArrival = Combine(client.OnArrival, visit.ArrivalCallback);
            client.OnDismiss = Combine(client.OnDismiss, visit.DismissCallback);
            if (!ContainsDelegate(client.OnArrival, visit.ArrivalCallback) || !ContainsDelegate(client.OnDismiss, visit.DismissCallback))
                return false;
            return true;
        }
        catch (Exception error)
        {
            Warn("EZAPI_CLIENT_VISIT_PREPARE", error);
            return false;
        }
    }

    private static Il2CppSystem.Action Combine(Il2CppSystem.Action? existing, Il2CppSystem.Action added)
        => existing is null ? added : Il2CppSystem.Delegate.Combine(existing, added).Cast<Il2CppSystem.Action>();

    private static bool ContainsDelegate(Il2CppSystem.Action? combined, Il2CppSystem.Action candidate)
    {
        if (combined is null) return false;
        foreach (var item in combined.GetInvocationList())
            if (item.Pointer == candidate.Pointer) return true;
        return false;
    }

    private static bool Contains(Il2CppSystem.Collections.Generic.List<StoreClient>? clients, IntPtr pointer)
    {
        if (clients is null || pointer == IntPtr.Zero) return false;
        for (var i = 0; i < clients.Count; i++)
            if (clients[i] is { } client && client.Pointer == pointer) return true;
        return false;
    }

    private static Visit? DequeueCurrent()
    {
        while (Pending.Count > 0)
        {
            var visit = Pending.Dequeue();
            if (visit.State == PresentationStatus.Queued && Visits.TryGetValue((visit.Owner, visit.Request), out var current) && current == visit)
                return visit;
        }
        return null;
    }

    private static void Arrived(Visit visit)
    {
        if (!_started || !GameSession.OnThread || !Current(visit) ||
            visit.State is PresentationStatus.Dismissed or PresentationStatus.Invalid) return;
        visit.State = PresentationStatus.Arrived;
        visit.Reason = null;
        visit.DialoguePending = true;
        visit.ArrivedAtMs = Environment.TickCount64;
    }

    private static void Dismissed(Visit visit)
    {
        if (!_started || !GameSession.OnThread) return;
        visit.State = PresentationStatus.Dismissed;
        visit.Reason = null;
        visit.DialoguePending = false;
        visit.ClosePending = visit.DialogueOpened;
        visit.DialogueOpened = false;
        visit.Script = null;
        var callback=visit.OnDismissed;visit.OnDismissed=null;
        try{callback?.Invoke();}catch(Exception error){Warn("EZAPI_CLIENT_VISIT_DISMISSED_CALLBACK",error);}
    }

    private static bool Current(Visit visit)
    {
        if (!_started || !GameSession.TryResolve(null, out _) || GameSession.Identity is not { } identity) return false;
        return identity.Epoch == visit.Epoch && identity.RunId == visit.RunId && identity.SaveSlotId == visit.SlotId;
    }

    private static void Finish(Visit visit, PresentationStatus status, string? reason)
    {
        visit.State = status;
        visit.Reason = reason;
        if (status is not PresentationStatus.Queued and not PresentationStatus.Arrived)
        {
            visit.DialoguePending = false;
            if (!visit.DialogueOpened) visit.Script = null;
        }
    }

    private static void ReleaseNativeClient(Visit visit)
    {
        var client = visit.Client;
        if (client is null)
        {
            ClearNativeVisitReferences(visit);
            return;
        }
        if (!GameSession.TryResolve(visit.Epoch, out _))
        {
            // Scene/run replacement owns the old wrapper now. Drop references without touching its native pointer.
            ClearNativeVisitReferences(visit);
            visit.DialogueOpened = false;
            visit.ClosePending = false;
            return;
        }
        try
        {
            if (visit.DialogueOpened && _started)
            {
                visit.DialogueOpened = false;
                var close = DialogueApi.Close(visit.Owner, visit.Request);
                if (close.Status == PresentationStatus.Indeterminate)
                    Warn("EZAPI_CLIENT_VISIT_DIALOGUE_CLOSE", new InvalidOperationException(close.Reason ?? "close indeterminate"));
            }
            if (visit.ArrivalCallback is not null) client.OnArrival = Remove(client.OnArrival, visit.ArrivalCallback);
            if (visit.DismissCallback is not null) client.OnDismiss = Remove(client.OnDismiss, visit.DismissCallback);
        }
        catch (Exception error)
        {
            _fault = true; // Do not retry partial delegate release against a possibly stale native object.
            Warn("EZAPI_CLIENT_VISIT_RELEASE_BLOCKED", error);
        }
        finally
        {
            ClearNativeVisitReferences(visit);
        }
    }

    private static void ClearNativeVisitReferences(Visit visit)
    {
        visit.Client = null;
        visit.ArrivalCallback = null;
        visit.DismissCallback = null;
        visit.ClientFactory = null;
        visit.OnDismissed = null;
        visit.OnOpened = null;
    }

    private static Il2CppSystem.Action? Remove(Il2CppSystem.Action? current, Il2CppSystem.Action owned)
    {
        if (current is null) return null;
        var removed = Il2CppSystem.Delegate.Remove(current, owned);
        return removed is null ? null : removed.Cast<Il2CppSystem.Action>();
    }

    private static void CancelAll(string reason)
    {
        foreach (var visit in Visits.Values)
        {
            if (visit.State is PresentationStatus.Queued or PresentationStatus.Arrived)
                Finish(visit, PresentationStatus.Dismissed, reason);
            visit.Script = null;
        }
        Pending.Clear();
        _busy = false;
        _lastDialogueAttemptMs = 0;
    }

    private static void PruneHistory()
    {
        if (Visits.Count < MaxRequests) return;
        foreach (var pair in Visits.Where(x => x.Value.State is PresentationStatus.Dismissed or PresentationStatus.Invalid or PresentationStatus.Closed)
                     .Take(Math.Max(1, Visits.Count - MaxRequests + 1)).ToArray())
            Visits.Remove(pair.Key);
    }

    private static bool Token(string? value, int max) => value is { Length: > 0 } && value.Length <= max &&
        value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-' or ':');

    private static void Warn(string code, Exception error)
        => MelonLogger.Warning("[EZapi] " + code + ": " + error);

    internal static object Report() => new
    {
        ready = IsAvailable,
        started = _started,
        faulted = _fault,
        hookReady = OfficialHookSubscriptions.ClientVisitReady,
        generation = _generation,
        queued = Visits.Values.Count(v => v.State == PresentationStatus.Queued),
        arrived = Visits.Values.Count(v => v.State == PresentationStatus.Arrived),
        requests = Visits.Values.Select(v => new { v.Owner, v.Request, v.RunId, v.SlotId, v.ActorId, state = v.State.ToString(), v.Reason }).ToArray()
    };
}

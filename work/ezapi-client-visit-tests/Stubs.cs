namespace EZapi
{
    internal sealed record GameRunIdentity(int SaveSlotId, string RunId, long Epoch);
    internal static class GameSession
    {
        internal static bool OnThread = true;
        internal static GameRunIdentity? Identity = new(12, "run-a", 1);
        internal static bool TryResolve(long? epoch, out object player)
        {
            player = new object();
            return OnThread && Identity is not null && (!epoch.HasValue || epoch.Value == Identity.Epoch);
        }
    }
    internal static class GameBuildFingerprint { internal static bool IsKnown = true; }
    internal static class OfficialHookSubscriptions { internal static bool ClientVisitReady = true; }
    internal static class DialogueApi
    {
        internal static bool IsAvailable = true;
        internal static readonly Queue<PresentationResult> Opens = new();
        internal static int OpenCalls, CloseCalls;
        internal static PresentationResult Open(string owner, string request, string run, int slot, DialogueScript script)
        {
            OpenCalls++;
            return Opens.Count == 0 ? new(PresentationStatus.Opened) : Opens.Dequeue();
        }
        internal static PresentationResult Close(string owner, string request) { CloseCalls++; return new(PresentationStatus.Closed); }
        internal static void Reset() { Opens.Clear(); OpenCalls = CloseCalls = 0; IsAvailable = true; }
    }
}

namespace MelonLoader
{
    internal static class MelonLogger
    {
        internal static readonly List<string> Warnings = new();
        internal static void Warning(string message) => Warnings.Add(message);
    }
}

namespace Il2CppSystem
{
    public class Delegate
    {
        internal sealed record Call(System.Action Callback, IntPtr Pointer);
        private readonly List<Call> _calls;
        internal IntPtr Pointer { get; }
        private static long _next;
        internal Delegate(System.Action call)
        {
            var pointer = (IntPtr)Interlocked.Increment(ref _next);
            _calls = new() { new Call(call, pointer) };
            Pointer = pointer;
        }
        internal Delegate(List<Call> calls)
        {
            _calls = calls;
            Pointer = (IntPtr)Interlocked.Increment(ref _next);
        }
        private Delegate(Call call) { _calls = new() { call }; Pointer = call.Pointer; }
        public Delegate[] GetInvocationList() => _calls.Select(c => new Delegate(c)).ToArray();
        public void Invoke() { foreach (var call in _calls.ToArray()) call.Callback(); }
        public static Delegate Combine(Delegate left, Delegate right) => new(left._calls.Concat(right._calls).ToList());
        public static Delegate? Remove(Delegate current, Delegate value)
        {
            var calls = current._calls.ToList();
            var index = calls.FindLastIndex(c => c.Pointer == value._calls[0].Pointer);
            if (index >= 0) calls.RemoveAt(index);
            return calls.Count == 0 ? null : new Delegate(calls);
        }
        public T Cast<T>() where T : Delegate => typeof(T) == typeof(Action)
            ? (T)(object)new Action(_calls)
            : (T)this;
    }
    public sealed class Action : Delegate
    {
        internal Action(System.Action call) : base(call) { }
        internal Action(List<Call> calls) : base(calls) { }
    }
    namespace Collections.Generic { public class List<T> : System.Collections.Generic.List<T> { } }
}

namespace Il2Cpp
{
    public sealed class StoreClient
    {
        private static long _next;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _next);
        public string identifier = "";
        public string spriteName = "vanilla";
        private Il2CppSystem.Action? _arrival, _dismiss;
        public bool RejectCallbackAccess;
        public Il2CppSystem.Action? OnArrival
        {
            get { if (RejectCallbackAccess) throw new InvalidOperationException("stale native callback read"); return _arrival; }
            set { if (RejectCallbackAccess) throw new InvalidOperationException("stale native callback write"); _arrival = value; }
        }
        public Il2CppSystem.Action? OnDismiss
        {
            get { if (RejectCallbackAccess) throw new InvalidOperationException("stale native callback read"); return _dismiss; }
            set { if (RejectCallbackAccess) throw new InvalidOperationException("stale native callback write"); _dismiss = value; }
        }
    }
    public sealed class StoreClientManager
    {
        private static long _next;
        public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _next);
        public Il2CppSystem.Collections.Generic.List<StoreClient> clientStack = new();
        public bool ThrowAfterAdd;
        public bool ThrowBeforeAdd;
        public void AddNextClient(StoreClient client)
        {
            if (ThrowBeforeAdd) throw new InvalidOperationException("before native insert");
            clientStack.Add(client);
            if (ThrowAfterAdd) throw new InvalidOperationException("after native insert");
        }
    }
    public static class StoreClientListDict
    {
        public static Func<string, StoreClient?>? Factory;
        public static StoreClient? CreateStoreClient(string actor) => Factory?.Invoke(actor);
    }
}

namespace Il2CppInterop.Runtime
{
    public static class DelegateSupport
    {
        public static T? ConvertDelegate<T>(System.Action callback) where T : class
        {
            if (typeof(T) == typeof(Il2CppSystem.Action)) return (T)(object)new Il2CppSystem.Action(callback);
            return null;
        }
    }
}

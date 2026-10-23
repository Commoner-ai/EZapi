namespace EZapi
{
    // Explicit test boundaries. No native factory, localization, object destruction
    // or timing assertion below substitutes for current-DLL gameplay evidence.
    internal static class GameSession
    {
        internal static readonly int Thread=Environment.CurrentManagedThreadId;
        internal static GameRunIdentity Run=new(5,"run",1);
        internal static void RequireThread() { if(Environment.CurrentManagedThreadId!=Thread) throw new InvalidOperationException("thread"); }
    }
    internal static class NativeAccess
    {
        internal static GameRunIdentity RequireRun() { GameSession.RequireThread(); if(GameSession.Run is null) throw new InvalidOperationException("not loaded"); return GameSession.Run; }
        internal static bool SameRun(GameRunIdentity run)=>GameSession.Run==run;
    }
    internal static class GameBuildFingerprint { internal static bool IsKnown=true; }
    internal static class NativeGuideLocale { internal static bool IsReady=true; }
    public sealed record Language(string LocaleCode,bool IsFallback=false);
    public static class GameLanguage { public static Language Current=new("zh"); }
    public static class ItemApi
    {
        internal static int Created,Destroyed;
        internal static bool? LastOwned;
        internal static Func<string,Il2Cpp.GameItem> Factory=id=>new(id);
        public static Il2Cpp.GameItem Create(string id,bool owned=true) { Created++;LastOwned=owned;return Factory(id); }
    }
}
namespace Il2Cpp
{
    public static class DirectoryMaster
    {
        internal static List<string>? Ids=new(){"z","a","a"};
        public static List<string>? GetIdentifierList<T>()=>Ids;
    }
    public sealed class GameItem
    {
        public GameItem(string id) { Id=id; }
        public string Id;
        public IntPtr Pointer=new(12);
        public object? parentInventory;
        public string spriteAtlasPath="native",spritePath="sprite";
        public Func<string>? Name;
        public Func<long>? Value;
        public Action? OnDestroy;
        public string GetDisplayName()=>Name?.Invoke()??(EZapi.GameLanguage.Current.LocaleCode=="en"?"English ":"中文 ")+Id;
        public long GetBaseValue()=>Value?.Invoke()??151;
        public void Destroy() { EZapi.ItemApi.Destroyed++;OnDestroy?.Invoke(); }
    }
}

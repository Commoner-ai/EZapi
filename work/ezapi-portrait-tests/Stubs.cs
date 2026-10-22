namespace UnityEngine
{
    public class Sprite
    {
        private static long _next;
        public IntPtr Pointer = new(Interlocked.Increment(ref _next));
    }
}
namespace Il2CppSystem.Collections.Generic
{
    public class Dictionary<K,V> : System.Collections.Generic.Dictionary<K,V> where K:notnull
    {
        private static long _next;
        public IntPtr Pointer = new(Interlocked.Increment(ref _next));
        public Action? AfterAdd;
        public bool ThrowRemove;
        public new void Add(K key,V value) { base.Add(key,value); AfterAdd?.Invoke(); }
        public new bool Remove(K key) { if(ThrowRemove) throw new Exception("remove failed"); return base.Remove(key); }
    }
}
namespace Il2Cpp
{
    public class GameItem
    {
        public IntPtr Pointer = new(42);
        public string? spriteAtlasPath, spritePath;
        public void SetSprite(string atlas,string key) => (spriteAtlasPath,spritePath)=(atlas,key);
    }
    public class SpriteDict
    {
        private static long _next;
        public IntPtr Pointer = new(Interlocked.Increment(ref _next));
        public static SpriteDict? Instance;
        public Il2CppSystem.Collections.Generic.Dictionary<string,UnityEngine.Sprite>? spriteDictionary = new();
        public Func<string,UnityEngine.Sprite?>? Resolver;
        public UnityEngine.Sprite? GetSprite(string key)
            => Resolver is not null ? Resolver(key) : spriteDictionary!.GetValueOrDefault(key);
    }
}
namespace MelonLoader
{
    public static class MelonLogger { public static void Warning(string message) { } }
}
namespace EZapi
{
    internal static class GameSession
    {
        internal static int Thread = Environment.CurrentManagedThreadId;
        internal static void RequireThread() { if(Thread!=Environment.CurrentManagedThreadId) throw new InvalidOperationException("THREAD"); }
    }
    // Native atlas/decode operations are explicit substitutes. Public atlas and portrait logic are real.
    internal static class LegacySpriteAdapter
    {
        internal static int Installed, Created;
        internal static bool NotReady;
        internal static string EmbeddedAtlasId(string owner,string key) => "EZapi.Assets/"+owner+"/"+key;
        internal static string EmbeddedSpriteCacheKey(string atlas,string key) => atlas+"/"+key;
        internal static void RequireAtlasSlot(string id) { if(NotReady)throw new SpriteAssetNotReadyException("wait"); }
        internal static UnityEngine.Sprite CreateEmbeddedSprite(string key,byte[] data,int width,int height,float x,float y,SpriteFilter filter,bool readable)
        { Created++; return new(); }
        internal static void InstallAtlas(string id,IReadOnlyDictionary<string,UnityEngine.Sprite> sprites) { Installed++; }
        internal static void ForgetSprite(string key) { }
    }
}

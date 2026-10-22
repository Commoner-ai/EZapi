// Only the native boundary is replaced; the real session, simple store and record API are linked.
namespace Il2CppSystem.Collections.Generic
{
    public class Dictionary<K, V> : System.Collections.Generic.Dictionary<K, V> where K : notnull
    {
        private static long _next;
        public IntPtr Pointer { get; } = new(Interlocked.Increment(ref _next));
        public Action? OnRead, BeforeWrite, AfterWrite;
        public int Writes;
        public new bool TryGetValue(K key, out V value)
        {
            OnRead?.Invoke(); return base.TryGetValue(key, out value!);
        }
        public new void Add(K key, V value)
        {
            BeforeWrite?.Invoke(); Writes++; base.Add(key, value); AfterWrite?.Invoke();
        }
        public new V this[K key]
        {
            get => base[key];
            set { BeforeWrite?.Invoke(); Writes++; base[key] = value; AfterWrite?.Invoke(); }
        }
    }
}
namespace Il2Cpp
{
    public class PlayerStore
    {
        private static long _next;
        public IntPtr Pointer = new(Interlocked.Increment(ref _next));
        public static PlayerStore? instance;
        public int saveSlotId = 533;
        public string runID = "test-run";
        public Il2CppSystem.Collections.Generic.Dictionary<string, string>? modData = new();
    }
}
namespace EZapi
{
    public enum GameEvent { GameLoadedInit, GameLoadedNormal }
}

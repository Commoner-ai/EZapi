namespace EZapi {
    internal static class GameSession {
        private static readonly int Thread=Environment.CurrentManagedThreadId;
        internal static bool Started=true;
        internal static bool OnThread=>Started&&Environment.CurrentManagedThreadId==Thread;
    }
    internal static class GameBuildFingerprint {internal static bool IsKnown=true;}
}
namespace Il2Cpp {
    internal static class LocHelper {
        internal static string? Locale="en";
        internal static int Reads;
        internal static bool Throw;
        public static string? GetCurrentLocaleCode(){Reads++;if(Throw)throw new InvalidOperationException("stub");return Locale;}
    }
}

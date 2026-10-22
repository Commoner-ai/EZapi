namespace MelonLoader {
    public static class MelonLogger {
        public static readonly List<(string Level,string Text)> Entries=new();
        public static void Msg(string s)=>Entries.Add(("Info",s));
        public static void Warning(string s)=>Entries.Add(("Warn",s));
        public static void Error(string s)=>Entries.Add(("Error",s));
    }
}
namespace EZapi {
    public static class MachineRegistrationApi {
        internal static ManufacturingTerminalView TerminalSnapshot()=>throw new NotSupportedException("Native registration boundary is not part of this pure-helper suite.");
    }
}

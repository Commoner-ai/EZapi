namespace EZapi;
// Native-boundary doubles only; registration, query/click isolation and layout
// execute the actual framework source linked by the project.
public sealed record GameLanguageSnapshot(string LocaleCode);
internal static class GameSession
{
    internal static bool OnThread = true;
    internal static void RequireThread() { if (!OnThread) throw new InvalidOperationException("thread"); }
}
internal static class GameBuildFingerprint { internal static bool IsKnown = true; }
internal static class DepartureRuntime
{
    internal static int Resets, Removed;
    internal static void InvalidateLayout() { }
    internal static void SceneChanged() => Resets++;
    internal static void Remove(string id) => Removed++;
}

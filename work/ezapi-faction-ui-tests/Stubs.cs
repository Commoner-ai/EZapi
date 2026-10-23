namespace EZapi;
internal static class GameSession { internal static bool OnThread=true; internal static void RequireThread(){if(!OnThread)throw new InvalidOperationException();} }
internal static class GameBuildFingerprint {internal static bool IsKnown=true;}
internal static class FactionReputationBrowser {
 internal static void RefreshSoon(){} internal static void Stop(){}
 internal static IReadOnlyList<FactionUiSnapshot> ReadCurrent()=>Array.Empty<FactionUiSnapshot>();
 internal static PresentationResult Open()=>new(PresentationStatus.Opened);
}
public sealed record LocalizedText(string Text);
public sealed class LocalizationCatalog {public LocalizedText Resolve(string locale,string key,string fallback,params object?[] args)=>new(fallback);}
public static class Localization {public const int MaxTextLength=8192;}

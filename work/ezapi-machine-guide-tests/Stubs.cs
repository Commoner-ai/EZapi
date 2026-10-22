namespace EZapi;
// 仅隔离离线测试不能接入的游戏线程、窗口与目录。被验证的规则、JSON、目录替换逻辑均链接生产源码。
internal static class GameSession {internal static bool OnThread=true;internal static void RequireThread(){if(!OnThread)throw new InvalidOperationException("thread");}}
internal static class RecipeGuideBrowser {internal static bool Visible;internal static bool Open()=>Visible=true;internal static void Close()=>Visible=false;}
internal static class NativeGuideLocale {internal static void Stop(){}}
public sealed record ContentSubmissionResult(bool Accepted,string? Reason);
internal static class ItemRegistry {internal static IReadOnlyList<ItemRegistrationResult> RegisterBatch(IReadOnlyList<ItemDefinition> items)=>throw new NotSupportedException();}

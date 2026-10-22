namespace EZapi.Sample;

/// <summary>
/// Schedules an existing game customer blueprint with the calling mod's portrait and an EZapi
/// dialogue. The game still owns that customer's trade behavior and native arrival/dismiss lifecycle.
/// </summary>
public static class ClientVisitExamples
{
    private const string Owner = "author.visit";
    private static readonly DialogueScript Script = DialogueApi.Create(Owner, new(
        Owner + ".greeting", "示例访客", "你好，需要了解什么？",
        new[]
        {
            new DialogueChoice(Owner + ".about", "了解这个 Mod", "确认后显示介绍。", _ => "欢迎使用 EZapi 内容 Mod。")
        }));

    /// <param name="context">A current lifecycle context whose Run supplies the save slot and run ID.</param>
    /// <param name="requestId">A unique ID owned by this mod, such as author.visit.chapter_1.</param>
    /// <param name="actorId">An ID accepted by the current game's StoreClientListDict factory.</param>
    /// <param name="spriteName">A loaded customer sprite key from this mod, without path or extension.</param>
    public static PresentationResult Queue(LifecycleContext context, string requestId, string actorId, string spriteName)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.IsCurrent) return new(PresentationStatus.Unavailable, "EZAPI_CLIENT_VISIT_SCOPE");
        return ClientVisitApi.Queue(Owner, requestId, context.Run.RunId, context.Run.SlotId, actorId, spriteName, Script);
    }

    public static PresentationResult Status(string requestId) => ClientVisitApi.Status(Owner, requestId);
}

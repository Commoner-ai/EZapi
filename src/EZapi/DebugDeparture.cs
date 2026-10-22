using MelonLoader;
namespace EZapi;

// Explicit regression opt-in only. Opens a read-only document; never starts a
// journey, consumes visits or saves. Both entries exercise shared allocation.
internal static class DebugDeparture
{
    private const string Owner = "ezapi.debug.departure";
    internal static void Register()
    {
        foreach (var suffix in new[] { "a", "b" })
        {
            var marker = suffix.ToUpperInvariant();
            DepartureApi.Register(Owner, new DepartureButton(Owner + "." + suffix, "Departure test " + marker, context =>
            {
                MelonLogger.Msg("[EZapi] Departure test " + marker + " clicked once; slot=" + context.SlotId);
                ReadingApi.Open(Owner, Owner + ".info", context.RunId, context.SlotId,
                    new LocalizedItemText("外出入口测试 " + marker, "Departure test " + marker),
                    new LocalizedItemText("自适应按钮点击已到达。此测试不扣次数、不进入目的地、不保存。", "Adaptive entry callback received. No visit consumed, destination entered or save requested."),
                    new LocalizedItemText("EZapi", "EZapi"));
            })
            {
                DisplayName = context => context.Language.Resolve(new LocalizedItemText("外出入口测试 " + marker, "Departure test " + marker)),
                RequireVisit = false,
                Order = 10000
            });
        }
    }
}

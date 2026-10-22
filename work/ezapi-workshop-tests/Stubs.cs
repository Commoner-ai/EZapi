using EZapi;

namespace EZapi;

internal static class WorkshopBrowser
{
    internal static bool Visible { get; private set; }
    internal static WorkshopSnapshot? LastSnapshot { get; private set; }
    internal static string? LastError { get; private set; }
    private static WorkshopRegistration? _registration;
    internal static bool IsShowing(WorkshopRegistration registration) => Visible && ReferenceEquals(_registration, registration);
    internal static bool Open(WorkshopRegistration registration)
    {
        try
        {
            LastSnapshot = WorkshopApi.Capture(registration);
            LastError = null;
            _registration = registration;
            Visible = true;
            return true;
        }
        catch (Exception error)
        {
            LastError = error.Message;
            Visible = false;
            return false;
        }
    }
    internal static void Close() { Visible = false; LastSnapshot = null; _registration = null; }
    internal static object Report() => new { Visible, LastError };
}

namespace EZapi;
internal static class ItemRegistry
{
    internal static bool TooLate;
    internal static string? DeclarationRegistrationError()=>TooLate?"EZAPI_TOO_LATE: initialization ended.":null;
}

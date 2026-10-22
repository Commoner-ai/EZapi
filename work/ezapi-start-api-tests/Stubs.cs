namespace Il2Cpp
{
    public static class NewGameData
    {
        public enum StartType
        {
            NONE = 0,
            WATER_MERCHANT = 1,
            APPRENTICE = 2,
            SCAV = 3,
            FENCE = 4,
            MOONSHINER = 5,
            DRUG_DEALER = 6,
            ORGAN_TRADER = 7,
            PHARMACIST = 8,
            STREET_FOOD_VENDOR = 9,
            FARMER = 10,
            GUNSMITH = 11,
            ROCK_BOTTOM = 12,
            RANCHER = 13
        }
    }
}

namespace EZapi
{
    internal static class PresentationRules
    {
        internal static bool Owner(string? value) => value is { Length: > 0 and <= 160 } &&
            System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z");
        internal static bool Owned(string? owner, string? id) => Owner(owner) && Owner(id) &&
            id!.StartsWith(owner + ".", StringComparison.Ordinal);
    }

    internal static class ItemRegistry
    {
        internal static bool Started { get; set; } = true;
        internal static bool TooLate { get; set; }
        internal static string? DeclarationRegistrationError() => !Started
            ? "EZAPI_NOT_READY: submit on initialization thread."
            : TooLate ? "EZAPI_TOO_LATE: register during initialization." : null;
    }
}

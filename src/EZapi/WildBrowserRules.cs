namespace EZapi;

// Pure rules shared by the native browser and offline boundary tests.
internal enum WildFilter { All, Purchased, Affordable }
internal sealed record WildCard(string Id, string Title, string Subtitle, bool Purchased, bool? Affordable);
internal static class WildBrowserRules
{
    internal static bool Matches(WildCard card, WildFilter filter, string query)
    {
        if (filter == WildFilter.Purchased && !card.Purchased || filter == WildFilter.Affordable && card.Affordable != true) return false;
        foreach (var word in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (!card.Title.Contains(word, StringComparison.OrdinalIgnoreCase) &&
                !card.Subtitle.Contains(word, StringComparison.OrdinalIgnoreCase) &&
                !card.Id.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
    internal static (int Columns, float Height) Grid(float width, float viewportHeight, int count)
    {
        int columns = Math.Max(1, (int)((width - 16 + 16) / 132));
        return (columns, Math.Max(viewportHeight, 16 + ((Math.Max(0, count) + columns - 1) / columns) * 132));
    }
}

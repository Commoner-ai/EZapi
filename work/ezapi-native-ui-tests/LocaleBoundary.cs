namespace EZapi;
// Layout validates inline bilingual text using the real type. CSV catalogs are not used here.
public static class Localization { public const int MaxTextLength = 8192; }
public sealed class LocalizationCatalog
{
    public Resolved Resolve(string locale, string key, string fallback, params object?[] args) => throw new NotSupportedException();
}
public sealed record Resolved(string Text);

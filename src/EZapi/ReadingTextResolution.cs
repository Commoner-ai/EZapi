namespace EZapi;

/// <summary>Resolves a complete reading page from one locale snapshot before native allocation begins.</summary>
internal sealed record ResolvedReadingText(string Title,string Body,string Signature);

internal static class ReadingTextResolution
{
    internal static ResolvedReadingText Resolve(string locale,LocalizedItemText title,LocalizedItemText body,LocalizedItemText signature)
    {
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(title);ArgumentNullException.ThrowIfNull(body);ArgumentNullException.ThrowIfNull(signature);
        title.Validate(512);body.Validate(131072);signature.Validate(4096);
        return new(title.ForLocale(locale)!,body.ForLocale(locale)!,signature.ForLocale(locale)!);
    }
}

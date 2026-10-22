namespace EZapi;

/// <summary>中英双语文本；zh* 使用中文，其它 locale 使用英文，缺失一侧回退另一侧。Key 可绑定所属内容包的 Localization/*.csv。</summary>
public sealed record LocalizedItemText(string? Chinese = null, string? English = null)
{
    /// <summary>可选所属内容包 CSV 键；ForLocale/For 只解析内联文字，Resolve 才读取 CSV。</summary>
    public string? Key { get; init; }
    public string? ForLocale(string? locale)
    {
        var chinese = locale?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
        var primary = chinese ? Chinese : English;
        return primary ?? (chinese ? English : Chinese);
    }

    /// <summary>按英文/中文显式选择，适合注册表查询和确定性离线工具。</summary>
    public string? For(bool english = false) => english ? English ?? Chinese : Chinese ?? English;
    /// <summary>声明阶段校验；至少一侧可读，已提供的一侧不可空白/超限，不会在渲染热路径执行。</summary>
    internal void Validate(int max,bool allowCsv=false)
    {
        if(Key is not null&&!allowCsv)throw new ArgumentException("EZAPI_LOCALIZED_TEXT_CATALOG: CSV keys require a content-pack field or the explicit catalog Resolve overload.");
        if(Chinese is null&&English is null&&(!allowCsv||Key is null))throw new ArgumentException("EZAPI_LOCALIZED_TEXT: provide Chinese/English text or a CSV key.");
        foreach(var text in new[]{Chinese,English})if(text is not null&&
            (string.IsNullOrWhiteSpace(text)||text.Length>max||text.Any(c=>char.IsControl(c)&&c is not ('\n' or '\r' or '\t'))))
            throw new ArgumentException("EZAPI_LOCALIZED_TEXT: readable text <="+max+" required.");
        if(Key is not null&&(Key.Length>160||!System.Text.RegularExpressions.Regex.IsMatch(Key,@"^[A-Za-z0-9_]+\z")))
            throw new ArgumentException("EZAPI_LOCALIZED_TEXT_KEY: key must be 1..160 ASCII letters, digits or underscores.");
    }

    /// <summary>按语言解析；有 Key 时依次查所给目录的语言表、英文表、内联文本和 readable fallback。</summary>
    public string Resolve(string locale,LocalizationCatalog? catalog,string fallback,params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(locale);ArgumentNullException.ThrowIfNull(args);Validate(Localization.MaxTextLength,allowCsv:true);
        if(string.IsNullOrWhiteSpace(fallback)||fallback.Length>Localization.MaxTextLength)
            throw new ArgumentException("EZAPI_LOCALIZED_TEXT_FALLBACK: readable fallback <=8192 characters required.",nameof(fallback));
        var readable=ForLocale(locale)??fallback;
        if(Key is null||catalog is null)return readable;
        return catalog.Resolve(locale,Key,readable,args).Text;
    }
}

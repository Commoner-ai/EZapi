namespace EZapi;

/// <summary>一次游戏语言观察。IsFallback=true时使用英文且Reason解释原因；不保存原生Locale引用。</summary>
public sealed record GameLanguageSnapshot(string LocaleCode,bool IsFallback,string? Reason=null)
{
    public bool IsChinese=>LocaleCode.StartsWith("zh",StringComparison.OrdinalIgnoreCase);

    /// <summary>以本次快照统一解析一组双语文本。声明至少提供一种语言，缺失一侧回退另一侧。</summary>
    public string Resolve(LocalizedItemText text)
    {
        ArgumentNullException.ThrowIfNull(text);text.Validate(Localization.MaxTextLength);
        if(text.Key is not null)throw new ArgumentException("EZAPI_LANGUAGE_CATALOG_REQUIRED: resolve a CSV-backed text with the catalog overload.",nameof(text));
        return text.ForLocale(LocaleCode)!;
    }

    /// <summary>按本次语言解析独立CSV目录；缺键使用作者提供的可读fallback，不返回内部键。</summary>
    public LocalizedText Resolve(LocalizationCatalog catalog,string key,string fallback,params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(catalog);return catalog.Resolve(LocaleCode,key,fallback,args);
    }

    /// <summary>按本次语言快照解析 CSV Key；缺键时回退内联双语文字及可读 fallback。</summary>
    public string Resolve(LocalizedItemText text,LocalizationCatalog catalog,string fallback,params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(text);ArgumentNullException.ThrowIfNull(catalog);
        return text.Resolve(LocaleCode,catalog,fallback,args);
    }
}

/// <summary>
/// 当前游戏语言的只读便捷入口。仅初始化后的主线程、已核对构建调用原生getter；其它情况带原因回退英文。
/// 不切换语言、不修改游戏字符串表。每次展示/刷新重新读取；缓存Snapshot不会随游戏语言自动变化。
/// </summary>
public static class GameLanguage
{
    public static GameLanguageSnapshot Current
    {
        get {
            if(!GameSession.OnThread)return Fallback("EZAPI_LANGUAGE_THREAD: use the initialized game main thread.");
            if(!GameBuildFingerprint.IsKnown)return Fallback("EZAPI_LANGUAGE_BUILD: current game build is not verified.");
            try {
                var code=Il2Cpp.LocHelper.GetCurrentLocaleCode();
                if(string.IsNullOrWhiteSpace(code)||code.Length>64||code.Any(c=>c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')))
                    return Fallback("EZAPI_LANGUAGE_LOCALE: native locale is missing or invalid.");
                return new(code,false);
            }catch(Exception error){return Fallback("EZAPI_LANGUAGE_READ: "+error.GetType().Name);}
        }
    }
    /// <summary>适合对话/电话/供应商等Func&lt;string&gt;动态文字，调用时读取当前语言。</summary>
    public static string Resolve(LocalizedItemText text)=>Current.Resolve(text);
    /// <summary>读取当前游戏语言解析文本或所属内容包 CSV；不修改游戏语言或字符串表。</summary>
    public static string Resolve(LocalizedItemText text,LocalizationCatalog catalog,string fallback,params object?[] args)
        =>Current.Resolve(text,catalog,fallback,args);
    public static LocalizedText Resolve(LocalizationCatalog catalog,string key,string fallback,params object?[] args)
        =>Current.Resolve(catalog,key,fallback,args);
    private static GameLanguageSnapshot Fallback(string reason)=>new("en",true,reason);
}

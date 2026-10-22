using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace EZapi;

/// <summary>
/// 官方 Localization/&lt;locale&gt;.csv 格式的纯托管读取入口。
/// 当前构建缺少 ModHelper.GetLocalized；此 API 不探测游戏语言、不修改游戏字符串表。
/// </summary>
public static class Localization
{
    private static readonly Dictionary<string, LocalizationCatalog> Packs = new(StringComparer.Ordinal);
    private static readonly object PackLock = new();
    public const int MaxFileBytes = 1048576;
    public const int MaxEntries = 4096;
    public const int MaxTextLength = 8192;
    public static IReadOnlyList<string> SupportedLocales { get; } = Array.AsReadOnly(new[] {
        "en", "fr", "de", "ru", "zh", "ja", "es", "ptBR", "ko", "it"
    });

    /// <summary>只返回已成功排队内容包的只读快照；未知/拒绝包返回 null。不代表原生目录已 Applied。</summary>
    public static LocalizationCatalog? GetPack(string packId)
    {
        ArgumentNullException.ThrowIfNull(packId);
        lock (PackLock) return Packs.TryGetValue(packId, out var catalog) ? catalog : null;
    }
    internal static void CommitPack(string packId, LocalizationCatalog catalog) { lock (PackLock) Packs[packId] = catalog; }
    internal static void ClearPacks() { lock (PackLock) Packs.Clear(); }

    /// <summary>UTF-8（允许 BOM），无表头；保留输入流所有权。错误包含物理行号。</summary>
    public static LocalizationTable Parse(Stream csv, string locale)
    {
        ArgumentNullException.ThrowIfNull(csv);
        ValidateLocale(locale);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = csv.Read(buffer, 0, buffer.Length)) > 0) {
            if (bytes.Length + count > MaxFileBytes) throw Error("LIMIT", "max 1 MiB per file.");
            bytes.Write(buffer, 0, count);
        }
        string source;
        try { source = new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
        catch (DecoderFallbackException e) { throw Error("ENCODING", "expected valid UTF-8.", e); }
        if (source.StartsWith('\uFEFF')) source = source[1..];
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(source);
        string? line; int number = 0;
        while ((line = reader.ReadLine()) is not null) {
            number++;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            try {
                var comma = line.IndexOf(',');
                if (comma < 0) throw Error("ROW", "expected key,text; actual multiline values are unsupported.");
                var key = line[..comma]; ValidateKey(key);
                var value = DecodeValue(line[(comma + 1)..]);
                if (!entries.TryAdd(key, value)) throw Error("DUPLICATE", "duplicate key: " + key);
                if (entries.Count > MaxEntries) throw Error("LIMIT", "max 4096 entries per file.");
            } catch (InvalidDataException e) {
                throw new InvalidDataException(locale + ".csv:" + number + ": " + e.Message, e);
            }
        }
        return new(locale, new ReadOnlyDictionary<string, string>(entries));
    }

    /// <summary>只读取指定 Localization 目录内的直接 .csv 文件；缺少目录返回空目录。每次调用创建独立快照。</summary>
    public static LocalizationCatalog LoadDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var tables = new Dictionary<string, LocalizationTable>(StringComparer.Ordinal);
        if (Directory.Exists(directory)) {
            var paths = Directory.EnumerateFiles(directory).Where(p =>
                string.Equals(Path.GetExtension(p), ".csv", StringComparison.OrdinalIgnoreCase)).Take(11).ToArray();
            if (paths.Length > SupportedLocales.Count) throw Error("LIMIT", "max 10 locale files.");
            Array.Sort(paths, StringComparer.Ordinal);
            foreach (var path in paths) {
                var locale = Path.GetFileNameWithoutExtension(path);
                ValidateLocale(locale);
                if (Path.GetFileName(path) != locale + ".csv") throw Error("LOCALE", "expected exact file name: " + locale + ".csv");
                using var file = File.OpenRead(path);
                if (!tables.TryAdd(locale, Parse(file, locale))) throw Error("DUPLICATE", "duplicate locale: " + locale);
            }
        } else if (File.Exists(directory)) throw Error("PATH", "expected a Localization directory.");
        return new(new ReadOnlyDictionary<string, LocalizationTable>(tables));
    }

    private static string DecodeValue(string raw)
    {
        if (raw.Length > MaxTextLength * 2 + 2) throw Error("LIMIT", "text too long.");
        string value;
        if (raw.StartsWith('"')) {
            var output = new StringBuilder(); bool closed = false;
            for (int i = 1; i < raw.Length; i++) {
                if (raw[i] != '"') { output.Append(raw[i]); continue; }
                if (i + 1 < raw.Length && raw[i + 1] == '"') { output.Append('"'); i++; continue; }
                if (i != raw.Length - 1) throw Error("QUOTE", "unexpected text after closing quote.");
                closed = true;
            }
            if (!closed) throw Error("QUOTE", "unclosed quote; use literal \\n for line breaks.");
            value = output.ToString();
        } else value = raw;
        value = value.Replace("\\n", "\n", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(value)) throw Error("TEXT", "nonblank text required.");
        if (value.Length > MaxTextLength) throw Error("LIMIT", "max 8192 characters per text.");
        if (value.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) throw Error("TEXT", "unsupported control character.");
        FormatIndexes(value);
        return value;
    }

    // 限制占位符下标/对齐宽度，防止错误声明触发大分配。只支持官方编号 string.Format。
    internal static IReadOnlyList<int> FormatIndexes(string text)
    {
        var indexes = new SortedSet<int>();
        for (int i = 0; i < text.Length; i++) {
            var c = text[i];
            if (c is not '{' and not '}') continue;
            if (i + 1 < text.Length && text[i + 1] == c) { i++; continue; }
            if (c == '}') throw Error("FORMAT", "unescaped closing brace.");
            var end = text.IndexOf('}', i + 1);
            if (end < 0) throw Error("FORMAT", "unclosed placeholder.");
            var match = Regex.Match(text[(i + 1)..end], @"^(0|[1-9][0-9]?)(?:,(-?(?:0|[1-9][0-9]{0,2})))?(?::([^{}]{0,64}))?\z");
            if (!match.Success || int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) > 31 ||
                (match.Groups[2].Success && Math.Abs(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)) > 256))
                throw Error("FORMAT", "use numbered {0}..{31}; alignment <=256, format <=64; Smart Strings unsupported.");
            var precision = Regex.Match(match.Groups[3].Value, @"^[A-Za-z]([0-9]+)\z");
            if (precision.Success && (!int.TryParse(precision.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var digits) || digits > 256))
                throw Error("FORMAT", "standard numeric format precision must be <=256.");
            indexes.Add(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
            i = end;
        }
        return indexes.ToArray();
    }
    internal static void ValidateKey(string key)
    {
        if (key is null || key.Length > 160 || !Regex.IsMatch(key, @"^[A-Za-z0-9_]+\z"))
            throw Error("KEY", "key must be 1..160 ASCII letters/digits/underscores.");
    }
    private static void ValidateLocale(string locale)
    {
        if (!SupportedLocales.Contains(locale, StringComparer.Ordinal)) throw Error("LOCALE", "unknown official locale code: " + locale);
    }
    internal static InvalidDataException Error(string code, string message, Exception? inner = null)
        => new("EZAPI_LOCALIZATION_" + code + ": " + message, inner);
}

/// <summary>只读文件快照；没有 Unity、原生字符串表或存档引用。</summary>
public sealed record LocalizationTable(string Locale, IReadOnlyDictionary<string, string> Entries);
public sealed record LocalizationResource(string Locale, string File, int Entries, IReadOnlyList<string> Keys,
    IReadOnlyList<string> MissingEnglishKeys);
public sealed record LocalizationIndex(int Files, int Entries, IReadOnlyList<LocalizationResource> Resources,
    IReadOnlyList<string> Warnings);
public sealed record LocalizedText(string Text, string RequestedLocale, string? ResolvedLocale,
    bool UsedFallback, bool MissingKey, string? FormatError);

/// <summary>按显式语言代码解析；不缓存已翻译物品名称。切换语言时由调用者重新查询。</summary>
public sealed class LocalizationCatalog
{
    public IReadOnlyDictionary<string, LocalizationTable> Tables { get; }
    public LocalizationIndex Index { get; }
    internal LocalizationCatalog(IReadOnlyDictionary<string, LocalizationTable> tables)
    {
        Tables = tables;
        var english = tables.TryGetValue("en", out var en) ? en.Entries : new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        var resources = new List<LocalizationResource>(); var warnings = new List<string>();
        foreach (var (locale, table) in tables) {
            foreach (var (key, value) in table.Entries) {
                if (english.TryGetValue(key, out var original) &&
                    !Localization.FormatIndexes(value).SequenceEqual(Localization.FormatIndexes(original)))
                    throw Localization.Error("PLACEHOLDERS", locale + ".csv: placeholder indexes differ from en for " + key);
            }
            var missing = english.Keys.Except(table.Entries.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0) warnings.Add(locale + ": " + missing.Length + " English key(s) missing; will fall back to en.");
            var extra = table.Entries.Keys.Except(english.Keys, StringComparer.Ordinal).Count();
            if (extra > 0) warnings.Add(locale + ": " + extra + " key(s) have no English fallback.");
            resources.Add(new(locale, "Localization/" + locale + ".csv", table.Entries.Count,
                Array.AsReadOnly(table.Entries.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray()), Array.AsReadOnly(missing)));
        }
        Index = new(tables.Count, tables.Values.Sum(t => t.Entries.Count), resources.AsReadOnly(), warnings.AsReadOnly());
    }

    /// <summary>
    /// 当前代码 → en → 调用者可读 fallback。InvariantCulture 格式化；缺参数/格式错误返回未格式化文本和错误，避免界面崩溃。
    /// fallback 是纯文本，不参与格式化，不能使用内部 ID 作为正常玩家文案。
    /// </summary>
    public LocalizedText Resolve(string locale, string key, string fallback, params object?[] args)
    {
        ArgumentNullException.ThrowIfNull(locale); Localization.ValidateKey(key);
        if (string.IsNullOrWhiteSpace(fallback) || fallback.Length > Localization.MaxTextLength)
            throw new ArgumentException("EZAPI_LOCALIZATION_FALLBACK: readable fallback <=8192 characters required.", nameof(fallback));
        ArgumentNullException.ThrowIfNull(args);
        var canonical=CanonicalLocale(locale);
        string? resolved = null; string text = fallback;
        if (canonical is not null && Tables.TryGetValue(canonical, out var table) && table.Entries.TryGetValue(key, out var current)) { text = current; resolved = canonical; }
        else if (Tables.TryGetValue("en", out var en) && en.Entries.TryGetValue(key, out var english)) { text = english; resolved = "en"; }
        if (resolved is null) return new(text, locale, null, true, true, null);
        var usedFallback=canonical is null||resolved!=canonical;
        try {
            var formatted = string.Format(CultureInfo.InvariantCulture, text, args);
            if (formatted.Length > 65536) return new(text, locale, resolved, usedFallback, false, "EZAPI_LOCALIZATION_FORMAT: output exceeds 65536 characters.");
            return new(formatted, locale, resolved, usedFallback, false, null);
        } catch (FormatException e) { return new(text, locale, resolved, usedFallback, false, "EZAPI_LOCALIZATION_FORMAT: " + e.Message); }
    }

    internal bool HasEnglishKey(string key)=>Tables.TryGetValue("en",out var table)&&table.Entries.ContainsKey(key);

    private static string? CanonicalLocale(string locale)
    {
        var normalized=locale.Replace('_','-');
        if(normalized.Equals("pt-BR",StringComparison.OrdinalIgnoreCase)||normalized.Equals("ptBR",StringComparison.OrdinalIgnoreCase))return "ptBR";
        var language=normalized.Split('-',2)[0];
        return Localization.SupportedLocales.FirstOrDefault(code=>string.Equals(code,language,StringComparison.OrdinalIgnoreCase));
    }
}

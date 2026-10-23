namespace EZapi;

/// <summary>物品声明的纯托管文字路由；工厂、Tooltip、分类查询和离线工具共用，原始定义保持不变。</summary>
internal static class ItemText
{
    internal static LocalizationCatalog? Catalog(ItemDefinition definition)
        => definition.ModId is { } owner ? Localization.GetPack(owner) : null;

    internal static string Resolve(ItemDefinition definition, LocalizedItemText? text, string? fallback, string locale)
    {
        if (text is null) return fallback ?? "";
        // Inline fields were validated at declaration. NativeItemOptions deliberately allows
        // empty descriptions: use its exact locale/null semantics again on every refresh.
        // Generic LocalizedItemText.Resolve requires readable text and is still used for CSV.
        if (text.Key is null) return text.ForLocale(locale) ?? fallback ?? "";
        return text.Resolve(locale, Catalog(definition), string.IsNullOrWhiteSpace(fallback) ? "EZapi" : fallback!);
    }

    // An all-null optional DLL description supplies no override. Do not turn it into a clear.
    internal static bool HasTranslation(LocalizedItemText? text)
        => text is not null && (text.Key is not null || text.Chinese is not null || text.English is not null);

    internal static string Name(ItemDefinition definition, string locale)
        => Resolve(definition, definition.LocalizedName, definition.Name, locale);

    internal static bool IsGeneratedName(ItemDefinition d,string current)
    {
        if(string.IsNullOrEmpty(current)||current==d.Name)return true;
        foreach(var locale in Localization.SupportedLocales)if(current==Name(d,locale))return true;
        return false;
    }
    internal static string? Description(ItemDefinition d,string locale)
    {
        if(d.LocalizedShortDescription is not null||d.LocalizedDescription is not null||d.Description is not null||d.ShortDescription is not null)
            return Resolve(d,d.LocalizedShortDescription??d.LocalizedDescription,d.Description??d.ShortDescription,locale);
        return d.LocalizedLongDescription is not null||d.LongDescription is not null?Resolve(d,d.LocalizedLongDescription,d.LongDescription,locale):null;
    }
    internal static string TypeSignature(ItemTypeNameDefinition type,string? owner)
        => type.LocalizedName is null?type.Name:System.Text.Json.JsonSerializer.Serialize(new{type.Name,type.LocalizedName,Owner=type.LocalizedName.Key is null?null:owner});

    internal static IEnumerable<(string Path, LocalizedItemText Text, int Limit)> Bindings(ItemDefinition d)
    {
        if (d.LocalizedName is { } name) yield return ("localizedName", name, 200);
        if (d.LocalizedDescription is { } description) yield return ("localizedDescription", description, 4000);
        if (d.LocalizedShortDescription is { } shortText) yield return ("localizedShortDescription", shortText, 4000);
        if (d.LocalizedLongDescription is { } longText) yield return ("localizedLongDescription", longText, 8000);
        if (d.LocalizedFlavorText is { } flavor) yield return ("localizedFlavorText", flavor, 4000);
        if (d.LocalizedCustomText is { } custom) yield return ("localizedCustomText", custom, 4000);
        if (d.Container?.LocalizedTitle is { } title) yield return ("container.localizedTitle", title, 200);
        if (d.Module?.LocalizedAdditionalEffectDisplay is { } effect) yield return ("module.localizedAdditionalEffectDisplay", effect, 4000);
        if (d.Tooltip?.LocalizedSourceName is { } source) yield return ("tooltip.localizedSourceName", source, 200);
        foreach (var type in d.TypeNames ?? Array.Empty<ItemTypeNameDefinition>())
            if (type?.LocalizedName is { } typeName) yield return ("typeNames[" + type.Id + "].localizedName", typeName, 200);
        int index = 0;
        foreach (var label in d.Tooltip?.Labels ?? Array.Empty<TooltipLabelDefinition>()) {
            if (label?.LocalizedText is { } text) yield return ($"tooltip.labels[{index}].localizedText", text, 200);
            index++;
        }
        index = 0;
        foreach (var line in d.Tooltip?.Lines ?? Array.Empty<TooltipLineDefinition>()) {
            if (line?.LocalizedText is { } text) yield return ($"tooltip.lines[{index}].localizedText", text, 2000);
            index++;
        }
    }

    internal static void ValidateCatalog(ItemDefinition definition, LocalizationCatalog catalog)
    {
        foreach (var binding in Bindings(definition)) {
            if (binding.Text.Key is not { } key) continue;
            if (!catalog.HasEnglishKey(key))
                throw new InvalidDataException($"EZAPI_ITEM_LOCALIZATION: {definition.Id}.{binding.Path}: English CSV key missing: {key}");
            foreach (var table in catalog.Tables.Values) {
                if (!table.Entries.TryGetValue(key, out var value)) continue;
                if (value.Length > binding.Limit || Localization.FormatIndexes(value).Count != 0)
                    throw new InvalidDataException($"EZAPI_ITEM_LOCALIZATION: {definition.Id}.{binding.Path}: {table.Locale}.csv key {key} must be <= {binding.Limit} characters without format placeholders.");
            }
        }
    }
}

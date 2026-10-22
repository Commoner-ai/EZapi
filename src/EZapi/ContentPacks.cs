using System.Text.Json;
using System.Text.RegularExpressions;
namespace EZapi;

/// <summary>
/// 无游戏依赖的整包入口，CLI 与运行时共用。检查只读文件，不提交声明、不解码图片。
/// 单包 Check 不解析依赖是否安装；CheckDirectory 才解析该目录内的完整依赖图。
/// </summary>
public static class ContentPacks
{
    public static PackManifest ParseManifest(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var bytes = new MemoryStream();
        var block = new byte[8192]; int count;
        while ((count = json.Read(block, 0, block.Length)) > 0) {
            if (bytes.Length + count > 65536) throw new InvalidDataException("EZAPI_PACK_LIMIT: manifest max 64 KiB.");
            bytes.Write(block, 0, count);
        }
        using var doc = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions {MaxDepth = 16});
        JsonItems.ValidateObject(doc.RootElement, typeof(PackManifest), "pack");
        var result = doc.RootElement.Deserialize<PackManifest>(new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase})!;
        ValidateOwner(result.Id);
        if (string.IsNullOrWhiteSpace(result.Name) || result.Name.Length > 200)
            throw new InvalidDataException("EZAPI_PACK_NAME: readable name <=200 characters required.");
        if (result.EzapiVersion != FrameworkInfo.JsonProtocolVersion)
            throw new InvalidDataException("EZAPI_VERSION: expected ezapiVersion=" + FrameworkInfo.JsonProtocolVersion);
        ParseVersion(result.Version);
        if (result.MinEzapiVersion is not null && ParseVersion(result.MinEzapiVersion) > ParseVersion(FrameworkInfo.Version))
            throw new InvalidDataException("EZAPI_PACK_FRAMEWORK: requires EZapi >= " + result.MinEzapiVersion + "; current " + FrameworkInfo.Version);
        if (result.Requires.Count > 64 || result.Requires.Distinct(StringComparer.Ordinal).Count() != result.Requires.Count)
            throw new InvalidDataException("EZAPI_PACK_CAPABILITY: max 64 unique capability IDs.");
        foreach (var capability in result.Requires)
            if (!FrameworkInfo.Supports(capability)) throw new InvalidDataException("EZAPI_PACK_CAPABILITY: unavailable: " + capability);
        if (result.Dependencies.Count > 64) throw new InvalidDataException("EZAPI_PACK_DEPENDENCY: max 64 dependencies.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in result.Dependencies) {
            ValidateOwner(dependency.Id); ParseVersion(dependency.MinVersion);
            if (dependency.Id == result.Id || !ids.Add(dependency.Id))
                throw new InvalidDataException("EZAPI_PACK_DEPENDENCY: duplicate/self dependency: " + dependency.Id);
        }
        return result;
    }

    public static PackValidationResult Check(string folder)
    {
        try { return Read(folder).Result; }
        catch (Exception e) when (IsContentError(e)) { return Failure(folder, e); }
    }

    public static PackDirectoryValidationResult CheckDirectory(string root)
    {
        var plan = Plan(root);
        return new(plan.Results.All(r => r.Valid), plan.Results.AsReadOnly(), plan.Order.Select(p => p.Manifest.Id).ToArray());
    }

    internal static CheckedPack Read(string folder)
    {
        folder = Path.GetFullPath(folder);
        using var json = File.OpenRead(Path.Combine(folder, "pack.json"));
        var manifest = ParseManifest(json);
        using var input = File.OpenRead(Path.Combine(folder, "items.json"));
        var items = JsonItems.Parse(input);
        var typeNames = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var item in items) {
            if (item.ModId != manifest.Id || !item.Id.StartsWith(manifest.Id + ".", StringComparison.Ordinal))
                throw new InvalidDataException("EZAPI_OWNER: item ID/modId must belong to " + manifest.Id);
            PngAssets.Prepare(item, folder);
            foreach (var type in item.TypeNames) {
                if (typeNames.TryGetValue(type.Id, out var old) && old != ItemText.TypeSignature(type,item.ModId))
                    throw new InvalidDataException("EZAPI_ITEM_TYPE: conflicting display name: " + type.Id);
                typeNames[type.Id] = ItemText.TypeSignature(type,item.ModId);
            }
        }
        var lootFolder = Path.Combine(folder, "LootTables");
        // 不递归；只枚举直接文件。Take 限制后续处理量，不按文件内容决定排序。
        var paths = Directory.Exists(lootFolder) ? Directory.EnumerateFiles(lootFolder, "*.json").Take(17).ToArray() : Array.Empty<string>();
        if (paths.Length > 16) throw new InvalidDataException("EZAPI_LOOT_LIMIT: max 16 LootTables files per pack.");
        Array.Sort(paths, StringComparer.Ordinal);
        var loot = new List<LootDefinition>();
        foreach (var path in paths) {
            try { using var file = File.OpenRead(path); loot.Add(JsonLoot.Parse(file)); }
            catch (Exception e) when (IsContentError(e)) { throw new InvalidDataException("LootTables/" + Path.GetFileName(path) + ": " + e.Message, e); }
        }
        if (loot.Sum(JsonLoot.OperationCount) > 4096) throw new InvalidDataException("EZAPI_LOOT_LIMIT: max 4096 operations per owner.");
        var lists = new HashSet<(bool Group, string Id)>();
        foreach (var definition in loot) foreach (var group in new[] {false, true})
            foreach (var list in group ? definition.Groups : definition.Tables) {
                if (!list.Id.StartsWith(manifest.Id + ".", StringComparison.Ordinal))
                    throw new InvalidDataException("EZAPI_LOOT_OWNER: new table/group must belong to " + manifest.Id);
                if (!lists.Add((group, list.Id))) throw new InvalidDataException("EZAPI_LOOT_DUPLICATE: " + list.Id);
            }
        var declared = new HashSet<(bool Group,string Id)>();
        var itemIds = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        // 子命名空间依赖（如 author.base.addon）不能误判为当前包缺失的自有条目。
        bool IsOwn(string id) => id.StartsWith(manifest.Id + ".",StringComparison.Ordinal) &&
            !manifest.Dependencies.Any(d => d.Id.Length > manifest.Id.Length && id.StartsWith(d.Id + ".",StringComparison.Ordinal));
        void CheckEntries(bool group,IReadOnlyList<LootWeightDefinition> entries) {
            foreach (var row in entries) {
                if (!IsOwn(row.Id)) continue;
                if (group ? !declared.Contains((false,row.Id)) : !itemIds.Contains(row.Id))
                    throw new InvalidDataException("EZAPI_LOOT_REFERENCE: missing/forward own reference: " + row.Id);
            }
        }
        foreach (var definition in loot) {
            foreach (var group in new[] {false,true}) foreach (var list in group ? definition.Groups : definition.Tables) {
                declared.Add((group,list.Id)); CheckEntries(group,list.Entries);
            }
            foreach (var patch in definition.Patches) {
                var group=patch.Group is not null; var id=patch.Table??patch.Group!;
                if (IsOwn(id) && !declared.Contains((group,id)))
                    throw new InvalidDataException("EZAPI_LOOT_TARGET: missing/forward own list: " + id);
                CheckEntries(group,patch.Add); CheckEntries(group,patch.SetWeight);
            }
        }
        // CSV 在任何目录/物品声明提交之前完成整份校验，失败不会留下半个包。
        var localization = Localization.LoadDirectory(Path.Combine(folder, "Localization"));
        foreach(var item in items) ItemText.ValidateCatalog(item,localization);
        return new(folder, manifest, items, loot.AsReadOnly(), localization);
    }

    internal static PackPlan Plan(string root)
    {
        var directories = Directory.EnumerateDirectories(root).Take(257).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (directories.Length > 256) throw new InvalidDataException("EZAPI_PACK_LIMIT: max 256 direct content pack folders.");
        var results = new List<PackValidationResult>(); var packs = new List<CheckedPack>();
        foreach (var folder in directories) {
            try { var pack = Read(folder); packs.Add(pack); results.Add(pack.Result); }
            catch (Exception e) when (IsContentError(e)) { results.Add(Failure(folder, e)); }
        }
        void Reject(CheckedPack p, string error) {
            var i = results.FindIndex(r => r.Folder == p.Folder);
            if (results[i].Valid) results[i] = results[i] with {Valid = false, Error = error};
        }
        // 同一 ID 的所有副本都拒绝，避免按目录名偶然选择一个版本。
        foreach (var duplicate in packs.GroupBy(p => p.Manifest.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            foreach (var p in duplicate) Reject(p, "EZAPI_PACK_DUPLICATE: " + duplicate.Key);
        var unique = packs.GroupBy(p => p.Manifest.Id, StringComparer.Ordinal).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<CheckedPack>();
        bool Visit(CheckedPack p) {
            var result = results.Single(r => r.Folder == p.Folder);
            if (!result.Valid) return false;
            if (state.TryGetValue(p.Manifest.Id, out var s)) {
                if (s == 2) return true;
                Reject(p, "EZAPI_PACK_DEPENDENCY: cycle involving " + p.Manifest.Id); return false;
            }
            state[p.Manifest.Id] = 1;
            foreach (var d in p.Manifest.Dependencies) {
                if (!unique.TryGetValue(d.Id, out var target) ||
                    ParseVersion(target.Manifest.Version) < ParseVersion(d.MinVersion) || !Visit(target)) {
                    Reject(p, "EZAPI_PACK_DEPENDENCY: unavailable/old/cyclic dependency " + d.Id + " >= " + d.MinVersion); return false;
                }
            }
            state[p.Manifest.Id] = 2; order.Add(p); return true;
        }
        foreach (var pack in packs) Visit(pack);
        return new(results, order);
    }

    internal static Version ParseVersion(string value)
    {
        // 明确采用三段非负十进制，不接受浮点、范围、预发布后缀或四段 assembly version。
        if (!Regex.IsMatch(value, @"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\z"))
            throw new InvalidDataException("EZAPI_PACK_VERSION: expected major.minor.patch: " + value);
        return Version.Parse(value);
    }
    private static void ValidateOwner(string id)
    {
        if (id.Length > 160 || !Regex.IsMatch(id, @"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z"))
            throw new InvalidDataException("EZAPI_PACK_ID: invalid ID: " + id);
    }
    private static bool IsContentError(Exception e) => e is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or FormatException or OverflowException;
    private static PackValidationResult Failure(string folder, Exception error) => new(Path.GetFullPath(folder), null, null, false, error.Message, 0, 0, Array.Empty<string>());
}
internal sealed record CheckedPack(string Folder, PackManifest Manifest, IReadOnlyList<ItemDefinition> Items, IReadOnlyList<LootDefinition> Loot, LocalizationCatalog Localization)
{
    internal PackValidationResult Result => new(Folder, Manifest.Id, Manifest.Version, true, null, Items.Count, Loot.Count, Manifest.Dependencies.Select(d => d.Id).ToArray()) { Localization = Localization.Index };
}
internal sealed record PackPlan(List<PackValidationResult> Results, List<CheckedPack> Order);

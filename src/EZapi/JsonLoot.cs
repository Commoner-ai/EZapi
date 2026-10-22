using System.Text.Json;
using System.Text.RegularExpressions;
namespace EZapi;

/// <summary>无原生依赖的掉落解析/校验；运行时与 CLI 共用，不解析注释或重复/未知/null 字段。</summary>
public static class JsonLoot
{
    private static readonly JsonSerializerOptions Options=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    public static LootDefinition Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var data=new MemoryStream();var block=new byte[81920];int count;
        while((count=json.Read(block,0,block.Length))>0) {
            if(data.Length+count>1024*1024)throw new InvalidDataException("EZAPI_LOOT_LIMIT: max 1 MiB per file.");
            data.Write(block,0,count);
        }
        using var document=JsonDocument.Parse(data.ToArray(),new JsonDocumentOptions{MaxDepth=16});
        JsonItems.ValidateObject(document.RootElement,typeof(LootDefinition),"loot");
        RequireWeightFields(document.RootElement);
        var definition=document.RootElement.Deserialize<LootDefinition>(Options)!;
        Validate(definition);return definition;
    }
    private static void RequireWeightFields(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Array)foreach(var child in value.EnumerateArray())RequireWeightFields(child);
        if(value.ValueKind!=JsonValueKind.Object)return;
        foreach(var property in value.EnumerateObject()) {
            if(property.Name is "entries" or "add" or "setWeight")foreach(var entry in property.Value.EnumerateArray())
                if(!entry.TryGetProperty("weight",out _))throw new InvalidDataException("EZAPI_LOOT_WEIGHT: explicit weight required for entries/add/setWeight.");
            RequireWeightFields(property.Value);
        }
    }
    public static void Validate(LootDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if(definition.Tables is null||definition.Groups is null||definition.Patches is null||
            definition.Tables.Count>64||definition.Groups.Count>64||definition.Patches.Count>128)
            throw new ArgumentException("EZAPI_LOOT_LIMIT: max 64 tables/64 groups/128 patches per file.");
        foreach(var lists in new[]{definition.Tables,definition.Groups}) {
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var list in lists) {
                if(list is null)throw new ArgumentException("EZAPI_LOOT_FIELD: null list.");
                ValidateId(list.Id);
                if(!ids.Add(list.Id))throw new ArgumentException("EZAPI_LOOT_DUPLICATE: "+list.Id);
                ValidateEntries(list.Entries,false);
            }
        }
        foreach(var patch in definition.Patches) {
            if(patch is null||(patch.Table is null)==(patch.Group is null))
                throw new ArgumentException("EZAPI_LOOT_PATCH: exactly one of table/group required.");
            ValidateId(patch.Table??patch.Group!);
            ValidateEntries(patch.Add,false);ValidateEntries(patch.SetWeight,true);
            if(patch.Remove is null||patch.Remove.Count>512||patch.Remove.Distinct(StringComparer.Ordinal).Count()!=patch.Remove.Count)
                throw new ArgumentException("EZAPI_LOOT_PATCH: max 512 unique remove IDs.");
            foreach(var id in patch.Remove)ValidateId(id);
            if(!patch.Clear&&patch.Add.Count==0&&patch.Remove.Count==0&&patch.SetWeight.Count==0)
                throw new ArgumentException("EZAPI_LOOT_PATCH: empty patch has no operation.");
        }
        if(OperationCount(definition)>4096)throw new ArgumentException("EZAPI_LOOT_LIMIT: max 4096 operations per submission.");
    }
    private static void ValidateEntries(IReadOnlyList<LootWeightDefinition> entries,bool allowZero)
    {
        if(entries is null||entries.Count>512)throw new ArgumentException("EZAPI_LOOT_LIMIT: max 512 entries per list.");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var entry in entries) {
            if(entry is null)throw new ArgumentException("EZAPI_LOOT_FIELD: null entry.");
            ValidateId(entry.Id);
            if(!ids.Add(entry.Id))throw new ArgumentException("EZAPI_LOOT_DUPLICATE: "+entry.Id);
            if(!float.IsFinite(entry.Weight)||entry.Weight<0||entry.Weight>1000000000||(!allowZero&&entry.Weight==0))
                throw new ArgumentException("EZAPI_LOOT_WEIGHT: finite weight <=1e9; add/register >0; setWeight >=0.");
        }
    }
    internal static int OperationCount(LootDefinition d)=>d.Tables.Count+d.Groups.Count+
        d.Patches.Sum(p=>(p.Clear?1:0)+p.Add.Count+p.Remove.Count+p.SetWeight.Count);
    public static void ValidateId(string id)
    {
        if(id is null||id.Length>160||!Regex.IsMatch(id,@"^[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*$"))
            throw new ArgumentException("EZAPI_LOOT_ID: use 1..160 ASCII letters/digits/underscore/hyphen and dot-separated segments.");
    }
}

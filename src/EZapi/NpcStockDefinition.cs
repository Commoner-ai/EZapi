using System.Text.Json;
using System.Text.Json.Serialization;
namespace EZapi;

/// <summary>可组合的货源分类；None不加入分类随机池。</summary>
[Flags, JsonConverter(typeof(NpcStockCategoryJsonConverter))]
public enum NpcTradeStockCategory { None=0, Material=1, Ore=2, Module=4, Machine=8, Household=16, Food=32, Medical=64 }

/// <summary>可组合供应商；Any同时允许Thief与Inventor。矿工使用独立MinerWeight。</summary>
[Flags, JsonConverter(typeof(NpcStockSupplierJsonConverter))]
public enum NpcTradeStockSupplier { Any=0, Thief=1, Inventor=2 }

/// <summary>共享C#/JSON货源定义。权重为相对值，不是百分比；默认构造可直接用于JSON。</summary>
public sealed record NpcTradeStockOptions
{
    public NpcTradeStockOptions() { }
    public NpcTradeStockOptions(NpcTradeStockCategory category,float weight=1){Category=category;Weight=weight;}
    public NpcTradeStockCategory Category {get;init;}
    public float Weight {get;init;}=1;
    public NpcTradeStockSupplier Suppliers {get;init;}=NpcTradeStockSupplier.Any;
    public bool IncludeInNightShop {get;init;}=true;
    public bool SkipWhenOwned {get;init;}
    public int MinimumDay {get;init;}
    public bool PreserveLootTableWeight {get;init;}=true;
    public float? MinerWeight {get;init;}
    public void Deconstruct(out NpcTradeStockCategory category,out float weight){category=Category;weight=Weight;}
}

/// <summary>一次抽取的纯托管条件。Day沿用StoreStation.GetDayCounter，不自行加1。</summary>
public sealed record NpcStockContext(NpcTradeStockCategory Category,NpcTradeStockSupplier Supplier,int Day,
    bool NightShop=false,bool Miner=false);
/// <summary>当前目录应用后的候选；Owned是本次玩家扫描结果，不可跨读档缓存。</summary>
public sealed record NpcStockCandidate(string OwnerId,string ItemId,NpcTradeStockOptions Options,bool Owned=false);
public sealed record NpcStockEntry(string ItemId,float Weight,string? SourceModId=null);
public sealed class NpcStockPool
{
    public double TotalWeight {get;}
    public IReadOnlyList<NpcStockEntry> Entries {get;}
    internal NpcStockPool(IReadOnlyList<NpcStockEntry> entries)
    {
        Entries=Array.AsReadOnly(entries.ToArray());
        foreach(var entry in Entries)TotalWeight+=entry.Weight;
    }
    /// <summary>传入[0,1)随机样本；不自动推进RNG。空池返回null。边界落入后一个权重区间。</summary>
    public string? Pick(double sample)
    {
        if(!double.IsFinite(sample)||sample<0||sample>=1)throw new ArgumentOutOfRangeException(nameof(sample),"EZAPI_NPC_STOCK_SAMPLE: expected [0,1).");
        if(TotalWeight==0)return null;
        var target=sample*TotalWeight;double sum=0;string? last=null;
        foreach(var entry in Entries){if(entry.Weight<=0)continue;sum+=entry.Weight;last=entry.ItemId;if(target<sum)return entry.ItemId;}
        return last; // 仅补偿浮点累加末尾舍入。
    }
}

/// <summary>纯托管候选池计算。既可供EZapi原生适配器复用，也可用于作者自建商人，不改全局掉落表。</summary>
public static class NpcStockPlanner
{
    public static void Validate(NpcTradeStockOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if((options.Category & ~(NpcTradeStockCategory)127)!=0 || (int)options.Category<0 ||
            (options.Suppliers & ~(NpcTradeStockSupplier)3)!=0 || (int)options.Suppliers<0 ||
            !float.IsFinite(options.Weight)||options.Weight<=0 || options.Weight>1000000000 ||
            options.MinerWeight is {} miner&&(!float.IsFinite(miner)||miner<0||miner>1000000000) || options.MinimumDay<0)
            throw new ArgumentException("EZAPI_NPC_STOCK_OPTIONS: valid category/supplier flags, positive finite weight, nonnegative miner weight/day required.");
    }
    /// <summary>每次复制输入，最多4096原表项/4096候选。保留原表顺序；相同ID只占一个最终区间。</summary>
    public static NpcStockPool Build(NpcStockContext context,IReadOnlyList<NpcStockEntry> baseline,IReadOnlyList<NpcStockCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(context);ArgumentNullException.ThrowIfNull(baseline);ArgumentNullException.ThrowIfNull(candidates);
        if(context.Day<0 || context.Category==NpcTradeStockCategory.None || (context.Category & ~(NpcTradeStockCategory)127)!=0 ||
            context.Supplier is not (NpcTradeStockSupplier.Any or NpcTradeStockSupplier.Thief or NpcTradeStockSupplier.Inventor))
            throw new ArgumentException("EZAPI_NPC_STOCK_CONTEXT: valid category, single supplier and nonnegative day required.");
        if(baseline.Count>4096||candidates.Count>4096)throw new ArgumentException("EZAPI_NPC_STOCK_LIMIT: max4096 baseline entries/candidates.");
        var entries=new List<NpcStockEntry>();var positions=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var row in baseline) {
            if(row is null||!Id(row.ItemId)||!float.IsFinite(row.Weight)||row.Weight<0)throw new ArgumentException("EZAPI_NPC_STOCK_ENTRY: valid ID and nonnegative finite weight required.");
            if(positions.TryGetValue(row.ItemId,out var at))entries[at]=entries[at] with{Weight=Add(entries[at].Weight,row.Weight)};
            else{positions.Add(row.ItemId,entries.Count);entries.Add(row with{});}
        }
        var seen=new HashSet<string>(StringComparer.Ordinal);
        // 先整批验证，避免调用者误把部分接受当作完整计划。
        foreach(var candidate in candidates) {
            if(candidate is null || !Id(candidate.OwnerId)||!Id(candidate.ItemId)||!candidate.ItemId.StartsWith(candidate.OwnerId+".",StringComparison.Ordinal)||!seen.Add(candidate.ItemId))
                throw new ArgumentException("EZAPI_NPC_STOCK_CANDIDATE: unique owner-prefixed item IDs required.");
            Validate(candidate.Options);
        }
        foreach(var candidate in candidates) {
            var o=candidate.Options;var at=positions.GetValueOrDefault(candidate.ItemId,-1);
            // 与此分类无关的声明不能清掉另一货源表中原有的同ID条目。
            if((o.Category & context.Category)==0)continue;
            var matches=context.Day>=o.MinimumDay && (!o.SkipWhenOwned||!candidate.Owned) &&
                (!context.NightShop||o.IncludeInNightShop) &&
                (context.Miner || o.Suppliers==NpcTradeStockSupplier.Any || context.Supplier!=NpcTradeStockSupplier.Any&&(o.Suppliers & context.Supplier)!=0);
            var weight=context.Miner ? o.MinerWeight??0 : o.Weight;
            if(!matches || weight==0) {
                // 显式注册的自身ID若已在基础表中，也必须遵守所有权/天数等限制。
                if(at>=0)entries[at]=entries[at] with{Weight=0};
                continue;
            }
            if(at>=0) {
                if(!o.PreserveLootTableWeight)entries[at]=entries[at] with{Weight=weight,SourceModId=candidate.OwnerId};
            } else {positions.Add(candidate.ItemId,entries.Count);entries.Add(new(candidate.ItemId,weight,candidate.OwnerId));}
        }
        // 原生Pick用Single累加。即使每个输入有限，总和溢出也必须在推进RNG前拒绝。
        double total=0;foreach(var entry in entries)total+=entry.Weight;
        if(total>float.MaxValue)throw new ArgumentException("EZAPI_NPC_STOCK_WEIGHT: native total weight overflow.");
        return new(entries);
    }
    private static bool Id(string? id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=160&&!id.Any(char.IsControl);
    private static float Add(float a,float b){var v=(double)a+b;if(v>float.MaxValue)throw new ArgumentException("EZAPI_NPC_STOCK_WEIGHT: duplicate base weight overflow.");return (float)v;}
}

// JSON只接收声明过的字符串名，禁止数字/数字字符串及未定义位；与Schema共用明确枚举范围。
// 自定义转换器避免JsonStringEnumConverter默认接受整数，绕开AI作者的拼写错误。
public sealed class NpcStockCategoryJsonConverter:JsonConverter<NpcTradeStockCategory>
{
    public override NpcTradeStockCategory Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        =>(NpcTradeStockCategory)NpcStockFlagJson.Read(ref reader,typeof(NpcTradeStockCategory));
    public override void Write(Utf8JsonWriter writer,NpcTradeStockCategory value,JsonSerializerOptions options)
        =>writer.WriteStringValue(NpcStockFlagJson.Write((int)value,typeof(NpcTradeStockCategory)));
}
public sealed class NpcStockSupplierJsonConverter:JsonConverter<NpcTradeStockSupplier>
{
    public override NpcTradeStockSupplier Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        =>(NpcTradeStockSupplier)NpcStockFlagJson.Read(ref reader,typeof(NpcTradeStockSupplier));
    public override void Write(Utf8JsonWriter writer,NpcTradeStockSupplier value,JsonSerializerOptions options)
        =>writer.WriteStringValue(NpcStockFlagJson.Write((int)value,typeof(NpcTradeStockSupplier)));
}
internal static class NpcStockFlagJson
{
    internal static int Read(ref Utf8JsonReader reader,Type type)
    {
        if(reader.TokenType!=JsonTokenType.String)throw new JsonException("EZAPI_NPC_STOCK_FLAGS: expected flag names, not a number.");
        var names=reader.GetString()!.Split(',');var declared=Enum.GetNames(type);int result=0;
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var part in names){var name=part.Trim();var exact=declared.FirstOrDefault(n=>string.Equals(n,name,StringComparison.OrdinalIgnoreCase));
            if(exact is null||!seen.Add(exact))throw new JsonException("EZAPI_NPC_STOCK_FLAGS: unknown or duplicate flag: "+name);
            var flag=(int)Enum.Parse(type,exact);
            if(flag==0&&names.Length!=1)throw new JsonException("EZAPI_NPC_STOCK_FLAGS: zero flag must appear alone.");
            result|=flag;
        }
        return result;
    }
    internal static string Write(int value,Type type)
    {
        var mask=type==typeof(NpcTradeStockCategory)?127:3;
        if(value<0||(value & ~mask)!=0)throw new JsonException("EZAPI_NPC_STOCK_FLAGS: undefined bits.");
        return Enum.ToObject(type,value).ToString()!;
    }
}

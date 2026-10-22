namespace EZapi;

/// <summary>与官方 LootTables JSON 的 tables/groups/patches 字段对应；省略的列表为空。</summary>
public sealed class LootDefinition
{
    public IReadOnlyList<LootListDefinition> Tables { get; init; } = Array.Empty<LootListDefinition>();
    public IReadOnlyList<LootListDefinition> Groups { get; init; } = Array.Empty<LootListDefinition>();
    public IReadOnlyList<LootPatchDefinition> Patches { get; init; } = Array.Empty<LootPatchDefinition>();
}
public sealed class LootListDefinition
{
    public string Id { get; init; } = "";
    public IReadOnlyList<LootWeightDefinition> Entries { get; init; } = Array.Empty<LootWeightDefinition>();
}
public sealed class LootWeightDefinition
{
    public string Id { get; init; } = "";
    /// <summary>相对权重，不是百分比；新建/添加 >0，setWeight 可为0。</summary>
    public float Weight { get; init; }
}
public sealed class LootPatchDefinition
{
    public string? Table { get; init; }
    public string? Group { get; init; }
    public bool Clear { get; init; }
    public IReadOnlyList<LootWeightDefinition> Add { get; init; } = Array.Empty<LootWeightDefinition>();
    public IReadOnlyList<string> Remove { get; init; } = Array.Empty<string>();
    public IReadOnlyList<LootWeightDefinition> SetWeight { get; init; } = Array.Empty<LootWeightDefinition>();
}
/// <summary>接收成功只表示声明已排队。原生 journal 提交和实际抽取需分别检查。</summary>
public sealed record LootRegistrationResult(bool Accepted,string Message,int OperationCount);
/// <summary>纯托管快照，不保留 LootEntry 或原生列表。Probability 按当前表总权重计算。</summary>
public sealed record LootEntrySnapshot(string Id,float Weight,string? SourceModId,string? ModifiedByModId,double Probability);
public sealed record LootListSnapshot(string Id,bool IsGroup,bool Found,double TotalWeight,IReadOnlyList<LootEntrySnapshot> Entries);

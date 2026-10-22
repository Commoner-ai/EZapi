namespace EZapi;

/// <summary>工坊中可展示的资源类别。这里只描述，不自动读取、扣除或发放资源。</summary>
public enum WorkshopResourceKind { Credits = 0, Item = 1 }

/// <summary>工坊分组标题。EntryCount 是作者提供的展示数量，不自动重写。</summary>
public sealed record WorkshopGroup(string ChineseTitle, string EnglishTitle, int EntryCount);

/// <summary>一项资源条件；Satisfied 由作者按当前游戏状态计算，Consume 只描述意图。</summary>
public sealed record WorkshopRequirement(WorkshopResourceKind Kind, string ResourceId, int Quantity, bool Consume, bool Satisfied)
{
    public string DisplayName { get; init; } = "";
    /// <summary>Optional bilingual resource display name; does not change the resource ID or eligibility.</summary>
    public LocalizedItemText? LocalizedDisplayName { get; init; }
}

/// <summary>一项资源奖励的展示定义；框架不会据此创建物品或增加资金。</summary>
public sealed record WorkshopReward(WorkshopResourceKind Kind, string ResourceId, int Quantity)
{
    public string DisplayName { get; init; } = "";
    /// <summary>Optional bilingual reward display name; does not deliver the reward.</summary>
    public LocalizedItemText? LocalizedDisplayName { get; init; }
}

/// <summary>工坊页中的一个节点。资源资格、支付和奖励由所属 Mod 的 Unlock 回调负责。</summary>
public sealed record WorkshopEntry(string Id, string Title, string Subtitle, string Description, string Requirement,
    string CostText, string NodeCostText, string StateText, string ActionText,
    bool Unlocked, bool PrerequisiteMet, bool ActionEnabled)
{
    /// <summary>Optional bilingual node title, resolved when capturing a view.</summary>
    public LocalizedItemText? LocalizedTitle { get; init; }
    /// <summary>Optional bilingual subtitle, resolved when capturing a view.</summary>
    public LocalizedItemText? LocalizedSubtitle { get; init; }
    /// <summary>Optional bilingual description, resolved when capturing a view.</summary>
    public LocalizedItemText? LocalizedDescription { get; init; }
    /// <summary>Optional bilingual requirement prose; does not evaluate eligibility.</summary>
    public LocalizedItemText? LocalizedRequirement { get; init; }
    /// <summary>Optional bilingual cost prose; does not pay resources.</summary>
    public LocalizedItemText? LocalizedCostText { get; init; }
    /// <summary>Optional bilingual node-cost prose.</summary>
    public LocalizedItemText? LocalizedNodeCostText { get; init; }
    /// <summary>Optional bilingual state prose; does not modify state.</summary>
    public LocalizedItemText? LocalizedStateText { get; init; }
    /// <summary>Optional bilingual action button text; does not alter action permission.</summary>
    public LocalizedItemText? LocalizedActionText { get; init; }
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
    public IReadOnlyList<WorkshopRequirement> Conditions { get; init; } = Array.Empty<WorkshopRequirement>();
    public IReadOnlyList<WorkshopReward> Rewards { get; init; } = Array.Empty<WorkshopReward>();
    public bool? Completed { get; init; }
    public IReadOnlyList<string> VisualLinks { get; init; } = Array.Empty<string>();
    public float? GraphX { get; init; }
    public float? GraphY { get; init; }
}

/// <summary>单次读取的展示快照。集合在 API 接受时会深复制为只读快照。</summary>
public sealed record WorkshopSnapshot(int Credits, bool English, IReadOnlyList<WorkshopGroup> Groups,
    IReadOnlyList<WorkshopEntry> Entries, string Message = "")
{
    /// <summary>Opt in to current-game locale on open/refresh. False retains the legacy explicit English flag.</summary>
    public bool FollowGameLanguage { get; init; }
    /// <summary>Optional bilingual page message.</summary>
    public LocalizedItemText? LocalizedMessage { get; init; }
    /// <summary>Creates a view declaration that follows the game language without a manual English flag.</summary>
    public static WorkshopSnapshot ForGameLanguage(int credits,IReadOnlyList<WorkshopGroup> groups,
        IReadOnlyList<WorkshopEntry> entries,string message="")=>new(credits,false,groups,entries,message){FollowGameLanguage=true};
}

/// <summary>已登记的工坊页标题。</summary>
public sealed record WorkshopTabHeader(string Id, string ChineseTitle, string EnglishTitle);

/// <summary>校验并冻结作者提供的工坊快照，防止 UI 持有可变列表或无界数据。</summary>
public static class WorkshopDefinitionRules
{
    private const int MaxEntries = 2048;
    private const int MaxGroups = 128;
    private const int MaxText = 8192;

    public static void Validate(string ownerId, WorkshopSnapshot snapshot) => Copy(ownerId, snapshot);

    internal static WorkshopSnapshot Copy(string ownerId, WorkshopSnapshot snapshot)
    {
        if (!PresentationRules.Owner(ownerId)) Fail("EZAPI_WORKSHOP_OWNER", "valid lowercase owner namespace required");
        if (snapshot is null) Fail("EZAPI_WORKSHOP_SNAPSHOT", "snapshot is required");
        if (snapshot!.Credits < 0) Fail("EZAPI_WORKSHOP_CREDITS", "credits must be >= 0");
        Text(snapshot.Message, MaxText, "message");
        snapshot.LocalizedMessage?.Validate(MaxText);
        if (snapshot.Groups is null || snapshot.Groups.Count > MaxGroups) Fail("EZAPI_WORKSHOP_GROUPS", "groups must contain at most 128 entries");
        if (snapshot.Entries is null || snapshot.Entries.Count > MaxEntries) Fail("EZAPI_WORKSHOP_ENTRIES", "entries must contain at most 2048 entries");

        var groups = new List<WorkshopGroup>(snapshot.Groups.Count);
        foreach (var group in snapshot.Groups)
        {
            if (group is null || group.EntryCount < 0 || group.EntryCount > MaxEntries)
                Fail("EZAPI_WORKSHOP_GROUP", "group and bounded nonnegative EntryCount required");
            Text(group!.ChineseTitle, 256, "ChineseTitle", nonempty: true);
            Text(group.EnglishTitle, 256, "EnglishTitle", nonempty: true);
            groups.Add(group with { });
        }

        var entries = new List<WorkshopEntry>(snapshot.Entries.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Entries)
        {
            if (entry is null || !PresentationRules.Owned(ownerId, entry.Id) || !ids.Add(entry.Id))
                Fail("EZAPI_WORKSHOP_ENTRY_ID", "entries need unique owner-prefixed IDs");
            Text(entry!.Title, 512, "Title", nonempty: entry.LocalizedTitle is null);
            Text(entry.Subtitle, 512, "Subtitle");
            Text(entry.Description, MaxText, "Description");
            Text(entry.Requirement, 2048, "Requirement");
            Text(entry.CostText, 512, "CostText");
            Text(entry.NodeCostText, 512, "NodeCostText");
            Text(entry.StateText, 512, "StateText");
            Text(entry.ActionText, 512, "ActionText");
            foreach(var text in new[]{entry.LocalizedTitle,entry.LocalizedSubtitle,entry.LocalizedCostText,
                entry.LocalizedNodeCostText,entry.LocalizedStateText,entry.LocalizedActionText})text?.Validate(512);
            entry.LocalizedDescription?.Validate(MaxText);entry.LocalizedRequirement?.Validate(2048);
            var dependencies = CopyKeys(entry.Dependencies, 256, "Dependencies");
            var visualLinks = CopyKeys(entry.VisualLinks, 256, "VisualLinks");
            if (entry.GraphX.HasValue && !float.IsFinite(entry.GraphX.Value) ||
                entry.GraphY.HasValue && !float.IsFinite(entry.GraphY.Value))
                Fail("EZAPI_WORKSHOP_GRAPH", "graph coordinates must be finite");
            if (entry.Conditions is null || entry.Conditions.Count > 64)
                Fail("EZAPI_WORKSHOP_CONDITIONS", "at most 64 conditions per entry");
            if (entry.Rewards is null || entry.Rewards.Count > 64)
                Fail("EZAPI_WORKSHOP_REWARDS", "at most 64 rewards per entry");
            var conditions = entry.Conditions.Select(Condition).ToArray();
            var rewards = entry.Rewards.Select(Reward).ToArray();
            entries.Add(entry with
            {
                Dependencies = Array.AsReadOnly(dependencies),
                VisualLinks = Array.AsReadOnly(visualLinks),
                Conditions = Array.AsReadOnly(conditions),
                Rewards = Array.AsReadOnly(rewards)
            });
        }
        return snapshot with{Groups=Array.AsReadOnly(groups.ToArray()),Entries=Array.AsReadOnly(entries.ToArray())};
    }

    // Caller supplies an already validated read-only snapshot. IDs, permissions,
    // quantities and dependency graphs retain exactly the captured values.
    internal static WorkshopSnapshot ResolveLocale(WorkshopSnapshot snapshot,string locale)
    {
        var effective=snapshot.FollowGameLanguage?locale:(snapshot.English?"en":"zh");
        string TextValue(LocalizedItemText? text,string literal)=>text?.ForLocale(effective)??literal;
        var entries=snapshot.Entries.Select(entry=>entry with {
            Title=TextValue(entry.LocalizedTitle,entry.Title),Subtitle=TextValue(entry.LocalizedSubtitle,entry.Subtitle),
            Description=TextValue(entry.LocalizedDescription,entry.Description),Requirement=TextValue(entry.LocalizedRequirement,entry.Requirement),
            CostText=TextValue(entry.LocalizedCostText,entry.CostText),NodeCostText=TextValue(entry.LocalizedNodeCostText,entry.NodeCostText),
            StateText=TextValue(entry.LocalizedStateText,entry.StateText),ActionText=TextValue(entry.LocalizedActionText,entry.ActionText),
            Conditions=Array.AsReadOnly(entry.Conditions.Select(value=>value with{DisplayName=TextValue(value.LocalizedDisplayName,value.DisplayName)}).ToArray()),
            Rewards=Array.AsReadOnly(entry.Rewards.Select(value=>value with{DisplayName=TextValue(value.LocalizedDisplayName,value.DisplayName)}).ToArray())
        }).ToArray();
        return snapshot with{English=!effective.StartsWith("zh",StringComparison.OrdinalIgnoreCase),
            Entries=Array.AsReadOnly(entries),Message=TextValue(snapshot.LocalizedMessage,snapshot.Message)};
    }

    private static WorkshopRequirement Condition(WorkshopRequirement? value)
    {
        if (value is null || !Enum.IsDefined(typeof(WorkshopResourceKind), value.Kind) || value.Quantity < 0 || value.Quantity > 1_000_000_000)
            Fail("EZAPI_WORKSHOP_CONDITION", "valid kind and quantity 0..1000000000 required");
        ResourceId(value!.Kind, value.ResourceId);
        Text(value.DisplayName, 256, "condition DisplayName");
        value.LocalizedDisplayName?.Validate(256);
        return value with { };
    }

    private static WorkshopReward Reward(WorkshopReward? value)
    {
        if (value is null || !Enum.IsDefined(typeof(WorkshopResourceKind), value.Kind) || value.Quantity < 0 || value.Quantity > 1_000_000_000)
            Fail("EZAPI_WORKSHOP_REWARD", "valid kind and quantity 0..1000000000 required");
        ResourceId(value!.Kind, value.ResourceId);
        Text(value.DisplayName, 256, "reward DisplayName");
        value.LocalizedDisplayName?.Validate(256);
        return value with { };
    }

    private static void ResourceId(WorkshopResourceKind kind, string resourceId)
    {
        if (resourceId is null || resourceId.Length > 160 || resourceId.Any(char.IsControl))
            Fail("EZAPI_WORKSHOP_RESOURCE_ID", "resource ID must be bounded and readable");
        if (resourceId.Length == 0 && kind == WorkshopResourceKind.Credits) return;
        if (!KeyRegex.IsMatch(resourceId))
            Fail("EZAPI_WORKSHOP_RESOURCE_ID", "resource ID must be a readable native or namespaced key");
    }

    private static string[] CopyKeys(IReadOnlyList<string>? values, int max, string field)
    {
        if (values is null || values.Count > max) Fail("EZAPI_WORKSHOP_" + field.ToUpperInvariant(), field + " must be a bounded list");
        var result = new string[values!.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (value is null || value.Length is < 1 or > 160 || value.Any(char.IsControl))
                Fail("EZAPI_WORKSHOP_" + field.ToUpperInvariant(), field + " entries must be readable IDs");
            result[i] = value;
        }
        return result;
    }

    private static void Text(string value, int max, string field, bool nonempty = false)
    {
        if (value is null || value.Length > max || value.IndexOf('\0') >= 0 ||
            (nonempty && string.IsNullOrWhiteSpace(value)))
            Fail("EZAPI_WORKSHOP_TEXT", field + " must be bounded text" + (nonempty ? " and nonempty" : ""));
    }

    private static readonly System.Text.RegularExpressions.Regex KeyRegex = new(
        @"\A[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*\z",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code, string message) => throw new ArgumentException(code + ": " + message);
}

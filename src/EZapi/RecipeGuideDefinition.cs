using System.Text.Json;
using System.Text.RegularExpressions;
namespace EZapi;

/// <summary>配方图鉴的一个投入/产出。Amount为显示数量，Quantity可显示ml/整堆等语义；不执行加工。</summary>
public sealed record RecipeGuideFigure(string ItemId, long Amount, string? Name=null, string? Requirement=null, string? Quantity=null)
{
    public LocalizedItemText? LocalizedName {get;init;}
    public LocalizedItemText? LocalizedRequirement {get;init;}
    public LocalizedItemText? LocalizedQuantity {get;init;}
}
/// <summary>固定文本配方说明；RecipeId归owner命名空间，MachineId可以是原版ID。与真正可执行配方独立。</summary>
public sealed record RecipeGuideEntry(string OwnerId, string RecipeId, string MachineId, string MachineName,
    string CategoryId, IReadOnlyList<RecipeGuideFigure> Inputs, RecipeGuideFigure Output, string? Note=null)
{
    public LocalizedItemText? LocalizedMachineName {get;init;}
    public LocalizedItemText? LocalizedNote {get;init;}
}
public sealed record RecipeGuideSnapshot(long Revision, IReadOnlyList<RecipeGuideEntry> Recipes);

/// <summary>图鉴规则与JSON使用同一实现，数量和文本有限制，防止错误内容拖慢游戏界面。</summary>
public static class RecipeGuideRules
{
    /// <summary>纯托管语言视图；不改目录或原文。zh*取中文，其它语言取英文，缺译一侧回退另一侧，再用原固定文本。</summary>
    public static RecipeGuideEntry ResolveLocale(RecipeGuideEntry entry,string? locale)
    {
        ArgumentNullException.ThrowIfNull(entry);
        RecipeGuideFigure Figure(RecipeGuideFigure f)=>f with{Name=f.LocalizedName?.ForLocale(locale)??f.Name,
            Requirement=f.LocalizedRequirement?.ForLocale(locale)??f.Requirement,Quantity=f.LocalizedQuantity?.ForLocale(locale)??f.Quantity};
        return entry with {MachineName=entry.LocalizedMachineName?.ForLocale(locale)??entry.MachineName,Note=entry.LocalizedNote?.ForLocale(locale)??entry.Note,
            Inputs=Array.AsReadOnly(entry.Inputs.Select(Figure).ToArray()),Output=Figure(entry.Output)};
    }
    public static void Validate(string ownerId,IReadOnlyList<RecipeGuideEntry> recipes)=>Copy(ownerId,recipes);
    internal static IReadOnlyList<RecipeGuideEntry> Copy(string ownerId,IReadOnlyList<RecipeGuideEntry> recipes)
    {
        if(ownerId is null || ownerId.Length>160 || !Regex.IsMatch(ownerId,@"^[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z"))Fail("lowercase owner ID<=160 required");
        ArgumentNullException.ThrowIfNull(recipes);
        if(recipes.Count>512)Fail("max512 recipes per owner");
        var result=new List<RecipeGuideEntry>();var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var r in recipes) {
            if(r is null)Fail("null recipe");
            if(r!.OwnerId!=ownerId || !Key(r.RecipeId) || !r.RecipeId.StartsWith(ownerId+".",StringComparison.Ordinal) || !ids.Add(r.RecipeId))Fail("unique owner-prefixed recipe IDs required");
            if(!Key(r.MachineId)||!Key(r.CategoryId))Fail("valid machine/category keys required");
            Text(r.MachineName,200,false);Text(r.Note,4000,true);
            r.LocalizedMachineName?.Validate(200);r.LocalizedNote?.Validate(4000);
            if(r.Inputs is null || r.Inputs.Count>64)Fail("max64 input figures required");
            foreach(var f in r.Inputs!)Figure(f);Figure(r.Output);
            result.Add(r with {Inputs=Array.AsReadOnly(r.Inputs!.Select(f=>f with {}).ToArray()),Output=r.Output with {}});
        }
        return result.AsReadOnly();
    }
    private static bool Key(string? id)=>id is not null && id.Length<=160 && Regex.IsMatch(id,@"^[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*\z");
    private static void Figure(RecipeGuideFigure? f)
    {
        if(f is null || !Key(f.ItemId) || f.Amount<1 || f.Amount>1000000000)Fail("figure requires item ID and amount1..1000000000");
        Text(f!.Name,200,true);Text(f.Requirement,1000,true);Text(f.Quantity,200,true);
        f.LocalizedName?.Validate(200);f.LocalizedRequirement?.Validate(1000);f.LocalizedQuantity?.Validate(200);
    }
    private static void Text(string? text,int max,bool optional)
    {
        if(text is null&&optional)return;
        if(string.IsNullOrWhiteSpace(text)||text.Length>max||text.Any(c=>char.IsControl(c)&&c is not ('\n' or '\r' or '\t')))Fail("readable bounded text required");
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string error)=>throw new ArgumentException("EZAPI_GUIDE: "+error);
}

/// <summary>独立图鉴JSON数组；不会作为items.json或可执行配方导入。owner由调用者/内容包提供。</summary>
public static class JsonRecipeGuide
{
    public static IReadOnlyList<RecipeGuideEntry> Parse(string ownerId,Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=json.Read(buffer,0,buffer.Length))>0) {
            if(bytes.Length+count>2*1024*1024)throw new InvalidDataException("EZAPI_GUIDE_LIMIT: max2MiB.");bytes.Write(buffer,0,count);
        }
        using var doc=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions{MaxDepth=8});
        if(doc.RootElement.ValueKind!=JsonValueKind.Array || doc.RootElement.GetArrayLength()>512)throw new InvalidDataException("EZAPI_GUIDE_JSON: array of <=512 entries required.");
        foreach(var row in doc.RootElement.EnumerateArray())JsonItems.ValidateObject(row,typeof(RecipeGuideEntry),"recipeGuide");
        var entries=JsonSerializer.Deserialize<RecipeGuideEntry[]>(doc.RootElement.GetRawText(),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase})!;
        return RecipeGuideRules.Copy(ownerId,entries);
    }
}

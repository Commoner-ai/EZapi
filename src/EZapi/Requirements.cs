using System.Text.Json;
namespace EZapi;

/// <summary>可共享C#/JSON的只读条件树；all/any用Children，not只接受一个子项，叶节点用TargetId或Amount。</summary>
public sealed class RequirementDefinition
{
    public string Kind {get;init;}="all";
    public string? TargetId {get;init;}
    public long? Amount {get;init;}
    public LocalizedItemText? Message {get;init;}
    public IReadOnlyList<RequirementDefinition> Children {get;init;}=Array.Empty<RequirementDefinition>();
}
public enum RequirementStatus { Satisfied, Unsatisfied, Unavailable }
/// <summary>Unknown不可作为false取反后放行。Reason是开发诊断；显示文本来自定义的双语Message。</summary>
public sealed record RequirementFact(RequirementStatus Status,string? Reason=null);
/// <summary>同步只读事实提供者。可用在离线内容工具、测试或作者自己的受控状态上；不能在Read内扣款/发物。</summary>
public interface IRequirementFacts { RequirementFact Read(string kind,string? targetId,long? amount); }
public sealed record RequirementEvaluation(RequirementStatus Status,string Kind,string? Message,string? Reason,
    IReadOnlyList<RequirementEvaluation> Children,GameRunIdentity? Run=null)
{
    public bool Satisfied=>Status==RequirementStatus.Satisfied;
}

/// <summary>纯托管条件校验/解析/组合；验证整棵树后才读事实，最多128节点/8层/32子项。</summary>
public static class Requirements
{
    private static readonly HashSet<string> TargetKinds=new(StringComparer.Ordinal){"upgrade-unlocked","upgrade-ready","service-unlocked","service-introduced","service-active","service-off-cooldown"};
    private static readonly HashSet<string> AmountKinds=new(StringComparer.Ordinal){"day-at-least","day-at-most","credits-at-least"};
    public static RequirementDefinition Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);using var bytes=new MemoryStream();var buffer=new byte[4096];int size;
        while((size=json.Read(buffer,0,buffer.Length))>0){if(bytes.Length+size>65536)throw new InvalidDataException("EZAPI_REQUIREMENT_LIMIT: max64KiB JSON.");bytes.Write(buffer,0,size);}
        using var document=JsonDocument.Parse(bytes.ToArray(),new JsonDocumentOptions{MaxDepth=32});
        var options=new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
        void Shape(JsonElement node) {
            if(node.ValueKind!=JsonValueKind.Object)throw Invalid("object required");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var field in node.EnumerateObject()) {
                if(!seen.Add(field.Name)||field.Name is not ("kind" or "targetId" or "amount" or "message" or "children")||field.Value.ValueKind==JsonValueKind.Null)throw Invalid("unknown, duplicate or null field: "+field.Name);
                if(field.Name=="children") {
                    if(field.Value.ValueKind!=JsonValueKind.Array)throw Invalid("children array required");
                    foreach(var child in field.Value.EnumerateArray())Shape(child);
                }
                if(field.Name=="message") {
                    if(field.Value.ValueKind!=JsonValueKind.Object)throw Invalid("bilingual message object required");
                    var names=new HashSet<string>(StringComparer.Ordinal);
                    foreach(var text in field.Value.EnumerateObject())if(!names.Add(text.Name)||text.Name is not ("chinese" or "english")||text.Value.ValueKind!=JsonValueKind.String)throw Invalid("invalid message field");
                }
            }
        }
        Shape(document.RootElement);return Copy(JsonSerializer.Deserialize<RequirementDefinition>(document.RootElement.GetRawText(),options)!);
    }
    /// <summary>同时校验并深复制；不保留作者可变Children集合。</summary>
    public static RequirementDefinition Copy(RequirementDefinition definition)
    {
        int count=0;var path=new HashSet<RequirementDefinition>(ReferenceEqualityComparer.Instance);
        RequirementDefinition Visit(RequirementDefinition node,int depth) {
            if(node is null||depth>8||++count>128||!path.Add(node))throw Invalid("null, cycle or tree limits128/8 exceeded");
            try {
                if(node.Children is null||node.Children.Count>32)throw Invalid("max32 children");
                node.Message?.Validate(1000);var group=node.Kind is "all" or "any" or "not";
                if(group) {
                    if(node.TargetId is not null||node.Amount.HasValue||node.Kind=="not"&&node.Children.Count!=1||node.Kind=="any"&&node.Children.Count==0)throw Invalid("invalid group arguments");
                }else {
                    if(node.Children.Count!=0)throw Invalid("leaf cannot have children");
                    if(TargetKinds.Contains(node.Kind??"")) {if(!ReadableIds.IsValid(node.TargetId)||node.Amount.HasValue)throw Invalid("target leaf requires stable targetId only");}
                    else if(AmountKinds.Contains(node.Kind??"")) {if(node.TargetId is not null||node.Amount is null or <0 or >int.MaxValue)throw Invalid("amount leaf requires0..Int32.MaxValue only");}
                    else throw Invalid("unknown kind: "+node.Kind);
                }
                return new(){Kind=node.Kind!,TargetId=node.TargetId,Amount=node.Amount,Message=node.Message is null?null:node.Message with {},Children=Array.AsReadOnly(node.Children.Select(child=>Visit(child,depth+1)).ToArray())};
            }finally{path.Remove(node);}
        }
        return Visit(definition,1);
    }
    public static RequirementEvaluation Evaluate(RequirementDefinition definition,IRequirementFacts facts,string? locale="en")
    {
        ArgumentNullException.ThrowIfNull(facts);var copy=Copy(definition);
        var cache=new Dictionary<(string Kind,string? Id,long? Amount),RequirementFact>();
        RequirementEvaluation Visit(RequirementDefinition node) {
            var children=node.Children.Select(Visit).ToArray();RequirementStatus status;string? reason=null;
            if(node.Kind=="all")status=children.Any(c=>c.Status==RequirementStatus.Unsatisfied)?RequirementStatus.Unsatisfied:children.Any(c=>c.Status==RequirementStatus.Unavailable)?RequirementStatus.Unavailable:RequirementStatus.Satisfied;
            else if(node.Kind=="any")status=children.Any(c=>c.Status==RequirementStatus.Satisfied)?RequirementStatus.Satisfied:children.Any(c=>c.Status==RequirementStatus.Unavailable)?RequirementStatus.Unavailable:RequirementStatus.Unsatisfied;
            else if(node.Kind=="not")status=children[0].Status==RequirementStatus.Unavailable?RequirementStatus.Unavailable:children[0].Satisfied?RequirementStatus.Unsatisfied:RequirementStatus.Satisfied;
            else {
                var key=(node.Kind,node.TargetId,node.Amount);
                if(!cache.TryGetValue(key,out var fact)) {
                    try{fact=facts.Read(node.Kind,node.TargetId,node.Amount);if(fact is null||!Enum.IsDefined(typeof(RequirementStatus),fact.Status))throw new InvalidOperationException("invalid fact result");}
                    catch(Exception error){fact=new(RequirementStatus.Unavailable,"EZAPI_REQUIREMENT_FACT: "+error.GetType().Name);}
                    cache[key]=fact;
                }
                status=fact.Status;reason=fact.Reason;
            }
            return new(status,node.Kind,node.Message?.ForLocale(locale),reason,Array.AsReadOnly(children));
        }
        return Visit(copy);
    }
    private static ArgumentException Invalid(string reason)=>new("EZAPI_REQUIREMENT_DEFINITION: "+reason);
}

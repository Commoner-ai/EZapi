using System.Text.RegularExpressions;
namespace EZapi.Registration;

/// <summary>通用声明种类；只描述内容身份，不替代物品/效果等实际原生注册入口。</summary>
public enum DefinitionKind { Item, Module, Node, Effect, Start, Perk, Status }
/// <summary>仅表示声明依赖是否满足，不表示原生目录应用、交付或保存成功。</summary>
public enum RegistrationStatus { DependenciesResolved, Waiting }
public sealed record RegistrationDependency(string OwnerId,string MinimumVersion);
public sealed record RegistrationDefinition(DefinitionKind Kind,string Id,string Signature,IReadOnlyList<string> RequiredIds);
public sealed record RegistrationView(string OwnerId,string Version,RegistrationStatus Status,string Reason,IReadOnlyList<string> DefinitionIds);

/// <summary>
/// 在初始化回调中收集整批通用元数据。Define不执行作者工厂、不创建原生对象。
/// 提交或回调结束后封存；后续调用Require/Define会明确拒绝。
/// </summary>
public sealed class RegistrationBatch
{
    private readonly List<RegistrationDependency> _dependencies=new();
    private readonly List<RegistrationDefinition> _definitions=new();
    private bool _sealed;
    private readonly int _thread=Environment.CurrentManagedThreadId;
    public string OwnerId {get;}
    public string Version {get;}
    public RegistrationBatch(string ownerId,string version)
    {
        RegistrationRules.Owner(ownerId);RegistrationRules.Version(version);
        OwnerId=ownerId;Version=version;
    }
    /// <summary>要求另一已提交且依赖已满足的作者版本。依赖缺失/过旧时等待，后续提交可自动解除。</summary>
    public RegistrationBatch Require(string ownerId,string minimumVersion="0.0.0")
    {
        Mutable();RegistrationRules.Owner(ownerId);RegistrationRules.Version(minimumVersion);
        if(ownerId==OwnerId||_dependencies.Any(d=>d.OwnerId==ownerId))throw new ArgumentException("EZAPI_REGISTRATION_DEPENDENCY: duplicate or self owner dependency.");
        if(_dependencies.Count>=64)throw new ArgumentException("EZAPI_REGISTRATION_LIMIT: max64 owner dependencies per batch.");
        _dependencies.Add(new(ownerId,minimumVersion));return this;
    }
    /// <summary>localId自动加owner前缀，也接受已带本owner前缀的完整ID。requiredIds需为完整ID。</summary>
    public RegistrationBatch Define(DefinitionKind kind,string localId,string signature,params string[] requiredIds)
    {
        Mutable();if(!Enum.IsDefined(typeof(DefinitionKind),kind))throw new ArgumentException("EZAPI_REGISTRATION_KIND: undefined definition kind.");
        RegistrationRules.Key(localId,"localId");
        var id=localId.StartsWith(OwnerId+".",StringComparison.Ordinal)?localId:OwnerId+"."+localId;
        RegistrationRules.Key(id,"definition ID");
        if(string.IsNullOrWhiteSpace(signature)||signature.Length>4096||signature.Any(char.IsControl))throw new ArgumentException("EZAPI_REGISTRATION_SIGNATURE: nonblank printable signature <=4096 required.");
        ArgumentNullException.ThrowIfNull(requiredIds);
        if(requiredIds.Length>64)throw new ArgumentException("EZAPI_REGISTRATION_LIMIT: max64 required IDs per definition.");
        var unique=new HashSet<string>(StringComparer.Ordinal);
        foreach(var required in requiredIds){RegistrationRules.Key(required,"required ID");
            if(!required.Contains('.')||required==id||!unique.Add(required))throw new ArgumentException("EZAPI_REGISTRATION_REFERENCE: full unique nonself required IDs needed.");}
        if(_definitions.Any(d=>d.Id==id))throw new ArgumentException("EZAPI_REGISTRATION_DEFINITION: duplicate ID within batch: "+id);
        if(_definitions.Count>=500)throw new ArgumentException("EZAPI_REGISTRATION_LIMIT: max500 definitions per batch.");
        _definitions.Add(new(kind,id,signature,Array.AsReadOnly(requiredIds.ToArray())));return this;
    }
    internal void Seal()=>_sealed=true;
    internal RegistrationPlan Capture()
    {
        RequireThread();
        Seal();
        return new(OwnerId,Version,Array.AsReadOnly(_dependencies.OrderBy(d=>d.OwnerId,StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(_definitions.OrderBy(d=>d.Id,StringComparer.Ordinal).Select(d=>d with{RequiredIds=Array.AsReadOnly(d.RequiredIds.OrderBy(id=>id,StringComparer.Ordinal).ToArray())}).ToArray()));
    }
    private void Mutable(){RequireThread();if(_sealed)throw new InvalidOperationException("EZAPI_REGISTRATION_SEALED: batch callback/submission has ended.");}
    private void RequireThread(){if(Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("EZAPI_REGISTRATION_THREAD: use batch on its constructing thread.");}
}

internal sealed record RegistrationPlan(string OwnerId,string Version,IReadOnlyList<RegistrationDependency> Dependencies,IReadOnlyList<RegistrationDefinition> Definitions);
internal static class RegistrationRules
{
    internal static void Owner(string value)
    {if(value is null||value.Length>160||!Regex.IsMatch(value,@"\A[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z"))throw new ArgumentException("EZAPI_REGISTRATION_OWNER: lowercase owner ID required.");}
    internal static void Key(string value,string field)
    {if(value is null||value.Length>160||!Regex.IsMatch(value,@"\A[a-z0-9_-]+(?:\.[a-z0-9_-]+)*\z"))throw new ArgumentException("EZAPI_REGISTRATION_ID: valid lowercase "+field+" <=160 required.");}
    internal static Version Version(string value)
    {
        if(value is null||value.Length>32||!Regex.IsMatch(value,@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\z")||
            !System.Version.TryParse(value,out var parsed))throw new ArgumentException("EZAPI_REGISTRATION_VERSION: numeric major.minor.patch required.");
        return parsed;
    }
}

/// <summary>独立托管目录；批次提交前整体验证，依赖解析不执行任何作者代码。</summary>
internal sealed class RegistrationCatalog
{
    private readonly Dictionary<string,RegistrationPlan> _plans=new(StringComparer.Ordinal);
    private readonly Dictionary<string,RegistrationView> _views=new(StringComparer.Ordinal);
    internal SubmitResult Submit(RegistrationBatch batch)
    {
        var plan=batch.Capture();
        if(_plans.TryGetValue(plan.OwnerId,out var existing))return Same(existing,plan)
            ?new(SubmitStatus.AlreadyPresent,""):new(SubmitStatus.Conflict,"EZAPI_REGISTRATION_CONFLICT: owner already submitted different version or declarations.");
        if(_plans.Count>=256||_plans.Values.Sum(p=>p.Definitions.Count)+plan.Definitions.Count>8192)
            return new(SubmitStatus.Invalid,"EZAPI_REGISTRATION_LIMIT: max256 owners /8192 definitions in catalog.");
        var occupied=_plans.Values.SelectMany(p=>p.Definitions).Select(d=>d.Id).ToHashSet(StringComparer.Ordinal);
        var conflict=plan.Definitions.FirstOrDefault(d=>occupied.Contains(d.Id));
        if(conflict is not null)return new(SubmitStatus.Conflict,"EZAPI_REGISTRATION_CONFLICT: definition ID already belongs to another batch: "+conflict.Id);
        // Resolve只读取候选目录。计算完成才替换两个索引，失败不留下半批状态。
        var prospective=_plans.Values.Append(plan).ToArray();var views=Resolve(prospective);
        _plans.Add(plan.OwnerId,plan);_views.Clear();foreach(var view in views)_views.Add(view.OwnerId,view);
        return new(SubmitStatus.Accepted,"");
    }
    internal IReadOnlyList<RegistrationView> Snapshot()=>Array.AsReadOnly(_views.Values.OrderBy(v=>v.OwnerId,StringComparer.Ordinal).ToArray());
    internal bool IsResolved(string owner)=>_views.TryGetValue(owner,out var view)&&view.Status==RegistrationStatus.DependenciesResolved;
    internal void Clear(){_plans.Clear();_views.Clear();}
    private static bool Same(RegistrationPlan a,RegistrationPlan b)=>a.OwnerId==b.OwnerId&&a.Version==b.Version&&
        a.Dependencies.SequenceEqual(b.Dependencies)&&a.Definitions.Count==b.Definitions.Count&&a.Definitions.Zip(b.Definitions)
            .All(pair=>pair.First.Kind==pair.Second.Kind&&pair.First.Id==pair.Second.Id&&pair.First.Signature==pair.Second.Signature&&pair.First.RequiredIds.SequenceEqual(pair.Second.RequiredIds));
    private static IReadOnlyList<RegistrationView> Resolve(IReadOnlyList<RegistrationPlan> plans)
    {
        // 最多8448个图节点、约106万条边；迭代工作队列，不递归，不受作者依赖链栈深影响。
        // Owner依赖自己的全部定义；定义依赖requiredId及其所在批次的Require。
        var nodes=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        var blocked=new Dictionary<string,string>(StringComparer.Ordinal);
        var owners=plans.ToDictionary(p=>p.OwnerId,StringComparer.Ordinal);
        var definitions=plans.SelectMany(p=>p.Definitions.Select(d=>(p.OwnerId,Definition:d))).ToDictionary(x=>x.Definition.Id,StringComparer.Ordinal);
        foreach(var plan in plans){nodes.Add("o:"+plan.OwnerId,new(StringComparer.Ordinal));foreach(var d in plan.Definitions)nodes.Add("d:"+d.Id,new(StringComparer.Ordinal));}
        foreach(var plan in plans){
            var ownerKey="o:"+plan.OwnerId;var targets=plan.Definitions.Select(d=>"d:"+d.Id).Prepend(ownerKey).ToArray();
            foreach(var dependency in plan.Dependencies){
                string? reason=!owners.TryGetValue(dependency.OwnerId,out var provider)?"missing owner "+dependency.OwnerId:
                    RegistrationRules.Version(provider.Version)<RegistrationRules.Version(dependency.MinimumVersion)?"owner "+dependency.OwnerId+" requires >="+dependency.MinimumVersion:null;
                foreach(var target in targets)if(reason is not null)blocked.TryAdd(target,reason);else nodes[target].Add("o:"+dependency.OwnerId);
            }
            foreach(var d in plan.Definitions){
                var key="d:"+d.Id;nodes[ownerKey].Add(key);
                foreach(var required in d.RequiredIds){
                    if(!definitions.TryGetValue(required,out var provider)){blocked.TryAdd(key,"missing definition "+required);continue;}
                    nodes[key].Add(provider.OwnerId==plan.OwnerId?"d:"+required:"o:"+provider.OwnerId);
                }
            }
        }
        var waiting=nodes.ToDictionary(p=>p.Key,p=>p.Value.Count,StringComparer.Ordinal);
        var dependants=nodes.Keys.ToDictionary(k=>k,_=>new List<string>(),StringComparer.Ordinal);
        foreach(var node in nodes)foreach(var prerequisite in node.Value)dependants[prerequisite].Add(node.Key);
        var ready=new Queue<string>(nodes.Keys.Where(k=>waiting[k]==0&&!blocked.ContainsKey(k)));var resolved=new HashSet<string>(StringComparer.Ordinal);
        while(ready.Count>0){var key=ready.Dequeue();if(!resolved.Add(key))continue;foreach(var dependant in dependants[key])if(--waiting[dependant]==0&&!blocked.ContainsKey(dependant))ready.Enqueue(dependant);}
        return plans.Select(p=>{
            var key="o:"+p.OwnerId;var ok=resolved.Contains(key);string reason="";
            if(!ok){
                if(!blocked.TryGetValue(key,out reason!)){
                    var blockedDefinition=p.Definitions.FirstOrDefault(d=>blocked.ContainsKey("d:"+d.Id));
                    reason=blockedDefinition is not null?blocked["d:"+blockedDefinition.Id]:"dependency is unresolved or cyclic";
                }
                reason="EZAPI_REGISTRATION_WAITING: "+reason;
            }
            return new RegistrationView(p.OwnerId,p.Version,ok?RegistrationStatus.DependenciesResolved:RegistrationStatus.Waiting,reason,
                Array.AsReadOnly(p.Definitions.Select(d=>d.Id).ToArray()));
        }).ToArray();
    }
}

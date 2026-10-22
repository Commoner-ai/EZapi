using System.Text;
using EZapi.Registration;
namespace EZapi;

/// <summary>初始化期元数据和游离原生物品测试夹具；不移动玩家物品或请求游戏存档。</summary>
internal static class DebugRegistration
{
    private const string Prefix="ezapi.debug.registration";
    private static readonly List<(string Name,bool Passed)> InitializationChecks=new();
    private static IReadOnlyList<RegistrationView>? _beforeProvider;
    internal static object? LastReport{get;private set;}
    internal static void Clear(){InitializationChecks.Clear();_beforeProvider=null;LastReport=null;}
    internal static void Register()
    {
        Clear();
        void Check(bool ok,string name)=>InitializationChecks.Add((name,ok));
        var addon=RegistrationApi.Register(Prefix+".addon","1.0.0",b=>b.Require(Prefix+".base","1.2.0")
            .Define(DefinitionKind.Module,"addon","v1",Prefix+".base.item"));
        Check(addon.Status==SubmitStatus.Accepted,"initialization accepts dependent batch before provider");
        Check(!RegistrationApi.IsResolved(Prefix+".addon"),"missing provider stays Waiting");
        _beforeProvider=RegistrationApi.Snapshot();
        void DefineBase(RegistrationBatch b){foreach(var kind in Enum.GetValues<DefinitionKind>())b.Define(kind,kind.ToString().ToLowerInvariant(),"v1");}
        Check(RegistrationApi.Register(Prefix+".base","1.2.0",DefineBase).Accepted,"all seven definition kinds accepted during initialization");
        Check(RegistrationApi.IsResolved(Prefix+".addon"),"provider submission resolves earlier dependent batch");
        Check(RegistrationApi.Register(Prefix+".base","1.2.0",DefineBase).Status==SubmitStatus.AlreadyPresent,"identical submission is idempotent");
        Check(RegistrationApi.Register(Prefix+".base","1.3.0",DefineBase).Status==SubmitStatus.Conflict,"changed existing version refuses whole submission");
        RegistrationBatch? retained=null;
        var failure=RegistrationApi.Register(Prefix+".failed","1.0.0",b=>{retained=b;b.Define(DefinitionKind.Item,"discarded","v1");throw new InvalidOperationException("intentional declaration failure");});
        Check(failure.Status==SubmitStatus.Invalid&&!RegistrationApi.Snapshot().Any(v=>v.OwnerId==Prefix+".failed"),"callback exception commits no metadata");
        bool sealedBatch=false;
        try{retained!.Define(DefinitionKind.Item,"late","v1");}catch(InvalidOperationException ex){sealedBatch=ex.Message.Contains("SEALED");}
        Check(sealedBatch,"failed callback builder is sealed");
        using var json=new MemoryStream(Encoding.UTF8.GetBytes("{\"ownerId\":\""+Prefix+".json\",\"version\":\"1.0.0\",\"dependencies\":[{\"ownerId\":\""+Prefix+".base\",\"minimumVersion\":\"1.2.0\"}],\"definitions\":[{\"kind\":\"Status\",\"localId\":\"ready\",\"signature\":\"v1\"}]}"));
        Check(RegistrationApi.RegisterJson(json).Accepted&&RegistrationApi.IsResolved(Prefix+".json"),"strict JSON uses same runtime registration directory");
    }
    internal static string Run()
    {
        var checks=InitializationChecks.Select(c=>(object)new{name=c.Name,passed=c.Passed}).ToList();
        int passed=InitializationChecks.Count(c=>c.Passed),failed=InitializationChecks.Count(c=>!c.Passed);
        void Check(bool ok,string name){checks.Add(new{name,passed=ok});if(ok)passed++;else failed++;}
        try{
            Check(InitializationChecks.Count==9,"initialization fixture executed all expected assertions");
            var snapshots=RegistrationApi.Snapshot();
            var own=snapshots.Where(v=>v.OwnerId.StartsWith(Prefix+".",StringComparison.Ordinal)).ToArray();
            Check(own.Length==3&&own.All(v=>v.Status==RegistrationStatus.DependenciesResolved),"all three committed fixture batches remain resolved after loading");
            Check(own.Single(v=>v.OwnerId==Prefix+".base").DefinitionIds.Count==7,"all seven metadata definitions survive directory application");
            Check(_beforeProvider!.Single(v=>v.OwnerId==Prefix+".addon").Status==RegistrationStatus.Waiting,"older immutable snapshot retains Waiting state");
            bool invoked=false;var late=RegistrationApi.Register(Prefix+".late","1.0.0",_=>invoked=true);
            Check(late.Status==SubmitStatus.Invalid&&!invoked,"late declaration refused before invoking author callback");
            Check(RegistrationApi.Snapshot().Count==snapshots.Count,"refused late submission leaves directory unchanged");
            bool readOnly=false;try{((IList<RegistrationView>)snapshots).Clear();}catch(NotSupportedException){readOnly=true;}
            Check(readOnly,"snapshot collection refuses writes");
            bool idsReadOnly=false;try{((IList<string>)own[0].DefinitionIds).Clear();}catch(NotSupportedException){idsReadOnly=true;}
            Check(idsReadOnly,"definition ID collection refuses writes");
            DebugPublicHelpers.Run(Check);
            DebugProgression.Run(Check);
            DebugItemData.Run(Check);
        }catch(Exception ex){Check(false,"runtime execution: "+ex);}
        LastReport=new{at=DateTimeOffset.Now,passed,failed,checks,scope="initialization metadata plus detached native item/tag/freeze/graph fixtures; no player delivery, gameplay purchase, disk save or save-reload claim"};
        EZapiBootstrap.WriteDiagnostics();return $"通用注册：{passed}通过，{failed}失败。";
    }
}

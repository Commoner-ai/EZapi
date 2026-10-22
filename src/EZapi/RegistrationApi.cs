using EZapi.Registration;
namespace EZapi;

/// <summary>初始化期通用声明/依赖目录。依赖满足只表示元数据就绪，实际内容使用各域API注册。</summary>
public static class RegistrationApi
{
    private static readonly RegistrationCatalog Catalog=new();
    private static bool _started,_declaring;
    private static int _thread;
    private static long _generation;
    public static SubmitResult Register(string ownerId,string version,Action<RegistrationBatch> declare)
    {
        var error=Error();if(error is not null)return Invalid(error);
        RegistrationBatch? batch=null;_declaring=true;var generation=_generation;
        try{
            ArgumentNullException.ThrowIfNull(declare);batch=new(ownerId,version);declare(batch);
            error=Error(ignoreDeclaring:true);if(error is not null)return Invalid(error);
            if(generation!=_generation)return Invalid("EZAPI_REGISTRATION_STALE: framework restarted during declaration.");
            return Catalog.Submit(batch);
        }catch(Exception ex){return Invalid("EZAPI_REGISTRATION_DECLARE: "+ex.Message);}
        finally{batch?.Seal();_declaring=false;}
    }
    /// <summary>提交自行构建或JsonRegistration读取的批次；提交后该批次封存。</summary>
    public static SubmitResult Register(RegistrationBatch batch)
    {
        var error=Error();if(error is not null)return Invalid(error);
        if(batch is null)return Invalid("EZAPI_REGISTRATION_BATCH: nonnull batch required.");
        try{return Catalog.Submit(batch);}catch(Exception ex){return Invalid("EZAPI_REGISTRATION_DECLARE: "+ex.Message);}
    }
    public static SubmitResult RegisterJson(Stream json)
    {
        var error=Error();if(error is not null)return Invalid(error);
        try{return Register(JsonRegistration.Parse(json));}catch(Exception ex){return Invalid("EZAPI_REGISTRATION_JSON: "+ex.Message);}
    }
    /// <summary>只读快照；随后续批次提交重新解析。不能以此判断原生目录Applied状态。</summary>
    public static IReadOnlyList<RegistrationView> Snapshot(){RequireThread();return Catalog.Snapshot();}
    public static bool IsResolved(string ownerId){RequireThread();RegistrationRules.Owner(ownerId);return Catalog.IsResolved(ownerId);}
    internal static void Start(){if(_started)return;_generation++;_thread=Environment.CurrentManagedThreadId;_started=true;}
    internal static void Stop(){_generation++;_started=false;Catalog.Clear();}
    internal static object Report()=>new{ready=_started,entries=Catalog.Snapshot(),scope="metadata dependency resolution only; native application remains in domain APIs"};
    private static string? Error(bool ignoreDeclaring=false)=>!_started||Environment.CurrentManagedThreadId!=_thread?"EZAPI_REGISTRATION_THREAD: active initialization thread required.":
        _declaring&&!ignoreDeclaring?"EZAPI_REGISTRATION_REENTRY: nested registration callback rejected.":ItemRegistry.DeclarationRegistrationError();
    private static void RequireThread(){if(!_started||Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("EZAPI_REGISTRATION_THREAD: Snapshot requires active EZapi main thread.");}
    private static SubmitResult Invalid(string reason)=>new(SubmitStatus.Invalid,reason);
}

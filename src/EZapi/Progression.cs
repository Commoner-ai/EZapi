using Il2Cpp;
namespace EZapi;

public enum ProgressionReadStatus { Ready, Missing, Unavailable, Invalid }
/// <summary>纯托管结果；Ready只证明当前只读观察，不代表已购买或允许跳过原生付款。</summary>
public sealed record ProgressionReadResult<T>(ProgressionReadStatus Status,T? Value=null,GameRunIdentity? Run=null,string? Reason=null) where T:class;
/// <summary>当前网络升级的原生状态；价格由GetCost、资格由原生判定提供，不猜测state数字。</summary>
public sealed record UpgradeSnapshot(string Id,string Title,int Cost,bool Unlocked,bool Ready,bool PrerequisitesMet,
    string MissingPrerequisite,bool DemoLocked,bool Repeatable,int CooldownRemaining,int CooldownDuration,IReadOnlyList<string> Prerequisites);
/// <summary>已介绍/已解锁/正在使用/实际生效/冷却分别报告；费用是当前原生字段，不调用UpdateCost。</summary>
public sealed record ServiceSnapshot(string Id,string NativeDisplayName,int Cost,bool Unlocked,bool Introduced,bool Using,
    bool Active,bool OnCooldown,int CooldownRemaining,int CooldownDuration,bool Mandatory);

/// <summary>
/// 知识库“网络升级与商店服务”的只读封装。每次从当前周目重新解析，不保存原生引用、不创建目录、不Unlock或支付。
/// 入口未知/损坏/周目变化有独立结果；Ready仍不是最终付款或写入资格。
/// </summary>
public static class ProgressionApi
{
    public static bool IsAvailable=>GameSession.OnThread&&GameBuildFingerprint.IsKnown&&RunData.IsAvailable;
    public static ProgressionReadResult<UpgradeSnapshot> ReadUpgrade(string id)
    {
        if(!ValidId(id))return new(ProgressionReadStatus.Invalid,Reason:"EZAPI_PROGRESSION_ID: readable stable ID<=160 required.");
        if(!IsAvailable)return new(ProgressionReadStatus.Unavailable,Reason:"EZAPI_PROGRESSION_NOT_READY");
        try {
            var run=NativeAccess.RequireRun();var item=NetworkUpgrade.GetUpgradeById(id);
            if(!NativeAccess.SameRun(run))return new(ProgressionReadStatus.Unavailable,Reason:"EZAPI_PROGRESSION_STALE");
            if(item is null||item.Pointer==IntPtr.Zero)return new(ProgressionReadStatus.Missing,Run:run);
            var pointer=item.Pointer;
            if(item.id!=id)throw new InvalidOperationException("upgrade lookup returned another ID");
            var prerequisites=new List<string>();
            if(item.prerequisite is { } source) {
                if(source.Count>128)throw new InvalidOperationException("prerequisite limit128");
                foreach(var key in source) {
                    if(!ValidId(key)||prerequisites.Contains(key,StringComparer.Ordinal))throw new InvalidOperationException("invalid or duplicate prerequisite ID");
                    prerequisites.Add(key);
                }
            }
            var snapshot=new UpgradeSnapshot(id,item.GetLocalizedTitle()??id,item.GetCost(),item.IsUnlocked(),item.IsReady(),
                item.IsHavePrerequisite(),item.GetMissingPrerequisite()??"",NetworkUpgrade.IsLockedInDemo(id),item.isRepeatable,
                item.cooldownCurrent,item.cooldownDuration,Array.AsReadOnly(prerequisites.ToArray()));
            if(snapshot.Cost<0||!NativeAccess.SameRun(run)||item.Pointer!=pointer||item.id!=id)
                throw new InvalidOperationException("upgrade scope or cost changed");
            return new(ProgressionReadStatus.Ready,snapshot,run);
        }catch(Exception error){return new(ProgressionReadStatus.Unavailable,Reason:"EZAPI_PROGRESSION_READ: "+error.GetType().Name);}
    }
    public static ProgressionReadResult<ServiceSnapshot> ReadService(string id)
    {
        if(!ValidId(id))return new(ProgressionReadStatus.Invalid,Reason:"EZAPI_PROGRESSION_ID: readable stable ID<=160 required.");
        if(!IsAvailable)return new(ProgressionReadStatus.Unavailable,Reason:"EZAPI_PROGRESSION_NOT_READY");
        try {
            var run=NativeAccess.RequireRun();var item=StoreService.FindServiceByID(id);
            if(!NativeAccess.SameRun(run))return new(ProgressionReadStatus.Unavailable,Reason:"EZAPI_PROGRESSION_STALE");
            if(item is null||item.Pointer==IntPtr.Zero)return new(ProgressionReadStatus.Missing,Run:run);
            var pointer=item.Pointer;
            if(item.id!=id)throw new InvalidOperationException("service lookup returned another ID");
            var snapshot=new ServiceSnapshot(id,item.displayName??id,item.cost,item.unlocked,item.introduced,item.isUsing,
                item.IsActive(),item.IsOnCooldown(),item.currentCooldown,item.totalCooldown,item.isMandatory);
            if(snapshot.Cost<0||!NativeAccess.SameRun(run)||item.Pointer!=pointer||item.id!=id)
                throw new InvalidOperationException("service scope or cost changed");
            return new(ProgressionReadStatus.Ready,snapshot,run);
        }catch(Exception error){return new(ProgressionReadStatus.Unavailable,Reason:"EZAPI_PROGRESSION_READ: "+error.GetType().Name);}
    }
    internal static bool ValidId(string? id)=>ReadableIds.IsValid(id);
}

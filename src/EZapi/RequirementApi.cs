using Il2Cpp;
namespace EZapi;

/// <summary>当前周目条件判断，不扣钱、不解锁、不初始化服务；可用于工坊/配方/事件/对话的只读资格。</summary>
public static class RequirementApi
{
    public static RequirementEvaluation Evaluate(RequirementDefinition definition)
    {
        var copy=Requirements.Copy(definition); // 即使游戏未载入，也先拒绝错误声明。
        if(!ProgressionApi.IsAvailable)return Unavailable(copy.Kind,"EZAPI_REQUIREMENT_NOT_READY");
        try {
            var facts=new NativeFacts();var result=Requirements.Evaluate(copy,facts,NativeItemLocalization.CurrentLocaleCode);
            return NativeAccess.SameRun(facts.Run)?result with {Run=facts.Run}:Unavailable(copy.Kind,"EZAPI_REQUIREMENT_STALE");
        }catch(Exception error){return Unavailable(copy.Kind,"EZAPI_REQUIREMENT_READ: "+error.GetType().Name);}
    }
    private static RequirementEvaluation Unavailable(string kind,string reason)=>new(RequirementStatus.Unavailable,kind,null,reason,Array.Empty<RequirementEvaluation>());
    private sealed class NativeFacts:IRequirementFacts
    {
        internal readonly GameRunIdentity Run=NativeAccess.RequireRun();
        private readonly Dictionary<string,ProgressionReadResult<UpgradeSnapshot>> _upgrades=new(StringComparer.Ordinal);
        private readonly Dictionary<string,ProgressionReadResult<ServiceSnapshot>> _services=new(StringComparer.Ordinal);
        private int? _day,_credits;
        public RequirementFact Read(string kind,string? targetId,long? amount)
        {
            if(!NativeAccess.SameRun(Run))return new(RequirementStatus.Unavailable,"EZAPI_REQUIREMENT_STALE");
            bool ok;
            if(kind.StartsWith("upgrade-",StringComparison.Ordinal)) {
                if(!_upgrades.TryGetValue(targetId!,out var row))_upgrades[targetId!]=row=ProgressionApi.ReadUpgrade(targetId!);
                if(row.Status!=ProgressionReadStatus.Ready||row.Run!=Run)return new(RequirementStatus.Unavailable,"EZAPI_REQUIREMENT_UPGRADE: "+row.Status);
                ok=kind=="upgrade-unlocked"?row.Value!.Unlocked:row.Value!.Ready;
            }else if(kind.StartsWith("service-",StringComparison.Ordinal)) {
                if(!_services.TryGetValue(targetId!,out var row))_services[targetId!]=row=ProgressionApi.ReadService(targetId!);
                if(row.Status!=ProgressionReadStatus.Ready||row.Run!=Run)return new(RequirementStatus.Unavailable,"EZAPI_REQUIREMENT_SERVICE: "+row.Status);
                var state=row.Value!;ok=kind switch {"service-unlocked"=>state.Unlocked,"service-introduced"=>state.Introduced,"service-active"=>state.Active,_=>!state.OnCooldown};
            }else if(kind.StartsWith("day-",StringComparison.Ordinal)) {
                _day??=StoreStation.GetDayCounter();if(_day<0)return new(RequirementStatus.Unavailable,"EZAPI_REQUIREMENT_DAY_UNKNOWN");
                ok=kind=="day-at-least"?_day>=amount:_day<=amount;
            }else {
                var player=GameSession.Resolve(Run);_credits??=player.GetCash();
                if(_credits<0)return new(RequirementStatus.Unavailable,"EZAPI_REQUIREMENT_CASH_UNKNOWN");ok=_credits>=amount;
            }
            return NativeAccess.SameRun(Run)?new(ok?RequirementStatus.Satisfied:RequirementStatus.Unsatisfied):new(RequirementStatus.Unavailable,"EZAPI_REQUIREMENT_STALE");
        }
    }
}

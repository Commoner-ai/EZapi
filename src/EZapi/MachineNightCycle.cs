namespace EZapi;

/// <summary>夜间有限同步循环。不根据性能自动增加批次；默认1批，动态额度只能缩减本次最初额度。</summary>
internal static class MachineNightCycle
{
    internal static (int Committed,string Status) Run(Func<int> resolve,Func<MachineBatchResult> process,Func<bool> current)
    {
        if(!current())return(0,"scope-stale");
        var initial=resolve();
        if(!current())return(0,"scope-stale");
        if(initial is <1 or >64)return(0,"nightly-count-invalid: expected1..64");
        var committed=0;var status="nightly-limit-reached";
        while(committed<initial) {
            if(!current())return(committed,"scope-stale");
            if(committed>0) {
                var next=resolve();
                if(!current())return(committed,"scope-stale");
                if(next is <1 or >64)return(committed,"nightly-count-invalid: expected1..64");
                if(committed>=next)return(committed,"nightly-limit-reached");
            }
            var result=process();status=result.Committed?"produced:"+result.RecipeId:result.Error??result.Transaction?.Error??"rejected";
            if(!result.Committed)return(committed,status);
            committed++;
        }
        return(committed,status);
    }
}

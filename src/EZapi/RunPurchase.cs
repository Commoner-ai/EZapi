using Il2Cpp;
namespace EZapi;

/// <summary>
/// 当前周目现金与作者交付步骤的同步补偿事务。Check只读，Apply/Restore核对自己的交付结果；
/// 作者步骤不可另外修改现金或切换周目。不是保存事务，不自动保存、重试或跨进程恢复。
/// </summary>
public static class RunPurchaseApi
{
    private const string Cash="ezapi.purchase.cash",Verify="ezapi.purchase.verify";
    private static bool _running;

    /// <summary>从当前已载入周目付款。允许0..128个作者步骤；空列表表示仅付款。</summary>
    public static TransactionResult Purchase(int cost,IReadOnlyList<TransactionStep> steps)
    {
        GameSession.RequireThread();
        if(!GameSession.TryResolve(null,out var store))throw new InvalidOperationException("EZAPI_PURCHASE_NOT_READY");
        return Purchase(store,cost,steps);
    }

    /// <summary>store必须是当前周目的准确实例，旧载入的包装对象不可用。cost为非负整数现金单位。</summary>
    public static TransactionResult Purchase(PlayerStore store,int cost,IReadOnlyList<TransactionStep> steps)
    {
        GameSession.RequireThread();
        ArgumentNullException.ThrowIfNull(steps);
        if(cost<0)throw new ArgumentOutOfRangeException(nameof(cost),"EZAPI_PURCHASE_COST");
        var snapshot=steps.ToArray();
        if(snapshot.Length>128||snapshot.Any(s=>s is null||string.IsNullOrWhiteSpace(s.Name)||s.Name.Length>160||s.Name.Any(char.IsControl)||
            s.Name is Cash or Verify||s.Check is null||s.Apply is null||s.Restore is null)||snapshot.Select(s=>s.Name).Distinct(StringComparer.Ordinal).Count()!=snapshot.Length)
            throw new ArgumentException("EZAPI_PURCHASE_STEPS: 0..128 unique named steps; ezapi.purchase.cash/verify are reserved.");
        if(_running)return Result(TransactionStatus.Reentrant,"EZAPI_PURCHASE_REENTRANT");
        if(!GameSession.TryResolve(null,out var current)||store is null||store.Pointer==IntPtr.Zero||current.Pointer!=store.Pointer)
            return Result(TransactionStatus.Rejected,"EZAPI_PURCHASE_STORE");
        var identity=GameSession.Identity!;
        bool Scope()=>GameSession.TryResolve(identity.Epoch,out var player)&&GameSession.Identity==identity&&player.Pointer==store.Pointer;
        int Read(){if(!Scope())throw new InvalidOperationException("EZAPI_PURCHASE_SCOPE");var value=store.GetCash();if(!Scope()||value!=store.playerCash)throw new InvalidOperationException("EZAPI_PURCHASE_CASH_READBACK");return value;}
        _running=true;
        try {
            int before;
            try{before=Read();}catch(Exception e){return Result(TransactionStatus.Rejected,e.Message);}
            if(before<0||before<cost)return Result(TransactionStatus.Rejected,"EZAPI_PURCHASE_FUNDS");
            var after=before-cost;
            bool paid=false,interference=false,recoverySound=true;
            string? guardError=null;
            bool Guard(int expected)
            {
                try{if(Read()==expected)return true;guardError="EZAPI_PURCHASE_CASH_INTERFERENCE";}
                catch(Exception e){guardError=e.Message;}
                interference=true;return false;
            }
            bool Check(TransactionStep step)
            {
                var expected=paid?after:before;
                if(!Guard(expected))return false;
                try{return step.Check();}
                finally{Guard(expected);}
            }
            bool Apply(TransactionStep step)
            {
                if(interference||!Guard(after))return false;
                try{return step.Apply();}
                finally{Guard(after);}
            }
            bool Restore(TransactionStep step)
            {
                if(!Scope()){guardError="EZAPI_PURCHASE_SCOPE";interference=true;recoverySound=false;return false;}
                try {
                    var ok=step.Restore();
                    if(!Guard(after))ok=false;
                    recoverySound&=ok;return ok;
                }catch{recoverySound=false;throw;}
            }
            var all=new List<TransactionStep>{new(Cash,()=>!interference&&Guard(before),()=>{
                if(cost!=0)store.PayMoney(cost,false);
                var balance=Read();paid=balance==after;
                if(!paid&&balance!=before){guardError="EZAPI_PURCHASE_CASH_READBACK";interference=true;}
                return paid;
            },()=>{
                // 只退本事务确认过的准确扣款；不覆盖作者/第三方修改、不触碰新载入，也不在交付恢复失败后退款。
                if(!recoverySound||!Scope()||interference)return false;
                var balance=Read();if(balance==before)return true;
                if(balance!=after){guardError="EZAPI_PURCHASE_CASH_INTERFERENCE";interference=true;return false;}
                if(cost!=0)store.ModCash(cost,false);
                return Scope()&&Read()==before;
            })};
            all.AddRange(snapshot.Select(step=>new TransactionStep(step.Name,()=>Check(step)&&!interference,()=>Apply(step)&&!interference,()=>Restore(step))));
            all.Add(new(Verify,()=>!interference&&Guard(paid?after:before),()=>!interference&&Guard(after),()=>true));
            var result=Transactions.RunPurchase(all);
            // 只读Check违规也可能改现金或切换载入；即便尚未付款，也不可返回“已恢复”。
            var expected=result.Committed?after:before;
            if(result.Status is not (TransactionStatus.Reentrant or TransactionStatus.RecoveryFailed)&&(!Guard(expected)||interference)) {
                var failures=result.RecoveryFailures.Concat(new[]{Cash}).Distinct(StringComparer.Ordinal).ToArray();
                return new(TransactionStatus.RecoveryFailed,result.FailedStep??Cash,result.AppliedSteps,Array.AsReadOnly(failures),guardError??result.Error??"EZAPI_PURCHASE_CASH_INTERFERENCE");
            }
            // 原生调用抛错但余额未变时，付款步骤已经尝试，通用引擎仍会核对恢复。
            return guardError is not null&&result.Error is null?result with{Error=guardError}:result;
        }finally{_running=false;}
    }
    private static TransactionResult Result(TransactionStatus status,string error)
        =>new(status,null,Array.Empty<string>(),Array.Empty<string>(),error);
    internal static object Report()=>new{running=_running};
}

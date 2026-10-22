namespace EZapi.Sample;

/// <summary>Shows dynamic supplier menus. The sample callback deliberately performs no payment or delivery.</summary>
public static class SupplierExamples
{
    public const string Owner="author.vendor";

    public static readonly SupplierDefinition Definition=new(
        Id:Owner+".supplier",
        Speaker:"示例供应商",
        Capture:Capture,
        Dismiss:_=>{},
        ConfirmLabel:"确认报价",
        BackLabel:"返回报价",
        ContinueLabel:"查看最新报价",
        LeaveLabel:"结束交易",
        FailureText:"报价暂不可用，请稍后再来。")
    {
        RequireConfirmation=true,
        AllowMenuRefresh=true,
        CanContinue=_=>true
        ,LocalizedSpeaker=new("示例供应商","Example supplier")
        ,LocalizedConfirmLabel=new("确认报价","Confirm offer")
        ,LocalizedBackLabel=new("返回报价","Back to offers")
        ,LocalizedContinueLabel=new("查看最新报价","Refresh offers")
        ,LocalizedLeaveLabel=new("结束交易","End trade")
        ,LocalizedFailureText=new("报价暂不可用，请稍后再来。","Offers are unavailable. Please return later.")
    };

    public static PresentationResult Queue(LifecycleContext context,string requestId,string spriteName)
    {
        ArgumentNullException.ThrowIfNull(context);
        if(!context.IsCurrent)return new(PresentationStatus.Unavailable,"EZAPI_SUPPLIER_SCOPE");
        return SupplierApi.Queue(Owner,requestId,context.Run.RunId,context.Run.SlotId,
            Owner+".stockist",spriteName,Definition);
    }

    public static PresentationResult Status(string requestId)=>SupplierApi.Status(Owner,requestId);

    private static SupplierPage Capture(DialogueContext context)
    {
        return new SupplierPage("本页报价会随每次继续交易重新读取。",new[]
        {
            new SupplierOffer(Owner+".sample_item","查看示例商品","确认后由模组负责扣款和交付。",
                c=>new LocalizedItemText("示例报价已确认；请将此回调替换为自己的资格检查、交易和回滚逻辑。",
                    "Example confirmed. Replace this callback with eligibility, trade and recovery logic.").ForLocale(c.LocaleCode)!,"确认示例"){
                    LocalizedLabel=new("查看示例商品","View example product"),
                    LocalizedPreview=new("确认后由模组负责扣款和交付。","The Mod handles payment and delivery after confirmation."),
                    LocalizedConfirmLabel=new("确认示例","Confirm example")}
        })
        {
            AutomaticOfferId=Owner+".sample_item",
            AutomaticDelaySeconds=8
            ,LocalizedBody=new("本页报价会随每次继续交易重新读取。","Offers are captured again whenever you continue trading.")
        };
    }
}

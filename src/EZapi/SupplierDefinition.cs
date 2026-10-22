namespace EZapi;

/// <summary>一次供应商访问中的单项报价。Purchase 仅在报价被确认时运行一次；库存与支付由模组自行处理。</summary>
public sealed record SupplierOffer(string Id,string Label,string Preview,Func<DialogueContext,string> Purchase,string? ConfirmLabel=null)
{
    /// <summary>Optional inline Chinese/English Label text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedLabel{get;init;}
    /// <summary>Optional inline Chinese/English Preview text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedPreview{get;init;}
    /// <summary>Optional inline Chinese/English ConfirmLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedConfirmLabel{get;init;}
}

/// <summary>Capture 返回的只读菜单快照。自动报价只会在该报价按钮实际显示后开始计时。</summary>
public sealed record SupplierPage(string Body,IReadOnlyList<SupplierOffer> Offers)
{
    public string? AutomaticOfferId{get;init;}
    public double AutomaticDelaySeconds{get;init;}
    /// <summary>Optional inline Chinese/English Body text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedBody{get;init;}
}

/// <summary>供应商访问定义。框架负责访客、对话、分页和确认；商品交付/支付仍由 Purchase 回调负责。</summary>
public sealed record SupplierDefinition(string Id,string Speaker,
    Func<DialogueContext,SupplierPage> Capture,
    Action<DialogueContext>? Dismiss=null,
    string ConfirmLabel="购买",string BackLabel="返回",
    string ContinueLabel="继续交易",string LeaveLabel="离开",
    string FailureText="供应商报价操作未完成，请查看日志。")
{
    /// <summary>Optional inline Chinese/English Speaker text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedSpeaker{get;init;}
    /// <summary>Optional inline Chinese/English ConfirmLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedConfirmLabel{get;init;}
    /// <summary>Optional inline Chinese/English BackLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedBackLabel{get;init;}
    /// <summary>Optional inline Chinese/English ContinueLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedContinueLabel{get;init;}
    /// <summary>Optional inline Chinese/English LeaveLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedLeaveLabel{get;init;}
    /// <summary>Optional inline Chinese/English FailureText text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedFailureText{get;init;}
    /// <summary>Optional inline Chinese/English NextPageLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedNextPageLabel{get;init;}
    /// <summary>Optional inline Chinese/English PreviousPageLabel text; resolved when a new dialogue graph opens. CSV keys require explicit catalog resolution.</summary>
    public LocalizedItemText? LocalizedPreviousPageLabel{get;init;}
    public bool RequireConfirmation{get;init;}=true;
    public bool AllowMenuRefresh{get;init;}=true;
    public Func<DialogueContext,bool>? CanContinue{get;init;}
    public string NextPageLabel{get;init;}="下一页";
    public string PreviousPageLabel{get;init;}="上一页";
}

internal static class SupplierContract
{
    internal static SupplierDefinition Freeze(string owner,SupplierDefinition definition)
    {
        if(definition is null||!PresentationRules.Owned(owner,definition.Id)||
            !Text(definition.Speaker,512,definition.LocalizedSpeaker is null)||definition.Capture is null||
            !Text(definition.ConfirmLabel,512,definition.LocalizedConfirmLabel is null)||!Text(definition.BackLabel,512,definition.LocalizedBackLabel is null)||
            !Text(definition.ContinueLabel,512,definition.LocalizedContinueLabel is null)||!Text(definition.LeaveLabel,512,definition.LocalizedLeaveLabel is null)||
            !Text(definition.FailureText,131072,definition.LocalizedFailureText is null)||!Text(definition.NextPageLabel,512,definition.LocalizedNextPageLabel is null)||
            !Text(definition.PreviousPageLabel,512,definition.LocalizedPreviousPageLabel is null))
            throw new ArgumentException("EZAPI_SUPPLIER_DEFINITION: owned ID, speaker, capture callback and bounded labels are required.");
        foreach(var text in new[]{definition.LocalizedSpeaker,definition.LocalizedConfirmLabel,definition.LocalizedBackLabel,
            definition.LocalizedContinueLabel,definition.LocalizedLeaveLabel,definition.LocalizedNextPageLabel,definition.LocalizedPreviousPageLabel})ValidateLocalized(text,512);
        ValidateLocalized(definition.LocalizedFailureText,131072);
        return definition with {
            LocalizedConfirmLabel=Default(definition.LocalizedConfirmLabel,definition.ConfirmLabel,"购买","Buy"),
            LocalizedBackLabel=Default(definition.LocalizedBackLabel,definition.BackLabel,"返回","Back"),
            LocalizedContinueLabel=Default(definition.LocalizedContinueLabel,definition.ContinueLabel,"继续交易","Continue trading"),
            LocalizedLeaveLabel=Default(definition.LocalizedLeaveLabel,definition.LeaveLabel,"离开","Leave"),
            LocalizedFailureText=Default(definition.LocalizedFailureText,definition.FailureText,"供应商报价操作未完成，请查看日志。","The supplier action did not complete. Check the log."),
            LocalizedNextPageLabel=Default(definition.LocalizedNextPageLabel,definition.NextPageLabel,"下一页","Next page"),
            LocalizedPreviousPageLabel=Default(definition.LocalizedPreviousPageLabel,definition.PreviousPageLabel,"上一页","Previous page")
        };
    }

    internal static SupplierPage FreezePage(SupplierPage page)
    {
        if(page is null||!Text(page.Body,131072,false)||page.Offers is null||page.Offers.Count>128||
            !double.IsFinite(page.AutomaticDelaySeconds)||page.AutomaticDelaySeconds<0||page.AutomaticDelaySeconds>3600)
            throw new ArgumentException("EZAPI_SUPPLIER_PAGE: bounded body, at most 128 offers and a finite delay in [0,3600] are required.");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        var offers=new SupplierOffer[page.Offers.Count];
        for(var i=0;i<offers.Length;i++)
        {
            var offer=page.Offers[i];
            if(offer is null||!PresentationRules.Owner(offer.Id)||!ids.Add(offer.Id)||!Text(offer.Label,512,offer.LocalizedLabel is null)||
                !Text(offer.Preview,131072,false)||offer.Purchase is null||
                offer.ConfirmLabel is not null&&!Text(offer.ConfirmLabel,512,offer.LocalizedConfirmLabel is null))
                throw new ArgumentException("EZAPI_SUPPLIER_OFFER: unique bounded IDs, labels, preview and purchase callback are required.");
            ValidateLocalized(offer.LocalizedLabel,512);ValidateLocalized(offer.LocalizedPreview,131072);ValidateLocalized(offer.LocalizedConfirmLabel,512);
            offers[i]=offer with{};
        }
        if(page.AutomaticOfferId is not null&&(!PresentationRules.Owner(page.AutomaticOfferId)||!ids.Contains(page.AutomaticOfferId)))
            throw new ArgumentException("EZAPI_SUPPLIER_AUTO_OFFER: the automatic offer ID must match an offer on this page.");
        if(page.AutomaticOfferId is null&&page.AutomaticDelaySeconds!=0)
            throw new ArgumentException("EZAPI_SUPPLIER_AUTO_DELAY: delay requires an automatic offer ID.");
        ValidateLocalized(page.LocalizedBody,131072);
        return page with{Offers=Array.AsReadOnly(offers)};
    }

    private static bool Text(string? value,int limit,bool nonempty)=>PresentationRules.Text(value,limit)&&
        (!nonempty||!string.IsNullOrWhiteSpace(value));
    private static void ValidateLocalized(LocalizedItemText? text,int limit){if(text is null)return;text.Validate(limit);}
    private static LocalizedItemText? Default(LocalizedItemText? text,string literal,string chinese,string english)
        =>text??(literal==chinese?new(chinese,english):null);
    internal static string ResolveText(LocalizedItemText? text,string literal,string locale)=>text?.ForLocale(locale)??literal;

    private static bool Token(string? value)=>value is {Length:>0 and <=160}&&value.All(c=>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-' or ':');
}

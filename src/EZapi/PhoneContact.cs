namespace EZapi;

/// <summary>当前电话请求身份；Generation 仅标识本次载入，不是可保存的业务幂等键。</summary>
public sealed record PhoneContext(long Generation,string RunId,int SlotId);
/// <summary>Visible/DisplayName 为只读查询；Call 只返回不可变脚本，不在这里扣款或发放物品。</summary>
public sealed record PhoneContact(string Id,long Number,Func<PhoneContext,bool> Visible,
    Func<PhoneContext,string> DisplayName,Func<PhoneContext,DialogueScript> Call);
public sealed record PhoneContactView(string OwnerId,string Id,long Number,bool Applied,bool Visible,string? Error);

internal sealed class PhoneEntry
{
    internal readonly string Owner;
    internal readonly PhoneContact Definition;
    internal bool Visible,Applied;
    internal string Name="",Error="";
    internal string Marker=>"ezapi.phone."+Definition.Id;
    internal PhoneEntry(string owner,PhoneContact definition)=>(Owner,Definition)=(owner,definition with{});
    internal PhoneContactView View()=>new(Owner,Definition.Id,Definition.Number,Applied,Visible,string.IsNullOrEmpty(Error)?null:Error);
}
internal sealed class PhoneCatalog
{
    internal readonly Dictionary<long,PhoneEntry> Entries=new();
    internal PresentationResult Register(string owner,PhoneContact contact)
    {
        if(contact is null||!PresentationRules.Owned(owner,contact.Id)||contact.Number is <100000 or >9999999999||
            contact.Visible is null||contact.DisplayName is null||contact.Call is null)return new(PresentationStatus.Invalid,"EZAPI_PHONE_CONTACT");
        if(Entries.ContainsKey(contact.Number)||Entries.Values.Any(e=>e.Definition.Id==contact.Id))return new(PresentationStatus.Conflict,"EZAPI_PHONE_DUPLICATE");
        if(Entries.Count>=128)return new(PresentationStatus.Conflict,"EZAPI_PHONE_LIMIT");
        Entries.Add(contact.Number,new(owner,contact));return new(PresentationStatus.Queued);
    }
}

namespace EZapi.Sample;

/// <summary>在初始化时登记一次；不需要手动创建原生电话对象、轮询UI或安装Harmony补丁。</summary>
public static class PhoneExamples
{
    private const string Owner="author.phone",ContactId=Owner+".help";
    private static readonly DialogueScript Help=DialogueApi.Create(Owner,new(
        Owner+".greeting","维修顾问","需要了解什么？",new[]{
            new DialogueChoice("help","查看帮助","确认后显示说明。",context=>"请把维修材料放入相应机器的输入槽。")
        }));
    public static PresentationResult Register()=>PhoneApi.Register(Owner,new(
        ContactId,9000000012,
        context=>true, // 可以读取当前周目进度；此查询不得扣款、发物品或调用展示接口。
        context=>"维修顾问",
        context=>Help)); // 这里只返回脚本。业务放在选项Confirm中，并自行实现恢复和持久幂等。
    public static PresentationResult Unregister()=>PhoneApi.Unregister(Owner,ContactId);
}

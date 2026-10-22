// 此离线套件只覆盖真实配方解析器和自定义事务分支。
// 标准事务涉及原生拆堆/合堆，单独由游戏内DebugMachineMaterials验收。
// 如意外进入默认分支立即失败，不能用替身的空事务冒充标准消费证据。
namespace EZapi;
internal sealed class DefaultMachineTransaction:IMachineBatchTransaction,IDisposable
{
    internal DefaultMachineTransaction(MachineRecipePlan plan)=>throw new NotSupportedException("Default branch requires native material tests.");
    internal void Prepare(Il2Cpp.GameItem item)=>throw new NotSupportedException();
    internal bool RestoredExternalPower=>throw new NotSupportedException("external power requires native test");
    internal void Complete()=>throw new NotSupportedException();
    public bool Validate()=>throw new NotSupportedException();
    public void Apply(IReadOnlyList<Il2Cpp.GameItem> products)=>throw new NotSupportedException();
    public bool Verify()=>throw new NotSupportedException();
    public bool Restore()=>throw new NotSupportedException();
    public void Fault(string reason)=>throw new NotSupportedException();
    public void Dispose(){}
}

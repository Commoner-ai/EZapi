using System.Text.Json;
namespace EZapi.Sample;

/// <summary>只读历史存档的最小完整例子；不启动游戏、不恢复物品、不触发保存。</summary>
public static class SaveReadbackExamples
{
    public static (int SlotId,string RunId) ReadIdentity(string path)
    {
        using var result=SaveReadbackApi.ReadFile(path);
        if(result.Status!=SaveReadStatus.Readable)throw new InvalidDataException(result.Reason??result.Status.ToString());
        // 当前ES3文本格式：playerStore.value包含状态。缺少/重复时明确拒绝，勿用默认0冒充成功。
        var envelope=SaveReadbackApi.RequireProperty(result.Root,"playerStore");
        var state=SaveReadbackApi.RequireProperty(envelope,"value");
        var slot=SaveReadbackApi.RequireProperty(state,"saveSlotId").GetInt32();
        var run=SaveReadbackApi.RequireProperty(state,"runID").GetString();
        if(slot<0||string.IsNullOrWhiteSpace(run))throw new InvalidDataException("Expected a stored slot and run ID.");
        return(slot,run);
    }
    public static JsonElement ReadDetachedRoot(string path)
    {
        using var result=SaveReadbackApi.ReadFile(path);
        if(result.Status!=SaveReadStatus.Readable)throw new InvalidDataException(result.Reason??result.Status.ToString());
        return result.Root.Clone(); // 需要跨using保留时显式Clone，通常只摘取所需字段更省内存。
    }
}

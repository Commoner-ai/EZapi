using Il2Cpp;
namespace EZapi.Sample;
/// <summary>SaveGraph只在内容Mod自己的当前事件中显式调用；不会自动保存或自动放回库存。</summary>
public static class SaveGraphExamples
{
    public static string ExportDetached(GameItem root)=>EZapi.SaveGraphApi.Encode(root);
    public static GameItem ImportDetached(string json,IReadOnlySet<int> existingIds)=>EZapi.SaveGraphApi.Decode(json,existingIds);
}

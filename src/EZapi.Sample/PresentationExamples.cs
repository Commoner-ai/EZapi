using EZapi;

namespace EZapi.Sample;

/// <summary>展示 owner 名称和模块 tooltip 回调。调用前不要重复注册示例模块。</summary>
public static class PresentationExamples
{
    public static IReadOnlyList<ContentSubmissionResult> Register()
    {
        const string owner = "author.tools";
        const string itemId = "author.tools.sensor";
        var ownerText = PresentationApi.RegisterOwnerDisplayName(owner, "工具示例", "Tools example");
        var item = NativeItemExamples.RegisterModule();
        var moduleText = item.Accepted
            ? PresentationApi.RegisterModuleText(owner, itemId, (instance, english) => new ModuleText(
                AdditionalEffect: english ? "Reads the current device state" : "读取当前设备状态",
                Lines: new[] { (english ? "Item ID: " : "物品 ID：") + instance.identifier }))
            : new ContentSubmissionResult(false, "Register the example module first: " + item.Reason);
        return new[] { ownerText, item, moduleText };
    }
}

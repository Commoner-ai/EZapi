using EZapi;
using Il2Cpp;
namespace EZapi.Sample;

/// <summary>仅供作者调用的示例，不自动启用，不修改原版存档或注册虚构机器。</summary>
public static class ModuleExamples
{
    /// <summary>初始化时调用。machineId必须是实际机器ID；返回Accepted后仍须检查原生admission组。</summary>
    public static ContentSubmissionResult Admit(string machineId)
        => ModuleApi.RegisterAdmission("sample.modules", new[] { new ModuleAdmission(machineId, new[] { "sample.modules.precision" }) });

    /// <summary>观察每次原生成长前后差异；此处只回传观察值，不自动发放奖励或保存。</summary>
    public static IDisposable ObserveLearning<T>(Func<GameItem, T> capture, Action<GameItem, T> completed)
        => ModuleApi.SubscribeLearning("sample.modules", "learning", capture, completed);

    /// <summary>通知发生在原生动作之前，不等于生产成功；不要据此无条件累计成长。</summary>
    public static IDisposable ObserveAction(Action<ModuleActionContext> observer)
        => ModuleApi.SubscribeAction("sample.modules", "action", "sample.modules.precision", observer);

    /// <summary>回调内立即使用Modules；不将context或原生对象缓存到后续帧。</summary>
    public static IDisposable AfterTemporaryStats(Action<IReadOnlyList<GameItem>> observer)
        => ModuleApi.Subscribe("sample.modules", "temporary", ModulePhase.AfterTemp, context => observer(context.Modules), order:100);
}

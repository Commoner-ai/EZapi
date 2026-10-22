using EZapi;
namespace EZapi.Sample;

/// <summary>最小生命周期/内容通知调用；订阅在初始化回调中创建，退出时由using释放。</summary>
public static class LifecycleContentExamples
{
    public static IDisposable SubscribeAfterNight(Action<LifecycleContext> callback)
        => LifecycleApi.Subscribe("ezapi.sample", "after-night", LifecyclePhase.AfterNight, callback);

    public static IDisposable ObserveOwnContent(string owner, Action<NativeApplicationView> callback)
        => ContentApi.Subscribe("ezapi.sample", "content-" + owner, view => {
            if (view.OwnerId == owner) callback(view);
        });
}

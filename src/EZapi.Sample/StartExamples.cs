using EZapi.Registration;
using Il2Cpp;

namespace EZapi.Sample;

/// <summary>展示官方开局身份枚举的注册与只读快照。不会修改游戏菜单或发放开局物品。</summary>
public static class StartExamples
{
    public static SubmitResult RegisterApprentice()
        => StartApi.RegisterStart("ezapi.sample", "ezapi.sample.apprentice", NewGameData.StartType.APPRENTICE);

    public static IReadOnlyList<StartRegistrationView> Snapshot()
        => StartApi.Snapshot();
}

using EZapi.Registration;
namespace EZapi.Sample;

/// <summary>在Mod的OnInitializeMelon调用；教学方法不会自动执行或注册游戏物品。</summary>
public static class RegistrationExamples
{
    public static SubmitResult DeclareMetadata()=>RegistrationApi.Register("author.tools","1.0.0",batch=>batch
        .Require("author.core","1.2.0")
        .Define(DefinitionKind.Effect,"stable","effect:v1")
        .Define(DefinitionKind.Module,"sensor","module:v1","author.tools.stable","author.core.base"));
    // 依赖已满足后，仍需通过EffectApi、ModuleApi或ItemRegistry等相应入口提交实际内容。
    // 不要把有游戏副作用的操作塞进此回调；只有通用元数据具有整批提交保证。
}

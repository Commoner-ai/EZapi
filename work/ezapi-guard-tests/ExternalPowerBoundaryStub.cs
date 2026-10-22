// 本套件不验证外电；外电真实状态机和原生钩子分别由external-power独立套件与实机覆盖。
namespace EZapi;
internal static class ExternalPowerRuntime {internal static NativeHookStatus Status()=>new("ezapi.power.external",false,Array.Empty<string>(),"not installed in this suite");}

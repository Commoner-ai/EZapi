// 本套件不验证外电；外电真实状态机和原生钩子分别由external-power独立套件与实机覆盖。
namespace EZapi;
internal static class ExternalPowerApi {internal static bool IsConnector(Il2Cpp.GameItem item)=>false;internal static int GetEnergy(Il2Cpp.GameItem item)=>throw new NotSupportedException("external power requires dedicated tests");}

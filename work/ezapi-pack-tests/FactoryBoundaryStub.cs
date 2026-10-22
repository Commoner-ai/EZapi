
namespace EZapi {
 internal static class NativeFactoryRegistry {
  internal static void Clear(){} internal static bool Contains(string id)=>false;
  internal static bool IsOwnedModule(string owner,string id)=>false;
  internal static bool IsDeclaredModule(string owner,string id)=>false;
  internal static string? Status(string id)=>null;
  internal static IReadOnlyList<NativeApplicationView> Views()=>Array.Empty<NativeApplicationView>();
  internal static IReadOnlyList<NativeApplicationView> Apply(Il2Cpp.ModItemDirectory d)=>Array.Empty<NativeApplicationView>();
  internal static bool TryPresentation(string id,out ItemDefinition d){d=null!;return false;}
  internal static IEnumerable<DebugItemRow> DebugRows()=>Array.Empty<DebugItemRow>();
 }
 internal static class NativeItemLocalization { internal static string CurrentLocaleCode=>"en"; }
}

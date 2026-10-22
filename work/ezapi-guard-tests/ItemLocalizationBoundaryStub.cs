namespace EZapi;
// This suite tests inventory guards only; item-localization owns its separate focused suite.
internal static class DeclarativeItemLocalization
{
    internal static NativeHookStatus Status()=>new("ezapi.items.localization",false,Array.Empty<string>(),"not part of the inventory guard fixture");
}

namespace EZapi;
internal static class NativeItemLocalization
{
 internal static string Locale="zh";
 internal static int Reads;
 internal static string CurrentLocaleCode{get{Reads++;return Locale;}}
}

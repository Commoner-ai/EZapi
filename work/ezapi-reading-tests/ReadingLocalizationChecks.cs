using EZapi;
internal static class ReadingLocalizationChecks
{
 internal static void Run(Action<bool,string> check,Action<Action,string> reject)
 {
  var original=new LocalizedItemText("标题","Title");var source=new LocalizedItemText("第一行\n第二行","First line\nSecond line");var signature=new LocalizedItemText("作者","Author");
  foreach(var locale in new[]{"en","en-US","zh","zh-CN","fr"}){
   var text=ReadingTextResolution.Resolve(locale,original,source,signature);var zh=locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
   check(text.Title==(zh?"标题":"Title")&&text.Body==(zh?"第一行\n第二行":"First line\nSecond line")&&text.Signature==(zh?"作者":"Author"),"reading page resolves all text in one locale "+locale);
  }
  check(ReadingTextResolution.Resolve("en",new("中文",null),new("正文",null),new("署名",null)).Body=="正文","reading missing English side falls back to Chinese");
  check(ReadingTextResolution.Resolve("zh",new(null,"Title"),new(null,"Body"),new(null,"Author")).Body=="Body","reading missing Chinese side falls back to English");
  foreach(var bad in new[]{new LocalizedItemText(),new(" ","English"),new("bad\0text","English"),new LocalizedItemText("正文","Body"){Key="csv"}})
   reject(()=>ReadingTextResolution.Resolve("en",bad,new("正文","Body"),new("作者","Author")),"invalid localized title rejected before native creation");
  reject(()=>ReadingTextResolution.Resolve("en",new("标题",new string('x',513)),new("正文","Body"),new("作者","Author")),"localized title 512-character bound");
  reject(()=>ReadingTextResolution.Resolve("en",new("标题","Title"),new("正文",new string('x',131073)),new("作者","Author")),"localized body bound");
  reject(()=>ReadingTextResolution.Resolve("en",new("标题","Title"),new("正文","Body"),new("作者",new string('x',4097))),"localized signature bound");
  check(original.Chinese=="标题"&&original.English=="Title","resolution preserves reusable author values");
 }
}

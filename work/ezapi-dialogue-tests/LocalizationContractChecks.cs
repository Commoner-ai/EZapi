using EZapi;
internal static class LocalizationContractChecks
{
 internal static void Run(Action<bool,string> check,Action<Action,string> reject)
 {
  var fields=new[]{"Speaker","Body","ConfirmLabel","BackLabel","LeaveLabel","LaterLabel","FailureText","NextPageLabel","PreviousPageLabel"};
  DialogueDefinition Definition()=>new("author.locale","Literal speaker","Literal body",new[]{new DialogueChoice("choice","Literal label","Literal preview",c=>c.LocaleCode)});
  foreach(var field in fields){
   var property=typeof(DialogueDefinition).GetProperty("Localized"+field)!;
   var d=Definition();property.SetValue(d,new LocalizedItemText("中文 "+field,"English "+field));
   var frozen=DialogueContract.Freeze("author",d);
   foreach(var locale in new[]{"en","zh-CN","fr"}){
    var resolved=DialogueContract.ResolveLocale(frozen,locale);var value=(string)typeof(DialogueDefinition).GetProperty(field)!.GetValue(resolved.Definition)!;
    check(value==(locale.StartsWith("zh")?"中文 ":"English ")+field,"all definition locale fields resolve "+field+" "+locale);
    check(property.GetValue(resolved.Definition) is null&&property.GetValue(frozen.Definition) is not null,"resolved snapshot leaves original localized field intact "+field+" "+locale);
   }
   foreach(var invalid in new[]{new LocalizedItemText(),new(" ","English"),new("bad\0text","English"),new LocalizedItemText("中文","English"){Key="unbound"},new LocalizedItemText("中文",new string('x',field is "Body" or "FailureText"?131073:513))}){
    var bad=Definition();property.SetValue(bad,invalid);reject(()=>DialogueContract.Freeze("author",bad),"reject invalid localized "+field);
   }
  }
  foreach(var field in new[]{"Label","Preview","ConfirmLabel"}){
   var d=Definition();var choice=d.Choices[0];typeof(DialogueChoice).GetProperty("Localized"+field)!.SetValue(choice,new LocalizedItemText("中文 "+field,"English "+field));
   var source=DialogueContract.Freeze("author",d);var resolved=DialogueContract.ResolveLocale(source,"en");
   check((string?)typeof(DialogueChoice).GetProperty(field)!.GetValue(resolved.Definition.Choices[0])=="English "+field,"choice localized field resolves "+field);
   check(ReferenceEquals(source.Definition.Choices[0].Confirm,resolved.Definition.Choices[0].Confirm),"locale mapping retains business delegate "+field);
  }
  var defaults=DialogueContract.ResolveLocale(DialogueContract.Freeze("author",Definition()),"en").Definition;
  check(defaults.ConfirmLabel=="Confirm"&&defaults.BackLabel=="Back"&&defaults.LeaveLabel=="Leave"&&defaults.LaterLabel=="Later"&&defaults.NextPageLabel=="Next page"&&defaults.PreviousPageLabel=="Previous page","all built-in dialogue navigation labels are English");
  var custom=Definition()with{ConfirmLabel="确认",LocalizedConfirmLabel=new("确认","确认")};
  check(DialogueContract.ResolveLocale(DialogueContract.Freeze("author",custom),"en").Definition.ConfirmLabel=="确认","explicit bilingual override preserves author text even matching built-in default");
  var callbacks=new List<string>();var frozenDefinition=DialogueContract.Freeze("author",Definition()).Definition;
  var script=new DialogueScript("author",frozenDefinition,"literal",c=>callbacks.Add(c.LocaleCode),c=>callbacks.Add(c.LocaleCode),"choice",new("继续","Continue"));
  var local=DialogueContract.ResolveLocale(script,"zh");
  check(local.ContinueLabel=="继续"&&local.AutomaticChoiceId=="choice"&&ReferenceEquals(script.OnContinue,local.OnContinue),"locale snapshot retains automatic choice and continue identity");
  var commit=new DialogueExecution(local,1,"run",1,"author.request");check(commit.Commit(0,()=>true,_=>{})=="zh","dynamic reply receives graph locale");
  commit.Continue(0,()=>true,_=>{});var leave=new DialogueExecution(local,2,"run",1,"author.request");leave.Leave(()=>true,_=>{});
  check(callbacks.SequenceEqual(new[]{"zh","zh"}),"continue and leave callbacks receive graph locale");
  var dOnly=Definition()with{Speaker="",LocalizedSpeaker=new("中文","English"),ConfirmLabel="",LocalizedConfirmLabel=new("确认","Accept")};
  check(DialogueContract.ResolveLocale(DialogueContract.Freeze("author",dOnly),"en").Definition.Speaker=="English","localized-only declarations accept empty literal fallback");
 }
}

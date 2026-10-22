using System.Text;
using System.Text.Json;
using EZapi;

int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;}
DeveloperToolsOptions Parse(string text)=>DeveloperToolsSettings.Parse(Encoding.UTF8.GetBytes(text));
Check(Parse("{}") is {Enabled:false,RegressionFixtures:false},"empty options keep runtime clean");
Check(Parse("{\"enabled\":true}") is {Enabled:true,RegressionFixtures:false},"developer panel does not enable fixtures");
Check(Parse("{\"enabled\":true,\"regressionFixtures\":true}") is {Enabled:true,RegressionFixtures:true},"explicit double opt-in enables fixtures");
Check(Parse("{\"enabled\":false,\"regressionFixtures\":true}") is {Enabled:false,RegressionFixtures:false},"disabled parent suppresses fixtures");
Check(Parse("{\"regressionFixtures\":true}") is {Enabled:false,RegressionFixtures:false},"fixtures alone cannot enable tools");
foreach(var bad in new[]{"", "[]", "null", "{\"enabled\":\"true\"}", "{\"enabled\":1}", "{\"enabled\":true,\"enabled\":false}", "{\"Enabled\":true}", "{\"enabled\":true,\"unknown\":false}", "{\"enabled\":true,}",new string(' ',4097)}){
    bool rejected=false;try{Parse(bad);}catch(Exception ex)when(ex is JsonException or InvalidDataException){rejected=true;}
    Check(rejected,"invalid config rejected");
}
Check(DeveloperToolsSettings.Parse(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("{\"enabled\":true}")).ToArray()).Enabled,"UTF8 BOM supported");
var directory=Path.Combine(Path.GetTempPath(),"ezapi-options-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
try{
    var path=Path.Combine(directory,"developer-tools.json");var warnings=new List<string>();
    DeveloperToolsSettings.Load(path,warnings.Add);
    Check(!DeveloperToolsSettings.Current.Enabled&&warnings.Count==0&&!File.Exists(path),"missing file does not create settings");
    File.WriteAllText(path,"{\"enabled\":true}");DeveloperToolsSettings.Load(path,warnings.Add);
    Check(DeveloperToolsSettings.Current.Enabled&&!DeveloperToolsSettings.Current.RegressionFixtures,"loaded developer preset omits fixtures");
    var original=File.ReadAllBytes(path);DeveloperToolsSettings.Load(path,warnings.Add);
    Check(original.SequenceEqual(File.ReadAllBytes(path)),"load never rewrites user configuration");
    File.WriteAllText(path,"{\"enabled\":true,\"typo\":true}");DeveloperToolsSettings.Load(path,warnings.Add);
    Check(!DeveloperToolsSettings.Current.Enabled&&warnings.Count==1,"invalid update disables previous enabled state");
    File.WriteAllText(path,new string(' ',5000));DeveloperToolsSettings.Load(path,warnings.Add);
    Check(!DeveloperToolsSettings.Current.Enabled&&warnings.Count==2,"oversize file refused");
    DeveloperToolsSettings.Reset();Check(!DeveloperToolsSettings.Current.Enabled,"shutdown resets process state");
}finally{Directory.Delete(directory,true);}
Console.WriteLine(JsonSerializer.Serialize(new{passed,failed=0,scope="Real developer-options parser/file lifecycle. Does not prove native overlay rendering."}));

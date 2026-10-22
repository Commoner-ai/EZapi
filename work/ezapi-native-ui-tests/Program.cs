using EZapi;
using System.Text;
using System.Text.Json;

int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; }
void Reject(Action action, string name) { bool rejected=false; try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException or ArgumentException) { rejected=true; } Check(rejected,name); }
UiNode Leaf(string id,int width,int height) => new() {Id=id,Kind=UiKind.Spacer,Width=width,Height=height};
NativeUiDocument Doc(UiNode n) => new() {Root=n};
NativeUiDocument Parse(string s) { using var stream=new MemoryStream(Encoding.UTF8.GetBytes(s)); return NativeUiLayout.Parse(stream); }
var row=new UiNode {Id="row",Kind=UiKind.Row,Padding=2,Gap=3,Vertical=UiAlign.Center,Children=new[]{Leaf("short",10,10),Leaf("tall",20,20)}};
var measured=NativeUiLayout.Measure(Doc(row));
Check(measured.Box==new UiBox(0,0,37,24),"row auto-size includes gap and padding");
Check(measured.Children[0].Box==new UiBox(2,7,10,10),"short control is vertically centered against tallest sibling");
Check(measured.Children[1].Box==new UiBox(15,2,20,20),"tall control keeps original logical dimensions");
var column=NativeUiLayout.Measure(Doc(row with{Kind=UiKind.Column,Horizontal=UiAlign.End,Vertical=UiAlign.Start,Width=40}));
Check(column.Children[0].Box==new UiBox(28,2,10,10)&&column.Children[1].Box==new UiBox(18,15,20,20),"column right alignment and vertical spacing");
var frame=NativeUiLayout.Measure(Doc(new(){Id="frame",Kind=UiKind.Frame,Width=51,Height=41,Horizontal=UiAlign.Center,Vertical=UiAlign.Center,Children=new[]{Leaf("inner",10,10)}}));
Check(frame.Children[0].Box==new UiBox(20,15,10,10),"odd remainder uses deterministic integer centering");
var reversed=NativeUiLayout.Measure(Doc(row with{Reverse=true}));
Check(reversed.Children[0].Id=="tall","reverse changes flow order");
Check(NativeUiLayout.Measure(Doc(reversed.Definition)).Children[0].Id=="tall","snapshot can be rebuilt without reversing twice");
var mutable=new List<UiNode>{Leaf("one",10,10)};
var snapshot=NativeUiLayout.Measure(Doc(row with{Children=mutable})); mutable.Clear();
Check(snapshot.Children.Count==1&&snapshot.Definition.Children.Count==1,"snapshot does not retain caller mutable child list");
Check(NativeUiLayout.Measure(Doc(new(){Id="inventory",Kind=UiKind.Inventory,Columns=6,Rows=4})).Box==new UiBox(0,0,96,64),"inventory logical cells are 16 pixels");
foreach(var n in new[]{row with{Width=1},row with{Padding=-1},row with{Gap=65},row with{Width=1025},row with{Kind=(UiKind)99},row with{Horizontal=(UiAlign)99},row with{Id="bad id"},row with{Children=new[]{Leaf("dup",1,1),Leaf("dup",1,1)}},row with{Children=Array.Empty<UiNode>()},Leaf("blank",0,0),(Leaf("text",10,10) with{Text=new("不应在空白节点","invalid")}),new UiNode{Id="label",Kind=UiKind.Label,Width=10,Height=10},new UiNode{Id="label",Kind=UiKind.Label,Width=10,Height=10,Text=new("标签","Label"),FontSize=float.NaN},new UiNode{Id="inv",Kind=UiKind.Inventory,Columns=33},new UiNode{Id="frame",Kind=UiKind.Frame,Children=new[]{Leaf("a",1,1),Leaf("b",1,1)}}})
    Reject(()=>NativeUiLayout.Measure(Doc(n)),"invalid layout rejected: "+n.Id);
var nested=Leaf("end",1,1);for(var i=0;i<14;i++)nested=new(){Id="depth"+i,Kind=UiKind.Frame,Children=new[]{nested}};
Reject(()=>NativeUiLayout.Measure(Doc(nested)),"bounded depth avoids recursive exhaustion");
Reject(()=>NativeUiLayout.Measure(Doc(row with{Children=Enumerable.Range(0,129).Select(i=>Leaf("n"+i,1,1)).ToArray()})),"bounded node count");
const string valid="{\"schemaVersion\":1,\"root\":{\"id\":\"test\",\"kind\":\"Label\",\"width\":80,\"height\":12,\"text\":{\"chinese\":\"测试\",\"english\":\"Test\"}}}";
Check(Parse(valid).Root.Text!.ForLocale("zh")=="测试"&&Parse(valid).Root.Text!.ForLocale("en")=="Test","inline language resolves without changing stable ID");
Check(Parse(valid.Replace("\"root\"","\"ROOT\"")).Root.Id=="test","document field casing is consistent");
foreach(var bad in new[]{valid.Replace("\"schemaVersion\":1","\"schemaVersion\":2"),valid.Replace("\"id\":\"test\"","\"id\":\"test\",\"ID\":\"other\""),valid.Replace("\"kind\":\"Label\"","\"kind\":\"Label\",\"typo\":1"),valid.Replace("\"kind\":\"Label\"","\"kind\":1"),valid.Replace("\"kind\":\"Label\"","\"kind\":\"1\""),valid.Replace("\"kind\":\"Label\",",""),valid.Replace("\"english\":\"Test\"","\"english\":\"Test\",\"Key\":\"unsupported\""),valid.Replace("\"height\":12","\"height\":12,\"fontSize\":25"),"null","[]",new string(' ',65537)})
    Reject(()=>Parse(bad),"strict JSON rejects unknown/duplicate/invalid or oversized content");
using(var bytes=new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(valid)).ToArray())){
    Check(NativeUiLayout.Parse(bytes).Root.Id=="test"&&bytes.CanRead,"BOM supported and caller stream remains open");
}
var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
foreach(var p in Directory.GetFiles(Path.Combine(root,"docs/examples/ui-preview"),"*.json")){
    using var stream=File.OpenRead(p);var data=NativeUiLayout.Parse(stream);Check(NativeUiLayout.Measure(data).Box.Width>0,"distributed recipe parsed: "+Path.GetFileName(p));
}
Console.WriteLine(JsonSerializer.Serialize(new {passed,failed=0,scope="Actual bounded NativeUiLayout parser and integer geometry, shipped recipes; no simulated native font/render acceptance."}));

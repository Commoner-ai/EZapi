using EZapi;
using Il2Cpp;
using System.Text.Json;

var passed=0;
void Check(bool ok,string name) { if(!ok) throw new Exception(name); passed++; }
void Reject(Action action,string name) { try { action(); } catch(Exception) { passed++; return; } throw new Exception(name); }
void Reset()
{
    GameSession.Run=new(5,"run",1);GameLanguage.Current=new("zh");GameBuildFingerprint.IsKnown=true;NativeGuideLocale.IsReady=true;
    ItemApi.Created=ItemApi.Destroyed=0;ItemApi.Factory=id=>new(id);DirectoryMaster.Ids=new(){"z","a","a"};
}
Reset();
var ids=ItemCatalogApi.CaptureIdentifiers();
Check(ids.SequenceEqual(new[]{"a","z"}) && ItemApi.Created==0,"actual public ID capture sorted/deduplicated without factories");
DirectoryMaster.Ids!.Clear();Check(ids.Count==2,"ID capture immutable after native list mutation");
Reject(()=>((IList<string>)ids)[0]="changed","ID snapshot read-only");
DirectoryMaster.Ids=Enumerable.Range(0,1025).Select(n=>"item"+n.ToString("D4")).ToList();
using(var scan=ItemCatalogApi.BeginScan())
{
    Check(scan.Progress.Total==1025 && scan.Progress.Processed==0 && ItemApi.Created==0,"1025 native IDs retained; begin calls no factories");
    Reject(()=>scan.Snapshot(),"partial pool not exposed as complete");
    var first=scan.Step(4,50);Check(first.Processed==4 && first.Accepted==4 && first.Status==ItemCatalogStatus.Reading,"step item budget honored");
    while(scan.Progress.Status==ItemCatalogStatus.Reading) scan.Step(64,50);
    var all=scan.Snapshot();Check(all.Count==1025 && all[^1].Id=="item1024","complete directory scanned without 512/1024 truncation");
    Check(ItemApi.Created==ItemApi.Destroyed && ItemApi.LastOwned==false,"every transient unowned factory instance destroyed");
    Check(all.All(e=>e.BaseValue==151 && e.DisplayName.StartsWith("中文 ") && e.SpriteAtlasPath=="native"),"native metadata preserved");
    Reject(()=>((IList<ItemCatalogEntry>)all)[0]=all[1],"result snapshot is immutable");
    scan.Step();Check(ItemApi.Created==1025,"completed step never repeats factories");
    GameLanguage.Current=new("en");Check(scan.Progress.Status==ItemCatalogStatus.Invalidated,"completed scan invalidated when language changes");
    Reject(()=>scan.Snapshot(),"old-language result inaccessible through invalidated scan");
}
Reset();GameLanguage.Current=new("en");
using(var en=ItemCatalogApi.BeginScan(new[]{"a"})) { en.Step();Check(en.Snapshot()[0].DisplayName=="English a" && en.Progress.LocaleCode=="en","native English names retained"); }
Reset();
using(var bad=ItemCatalogApi.BeginScan(new[]{"a","b","c"}))
{
    ItemApi.Factory=id=>id=="b"?throw new Exception("private data"):new(id);
    bad.Step(10,50);Check(bad.Progress.Status==ItemCatalogStatus.Complete && bad.Snapshot().Count==2 && bad.Failures.Single().Id=="b","one bad factory isolated and counted");
    Check(!bad.Failures[0].Reason.Contains("private data") && bad.Progress.Processed==3,"failure code excludes arbitrary private exception message");
    Check(ItemApi.Created==3 && ItemApi.Destroyed==2,"no blind retry of failed factory");
}
Reset();ItemApi.Factory=id=>new(id){Value=()=>throw new Exception("metadata")};
using(var failure=ItemCatalogApi.BeginScan(new[]{"a"})) { failure.Step();Check(failure.Failures.Count==1 && ItemApi.Destroyed==1,"metadata read error still destroys detached instance"); }
Reset();ItemApi.Factory=id=>new(id){OnDestroy=()=>throw new Exception("cleanup")};
using(var cleanup=ItemCatalogApi.BeginScan(new[]{"a","b"}))
{
    var result=cleanup.Step(10,50);Check(result.Status==ItemCatalogStatus.Failed && ItemApi.Created==1,"cleanup failure stops full scan; no resource leak loop");
    Reject(()=>cleanup.Snapshot(),"cleanup failure never exposes partial catalog");
}
Reset();
using(var stale=ItemCatalogApi.BeginScan())
{
    GameSession.Run=new(5,"run",2);Check(stale.Step().Status==ItemCatalogStatus.Invalidated && ItemApi.Created==0,"same slot/run reload invalidates by epoch before creating");
}
Reset();ItemApi.Factory=id=>{GameSession.Run=new(6,"other",2);return new(id);};
using(var mid=ItemCatalogApi.BeginScan(new[]{"a","b"})) { Check(mid.Step(10,50).Status==ItemCatalogStatus.Invalidated && ItemApi.Created==1 && ItemApi.Destroyed==0,"mid-factory reload aborts and does not dereference stale native wrapper"); }
Reset();ItemApi.Factory=id=>new(id){Name=()=>{GameLanguage.Current=new("en");return "new language";}};
using(var locale=ItemCatalogApi.BeginScan(new[]{"a","b"})) { Check(locale.Step(10,50).Status==ItemCatalogStatus.Invalidated && ItemApi.Destroyed==1,"mid-read language change discards result but still cleans current-run item"); }
Reset();
using(var cancel=ItemCatalogApi.BeginScan()) { cancel.Dispose();Check(cancel.Step().Status==ItemCatalogStatus.Cancelled && ItemApi.Created==0,"cancelled scan never runs factories"); }
Reset();
using(var reentry=ItemCatalogApi.BeginScan(new[]{"a"}))
{
    ItemApi.Factory=id=>{Reject(()=>reentry.Step(),"nested Step rejected");Reject(()=>reentry.Dispose(),"factory cannot dispose active scan");return new(id);};
    Check(reentry.Step().Status==ItemCatalogStatus.Complete,"reentry attempt cannot corrupt outer scan");
}
Reset();ItemApi.Factory=id=>{Thread.Sleep(15);return new(id);};
using(var timed=ItemCatalogApi.BeginScan(new[]{"a","b","c"})) { Check(timed.Step(4,1).Processed==1,"time checked between synchronous factories; first call can overrun"); }
Reset();
using(var budget=ItemCatalogApi.BeginScan())
{
    foreach(var count in new[]{0,-1,65})Reject(()=>budget.Step(count),"invalid item budget rejected");
    foreach(var time in new[]{0d,-1d,double.NaN,double.PositiveInfinity,51d})Reject(()=>budget.Step(4,time),"invalid time budget rejected");
    var worker=Task.Run(()=>{try{budget.Step();return false;}catch(InvalidOperationException){return true;}}).Result;
    Check(worker && ItemApi.Created==0,"worker-thread native scan rejected before factory");
}
Reset();DirectoryMaster.Ids=null;Reject(()=>ItemCatalogApi.CaptureIdentifiers(),"unavailable directory rejected");
Reset();DirectoryMaster.Ids=new();Reject(()=>ItemCatalogApi.CaptureIdentifiers(),"empty not-ready catalog rejected");
Reset();DirectoryMaster.Ids=new(){"good","bad\n"};Reject(()=>ItemCatalogApi.CaptureIdentifiers(),"invalid native ID rejects full capture");
Reset();DirectoryMaster.Ids=Enumerable.Range(0,65537).Select(n=>"id"+n).ToList();Reject(()=>ItemCatalogApi.CaptureIdentifiers(),"safety ceiling rejects full directory without truncation");
Reset();GameBuildFingerprint.IsKnown=false;Reject(()=>ItemCatalogApi.BeginScan(),"unknown game build rejected");
Reset();GameLanguage.Current=new("en",true);Reject(()=>ItemCatalogApi.BeginScan(),"fallback language cannot be claimed as actual game locale");
Reset();NativeGuideLocale.IsReady=false;
Reject(()=>ItemCatalogApi.BeginScan(),"locale code alone does not authorize factories while tables are loading");
Check(ItemApi.Created==0,"pending locale rejects before any factory side effects");
Reset();var subset=new[]{"a","a","b"};using(var copied=ItemCatalogApi.BeginScan(subset)){subset[0]="x";copied.Step(8,50);Check(copied.Snapshot().Select(x=>x.Id).SequenceEqual(new[]{"a","b"}),"explicit subset copied sorted and deduplicated");}
Reset();using(var empty=ItemCatalogApi.BeginScan(Array.Empty<string>())) { Check(empty.Step().Status==ItemCatalogStatus.Complete && empty.Snapshot().Count==0,"explicit empty subset completes without factory calls"); }
Console.WriteLine(JsonSerializer.Serialize(new{passed,failed=0,scope="real ItemCatalogApi and ItemCatalogScan source with explicit native factory/directory/locale/cleanup stubs; not native gameplay"}));

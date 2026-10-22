using EZapi;
using UnityEngine;
using System.Text.Json;

var cases=new List<(string Name,Action Run)>();
void Add(string name,Action run)=>cases.Add((name,run));
void Check(bool ok){if(!ok)throw new Exception("assertion failed");}
void Near(float value,float expected)=>Check(Math.Abs(value-expected)<0.0001f);
GuideImageRegion Region(int rotation=0)=>GuideImageRegion.Create(256,128,new(32,16,64,32),80,48,8,4,rotation);
void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("invalid geometry accepted");}
void Reset(){RecipeGuideImages.Clear();NativeGuideLocale.Stop();Items.OnCreate=null;Texture2D.FailRead=GUIStyle.FailCreate=RenderTexture.FailCreate=false;NativeItemLocalization.CurrentLocaleCode="zh";UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation=new();UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocaleAsync=new();UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase=new();}

Add("initialization invalid pending and failed defer factories without negative cache",()=>{
 var h=UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation;var count=Items.Creates;
 foreach(var mode in new[]{0,1,2}){h.Valid=mode!=0;h.IsDone=mode!=1;h.Status=mode==2?UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Failed:UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded;for(var i=0;i<3;i++)Check(RecipeGuideImages.Warm("real").Thumbnail is null);Check(Items.Creates==count&&RecipeGuideImages.Find("real") is null);}
 h.Valid=h.IsDone=true;h.Status=UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded;Check(RecipeGuideImages.Warm("real").Thumbnail!=null&&Items.Creates==count+1);
});
Add("selected locale operations defer factories until successful",()=>{
 var h=UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocaleAsync;var count=Items.Creates;
 h.Valid=false;Check(RecipeGuideImages.Warm("real").Thumbnail is null);h.Valid=true;h.IsDone=false;Check(RecipeGuideImages.Warm("real").Thumbnail is null);h.IsDone=true;h.Status=UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Failed;Check(RecipeGuideImages.Warm("real").Thumbnail is null&&Items.Creates==count);
 h.Status=UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded;Check(RecipeGuideImages.Warm("real").Thumbnail!=null&&Items.Creates==count+1);
});
Add("StringTable preload invalid pending failed then success retries same ID",()=>{
 var h=UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase!.PreloadOperation;var count=Items.Creates;
 h.Valid=false;Check(RecipeGuideImages.Warm("real").Thumbnail is null);h.Valid=true;h.IsDone=false;Check(RecipeGuideImages.Warm("real").Thumbnail is null);h.IsDone=true;h.Status=UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Failed;Check(RecipeGuideImages.Warm("real").Thumbnail is null&&Items.Creates==count);
 h.Status=UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded;Check(RecipeGuideImages.Warm("real").Thumbnail!=null&&Items.Creates==count+1);
});
Add("missing database and released handles stay observable and recover",()=>{
 var count=Items.Creates;UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase=null;Check(RecipeGuideImages.Warm("real").Thumbnail is null&&NativeGuideLocale.State=="database-unavailable");
 UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase=new();var h=UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase.PreloadOperation;h.Throw=true;Check(RecipeGuideImages.Warm("real").Thumbnail is null&&NativeGuideLocale.Error!.Contains("released handle")&&Items.Creates==count);
 h.Throw=false;Check(RecipeGuideImages.Warm("real").Thumbnail!=null&&NativeGuideLocale.State=="ready"&&NativeGuideLocale.Error is null);
});
Add("locale change releases old thumbnails and pending new locale does not cache fallback",()=>{
 var old=RecipeGuideImages.Warm("real");var texture=old.Thumbnail!;var count=Items.Creates;NativeItemLocalization.CurrentLocaleCode="en";var h=UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation;h.IsDone=false;
 Check(RecipeGuideImages.Warm("real").Thumbnail is null&&texture.Destroyed&&Items.Creates==count&&RecipeGuideImages.Find("real") is null);h.IsDone=true;UnityEngine.Localization.Settings.LocalizationSettings.CompleteLocale("en");Check(RecipeGuideImages.Warm("real").Thumbnail!=null&&Items.Creates==count+1);
});
Add("locale change inside author factory cannot publish old image",()=>{
 Items.OnCreate=_=>NativeItemLocalization.CurrentLocaleCode="en";var old=RecipeGuideImages.Warm("real");Check(old.Thumbnail is null&&old.Error=="EZAPI_GUIDE_IMAGE_STALE");Check(RecipeGuideImages.Find("real") is null);Items.OnCreate=null;UnityEngine.Localization.Settings.LocalizationSettings.CompleteLocale("en");Check(RecipeGuideImages.Warm("real").Thumbnail!=null);
});
Add("completed locale event permits previews without reentering initialization getter",()=>{Check(RecipeGuideImages.Warm("real").Thumbnail!=null);NativeItemLocalization.CurrentLocaleCode="en";var h=UnityEngine.Localization.Settings.LocalizationSettings.InitializationOperation;h.Throw=true;Check(RecipeGuideImages.Warm("real").Thumbnail is null);UnityEngine.Localization.Settings.LocalizationSettings.CompleteLocale("en");Check(RecipeGuideImages.Warm("real").Thumbnail!=null&&NativeGuideLocale.Error is null);});
Add("locale subscription held once and disposed with framework",()=>{var s=UnityEngine.Localization.Settings.LocalizationSettings.Subscriptions;var u=UnityEngine.Localization.Settings.LocalizationSettings.Unsubscriptions;RecipeGuideImages.Warm("real");RecipeGuideImages.Warm("another");Check(UnityEngine.Localization.Settings.LocalizationSettings.Subscriptions==s+1);NativeGuideLocale.Stop();Check(UnityEngine.Localization.Settings.LocalizationSettings.Unsubscriptions==u+1);});

Add("asymmetric atlas UV uses texture dimensions",()=>{var r=Region();Near(r.Uv.X,.125f);Near(r.Uv.Y,.125f);Near(r.Uv.Width,.25f);Near(r.Uv.Height,.25f);});
Add("fit preserves original transparent margins and GUI top origin",()=>{var b=Region().Fit(new(10,20,160,160));Near(b.X,26);Near(b.Y,76);Near(b.Width,128);Near(b.Height,64);});
Add("portrait target letterboxes full sprite",()=>{var b=Region().Fit(new(0,0,80,200));Near(b.X,8);Near(b.Y,88);Near(b.Width,64);Near(b.Height,32);});
foreach(var rotation in new[]{1,2,3}){var captured=rotation;Add("packed flip "+rotation,()=>{var r=Region(captured);var flipX=captured is 1 or 3;var flipY=captured is 2 or 3;Near(r.Uv.X,flipX?.375f:.125f);Near(r.Uv.Y,flipY?.375f:.125f);Near(r.Uv.Width,flipX?-.25f:.25f);Near(r.Uv.Height,flipY?-.25f:.25f);Check(r.Fit(new(0,0,160,160))==Region().Fit(new(0,0,160,160)));});}
Add("invalid dimensions crop margins rotation rejected",()=>{
 foreach(var v in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,0,-1}){
  Reject(()=>GuideImageRegion.Create(v,128,new(0,0,64,32),64,32,0,0,0));
  Reject(()=>GuideImageRegion.Create(256,128,new(0,0,v,32),64,32,0,0,0));
  Reject(()=>Region().Fit(new(0,0,v,10)));
 }
 Reject(()=>GuideImageRegion.Create(10,10,new(9,0,2,1),2,1,0,0,0));
 Reject(()=>GuideImageRegion.Create(10,10,new(-1,0,2,1),2,1,0,0,0));
 Reject(()=>GuideImageRegion.Create(10,10,new(0,0,2,1),2,1,1,0,0));
 Reject(()=>GuideImageRegion.Create(10,10,new(0,0,2,1),2,1,0,float.NaN,0));
 foreach(var rotation in new[]{-1,4,15})Reject(()=>Region(rotation));
});
Add("randomized crops remain within target and preserve aspect",()=>{
 var random=new Random(5801);
 for(var i=0;i<1000;i++){
  float width=random.Next(1,100),height=random.Next(1,100),left=random.Next(20),bottom=random.Next(20),fullWidth=width+left+random.Next(20),fullHeight=height+bottom+random.Next(20);
  var r=GuideImageRegion.Create(256,256,new(10,10,width,height),fullWidth,fullHeight,left,bottom,i%4);
  var box=new GuideImageBox(17,29,random.Next(1,600),random.Next(1,600));var fit=r.Fit(box);
  Check(fit.X>=box.X-.001&&fit.Y>=box.Y-.001&&fit.X+fit.Width<=box.X+box.Width+.001&&fit.Y+fit.Height<=box.Y+box.Height+.001);
  Near(fit.Width/fit.Height,width/height);
 }
});
Add("cache hit and draw never call factory again",()=>{var p=RecipeGuideImages.Warm("real");var before=Items.Creates;for(var i=0;i<50;i++){Check(ReferenceEquals(p,RecipeGuideImages.Warm("real")));Check(RecipeGuideImages.Draw("real",new(0,0,76,76)));}Check(Items.Creates==before&&Items.Created.Last().Destroyed);});
Add("negative cache never retries missing ID each frame",()=>{var before=Items.Creates;var p=RecipeGuideImages.Warm("missing.id");Check(p.Error is not null);for(var i=0;i<20;i++)Check(ReferenceEquals(p,RecipeGuideImages.Warm("missing.id")));Check(Items.Creates==before);});
Add("LRU retains touched entry and frees oldest owned thumbnail",()=>{var first=RecipeGuideImages.Warm("first");var second=RecipeGuideImages.Warm("second");var secondTexture=second.Thumbnail!;for(var i=0;i<126;i++)RecipeGuideImages.Warm("id"+i);RecipeGuideImages.Find("first");RecipeGuideImages.Warm("overflow");Check(RecipeGuideImages.Find("first")==first&&RecipeGuideImages.Find("second") is null&&secondTexture.Destroyed);Check(!first.Sprite!.Destroyed&&!second.Texture!.Destroyed);});
Add("clear destroys thumbnails but preserves shared sprites and textures",()=>{var p=RecipeGuideImages.Warm("real");var thumbnail=p.Thumbnail!;RecipeGuideImages.Clear();Check(thumbnail.Destroyed&&!p.Sprite!.Destroyed&&!p.Texture!.Destroyed&&RecipeGuideImages.Find("real") is null);});
Add("run change invalidates old native preview",()=>{var p=RecipeGuideImages.Warm("real");var thumbnail=p.Thumbnail!;NativeAccess.Current=new(NativeAccess.Current.Epoch+1);Check(RecipeGuideImages.Find("real") is null&&thumbnail.Destroyed);});
Add("style creation failure reclaims successful thumbnail",()=>{GUIStyle.FailCreate=true;var before=UnityEngine.Object.DestroyedObjects.Count;var p=RecipeGuideImages.Warm("badstyle");Check(p.Error is not null&&p.Thumbnail is null&&Items.Created.Last().Destroyed);Check(UnityEngine.Object.DestroyedObjects.Skip(before).OfType<Texture2D>().Count()==2);});
Add("GPU failure restores previous render target and reclaims partial resources",()=>{var previous=new RenderTexture(4,4,0,RenderTextureFormat.ARGB32);RenderTexture.active=previous;Texture2D.FailRead=true;var before=UnityEngine.Object.DestroyedObjects.Count;Check(RecipeGuideImages.Warm("gpu-fault").Error is not null&&RenderTexture.active==previous);var destroyed=UnityEngine.Object.DestroyedObjects.Skip(before).ToArray();Check(destroyed.OfType<RenderTexture>().Single().Released&&destroyed.OfType<Texture2D>().Count()==1);});
Add("render target refusal still releases its allocation",()=>{RenderTexture.FailCreate=true;var before=UnityEngine.Object.DestroyedObjects.Count;Check(RecipeGuideImages.Warm("target-fault").Error is not null);Check(UnityEngine.Object.DestroyedObjects.Skip(before).OfType<RenderTexture>().Single().Released);});
Add("temporary item cleanup failure isolates error and frees thumbnail",()=>{Items.OnCreate=item=>item.FailDestroy=true;var p=RecipeGuideImages.Warm("cleanup-fault");Check(p.Error!.StartsWith("EZAPI_GUIDE_IMAGE_ITEM_CLEANUP")&&p.Thumbnail is null);});
Add("factory returning attached object is not destroyed",()=>{Items.OnCreate=item=>item.parentInventory=new();Check(RecipeGuideImages.Warm("badfactory").Error is not null&&!Items.Created.Last().Destroyed);});
Add("reentrant factory cannot recursively create same ID",()=>{RecipeGuideImages.Preview? nested=null;Items.OnCreate=_=>nested=RecipeGuideImages.Warm("recursive");var before=Items.Creates;Check(RecipeGuideImages.Warm("recursive").Error is null&&nested?.Error=="EZAPI_GUIDE_IMAGE_REENTRANT"&&Items.Creates==before+1);});
Add("factory-triggered cache reset cannot publish stale thumbnail",()=>{Items.OnCreate=_=>RecipeGuideImages.Clear();var p=RecipeGuideImages.Warm("reset");Check(p.Error=="EZAPI_GUIDE_IMAGE_STALE"&&p.Thumbnail is null&&RecipeGuideImages.Find("reset") is null&&Items.Created.Last().Destroyed);});
Add("factory run change never destroys an old-run item",()=>{Items.OnCreate=_=>NativeAccess.Current=new(NativeAccess.Current.Epoch+1);Check(RecipeGuideImages.Warm("switch").Error=="EZAPI_GUIDE_IMAGE_STALE"&&!Items.Created.Last().Destroyed);});
Add("thumbnail dimension cap preserves aspect ratio",()=>{var source=new Texture2D(1024,1024);var region=GuideImageRegion.Create(1024,1024,new(0,0,800,400),800,400,0,0,0);var thumb=RecipeGuideImages.CreateThumbnail(source,region);Check(thumb.width==128&&thumb.height==64);UnityEngine.Object.Destroy(thumb);});
// Independent small-grid oracles exercise UV direction, missing triangles and transparent margins.
GuideImagePoint[] Quad()=>new[]{new GuideImagePoint(0,0),new GuideImagePoint(4,0),new GuideImagePoint(4,4),new GuideImagePoint(0,4)};
GuideImagePoint[] FullUv()=>new[]{new GuideImagePoint(0,0),new GuideImagePoint(1,0),new GuideImagePoint(1,1),new GuideImagePoint(0,1)};
ushort[] QuadTriangles()=>new ushort[]{0,1,2,0,2,3};
GuideImageMesh Mesh(GuideImagePoint[]? vertices=null,GuideImagePoint[]? uv=null,ushort[]? triangles=null)=>GuideImageMesh.Create(4,4,4,4,0,0,1,vertices??Quad(),uv??FullUv(),triangles??QuadTriangles());
Add("mesh quad exact identity pixel mapping",()=>Check(Mesh().Samples.SequenceEqual(Enumerable.Range(0,16))));
Add("mesh triangle masks neighboring atlas content",()=>{
 var mesh=Mesh(new[]{new GuideImagePoint(0,0),new GuideImagePoint(4,0),new GuideImagePoint(0,4)},new[]{new GuideImagePoint(0,0),new GuideImagePoint(1,0),new GuideImagePoint(0,1)},new ushort[]{0,1,2});
 for(var y=0;y<4;y++)for(var x=0;x<4;x++)Check(mesh.Samples[y*4+x]==(x+y<=3?y*4+x:-1));
});
Add("mesh holes remain transparent even with opaque atlas neighbors",()=>{
 var mesh=Mesh(triangles:new ushort[]{0,1,2});Check(mesh.Samples[12]==-1&&mesh.Samples[3]==3&&mesh.Samples[15]==15);
});
Add("mesh winding does not change texture mapping",()=>Check(Mesh(triangles:new ushort[]{2,1,0,3,2,0}).Samples.SequenceEqual(Mesh().Samples)));
foreach(var rotation in new[]{0,1,2,3}){var turn=rotation;Add("mesh arbitrary UV quarter turn "+turn,()=>{
 var uv=FullUv().Select(p=>turn switch{1=>new GuideImagePoint(1-p.Y,p.X),2=>new(1-p.X,1-p.Y),3=>new(p.Y,1-p.X),_=>p}).ToArray();var mesh=Mesh(uv:uv);
 for(var y=0;y<4;y++)for(var x=0;x<4;x++){
  var (sx,sy)=turn switch{1=>(3-y,x),2=>(3-x,3-y),3=>(y,3-x),_=>(x,y)};Check(mesh.Samples[y*4+x]==sy*4+sx);
 }
});}
Add("mesh cropped UV and pivot preserve original transparent margins",()=>{
 var v=Quad().Select(p=>new GuideImagePoint(p.X/2-1,p.Y/2-1)).ToArray();
 var uv=FullUv().Select(p=>new GuideImagePoint((p.X*4+2)/16,(p.Y*4+1)/8)).ToArray();
 var mesh=GuideImageMesh.Create(16,8,8,8,2,3,2,v,uv,QuadTriangles());
 Check(mesh.Crop==new GuideImageBox(2,1,4,4)&&mesh.Width==8&&mesh.Height==8);
 for(var y=0;y<8;y++)for(var x=0;x<8;x++)Check(mesh.Samples[y*8+x]==(x<4&&y>=1&&y<5?(y-1)*4+x:-1));
});
Add("mesh downsampling bounds both CPU and GPU allocations",()=>{
 var mesh=GuideImageMesh.Create(16384,8192,2048,1024,0,0,512,Quad().Select(p=>new GuideImagePoint(p.X,p.Y/2)).ToArray(),FullUv(),QuadTriangles());
 Check(mesh.Width==128&&mesh.Height==64&&mesh.ReadWidth==512&&mesh.ReadHeight==256&&mesh.Samples.All(i=>i>=0&&i<512*256));
});
Add("mesh nonfinite geometry invalid UV indices and empty triangles rejected",()=>{
 foreach(var value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity}){
  var v=Quad();v[0]=new(value,0);Reject(()=>Mesh(vertices:v));var uv=FullUv();uv[0]=new(0,value);Reject(()=>Mesh(uv:uv));
 }
 Reject(()=>Mesh(uv:new[]{new GuideImagePoint(-1,0),new(1,0),new(1,1),new(0,1)}));
 Reject(()=>Mesh(triangles:new ushort[]{0,1,9}));Reject(()=>Mesh(triangles:new ushort[]{0,1}));Reject(()=>Mesh(triangles:new ushort[]{0,0,0}));
 Reject(()=>Mesh(vertices:new[]{new GuideImagePoint(0,0)}));Reject(()=>Mesh(vertices:Quad().Select(p=>new GuideImagePoint(p.X+4,p.Y)).ToArray()));
 Reject(()=>GuideImageMesh.Create(0,4,4,4,0,0,1,Quad(),FullUv(),QuadTriangles()));
});
Add("mesh worst-case overlapping triangles stop at raster budget",()=>Reject(()=>GuideImageMesh.Create(128,128,128,128,0,0,32,Quad(),FullUv(),Enumerable.Repeat(new ushort[]{0,1,2},300).SelectMany(x=>x).ToArray())));
Add("mesh samples are copied before caller mutates source arrays",()=>{
 var v=Quad();var uv=FullUv();var t=QuadTriangles();var mesh=Mesh(v,uv,t);v[0]=new(999,999);uv[0]=new(999,999);t[0]=65000;Check(mesh.Samples.SequenceEqual(Enumerable.Range(0,16)));
});
var results=cases.Select(test=>{Reset();try{test.Run();return new{name=test.Name,passed=true,error=(string?)null};}catch(Exception e){return new{name=test.Name,passed=false,error=(string?)e.ToString()};}finally{Reset();}}).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new{suite="recipe-guide-images",passed=results.Count(r=>r.passed),failed=results.Count(r=>!r.passed),cases=results}));
return results.Any(r=>!r.passed)?1:0;

using EZapi;
using Il2Cpp;
using UnityEngine;
using System.Text.Json;

var passed=0;
void Check(bool yes,string label) {if(!yes)throw new Exception(label);passed++;}
void Reject(Action action,string label) {try{action();}catch(ArgumentException){passed++;return;}catch(ArtworkLimitException){passed++;return;}throw new Exception(label);}
byte[] Bytes(int n,byte rgb,byte alpha=255)=>Enumerable.Range(0,n).SelectMany(_=>new[]{rgb,rgb,rgb,alpha}).ToArray();
var basic=ArtworkPixels.AnalyzeRgba(2,2,Bytes(4,0));Check(basic.Content==ArtworkContent.Dark&&basic.VisiblePixels==4&&basic.DetailPixels==0,"opaque black statistics");
Check(ArtworkPixels.AnalyzeRgba(2,2,Bytes(4,255,0)).Content==ArtworkContent.Transparent,"transparent RGB never counts as content");
Check(ArtworkPixels.AnalyzeRgba(2,2,Bytes(4,9)).Content==ArtworkContent.Artwork,"dark visible artwork does not require a bright average");
Check(ArtworkPixels.AnalyzeRgba(2,2,Bytes(4,8)).Content==ArtworkContent.Dark,"channel threshold is exclusive");
Check(ArtworkPixels.AnalyzeRgba(1,1,Bytes(1,255)).Content==ArtworkContent.Dark,"single stray texel rejected by default");
Check(ArtworkPixels.AnalyzeRgba(1,1,Bytes(1,255),new(){MinimumDetailPixels=1}).Content==ArtworkContent.Artwork,"author can permit tiny valid art");
foreach(var alpha in new byte[]{0,15,16,255})Check(ArtworkPixels.AnalyzeRgba(2,2,Bytes(4,255,alpha)).VisiblePixels==(alpha>=16?4:0),"alpha boundary");
var sparse=Bytes(1000,0);sparse[0]=255;sparse[4]=255;
Check(ArtworkPixels.AnalyzeRgba(100,10,sparse).RequiredDetailPixels==10&&ArtworkPixels.AnalyzeRgba(100,10,sparse).Content==ArtworkContent.Dark,"detail fraction uses visible denominator");
for(var i=2;i<10;i++)sparse[i*4]=255;
Check(ArtworkPixels.AnalyzeRgba(100,10,sparse).Content==ArtworkContent.Artwork,"minimum fraction exact boundary accepted");
foreach(var options in new ArtworkOptions[]{new(){MinimumAlpha=0},new(){MinimumDetailPixels=0},new(){MaximumPixels=0},new(){MaximumPixels=1048577},new(){MinimumDetailFraction=double.NaN},new(){MinimumDetailFraction=double.PositiveInfinity},new(){MinimumDetailFraction=-0.1},new(){MinimumDetailFraction=1.1}})
 Reject(()=>ArtworkPixels.AnalyzeRgba(1,1,Bytes(1,0),options),"invalid pure options");
Reject(()=>ArtworkPixels.AnalyzeRgba(2,2,new byte[15]),"short RGBA rejected");Reject(()=>ArtworkPixels.AnalyzeRgba(0,1,Array.Empty<byte>()),"invalid dimensions rejected");
Reject(()=>ArtworkPixels.AnalyzeRgba(int.MaxValue,int.MaxValue,Array.Empty<byte>()),"overflow dimensions rejected before allocation");
Check(Task.Run(()=>ArtworkPixels.AnalyzeRgba(2,2,Bytes(4,100))).Result.Content==ArtworkContent.Artwork,"pure bytes work off Unity thread");

Sprite Reset(bool gpu=false,int size=2)
{
    Boundary.Failures.Clear();Boundary.Actions.Clear();QualitySettings.activeColorSpace=ColorSpace.Gamma;SystemInfo.supportsRenderTextures=true;Graphics.Hook=null;
    GL.Reset(true);RenderTexture.Reset(new(1,1));GameSession.Run=new(2,"test",1);GameBuildFingerprint.IsKnown=true;RenderHandler.Hook=null;RenderHandler.Calls=0;
    var texture=new Texture2D(size,size){isReadable=!gpu};Array.Fill(texture.Pixels,new Color32(255,0,0,255));
    var sprite=new Sprite{texture=texture,Rectangle=new(0,0,size,size)};RenderHandler.Sprite=sprite;Boundary.Actions.Clear();return sprite;
}
var sprite=Reset();var result=SpriteArtworkApi.Inspect(sprite);
Check(result.Status==SpriteArtworkStatus.Inspected&&result.Statistics!.Content==ArtworkContent.Artwork&&result.ReadPath==ArtworkReadPath.ReadableTexture,"actual adapter readable path");
Check(!Boundary.Actions.Contains("allocate")&&!sprite.Destroyed&&!sprite.texture.Destroyed,"readable path never creates GPU objects or destroys source");
sprite=Reset(true);result=SpriteArtworkApi.Inspect(sprite);
Check(result.Status==SpriteArtworkStatus.Inspected&&result.ReadPath==ArtworkReadPath.GpuReadback,"unreadable texture uses native GPU crop read");
Check(ReferenceEquals(RenderTexture.active,RenderTexture.Previous)&&GL.sRGBWrite&&RenderTexture.Last!.Released&&Boundary.Actions.Contains("destroy"),"GPU globals restored and temporaries released");
Check(!sprite.Destroyed&&!sprite.texture.Destroyed,"GPU path leaves external resources owned by caller");
sprite=Reset(true);result=SpriteArtworkApi.Inspect(sprite,new(){AllowGpuReadback=false});Check(result.Status==SpriteArtworkStatus.Unavailable&&result.Statistics is null&&!Boundary.Actions.Contains("allocate"),"disabled GPU is unavailable, not empty evidence");
sprite=Reset(true);SystemInfo.supportsRenderTextures=false;Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Unavailable&&!Boundary.Actions.Contains("allocate"),"unavailable GPU rejected before allocation");
sprite=Reset();sprite.texture.ThrowRead=true;Check(SpriteArtworkApi.Inspect(sprite).ReadPath==ArtworkReadPath.GpuReadback,"readable compressed format failure falls back to GPU");
sprite=Reset();sprite.texture.WrongCount=true;Check(SpriteArtworkApi.Inspect(sprite).ReadPath==ArtworkReadPath.GpuReadback,"incomplete native pixel array never used as complete crop");
foreach(var gpu in new[]{false,true})
{
    sprite=Reset(gpu,4);sprite.Rectangle=new(1,1,2,2);sprite.uv=new[]{new Vector2(.25f,.25f),new(.75f,.25f),new(.75f,.75f),new(.25f,.75f)};
    for(var y=1;y<3;y++)for(var x=1;x<3;x++)sprite.texture.Pixels[y*4+x]=new(0,0,0,255);
    result=SpriteArtworkApi.Inspect(sprite);
    Check(result.Status==SpriteArtworkStatus.Inspected&&result.Statistics!.Content==ArtworkContent.Dark&&result.Statistics.SampledPixels==4&&result.Crop==new ArtworkCrop(1,1,2,2),"atlas neighbours excluded on both read paths");
}
foreach(var gpu in new[]{false,true})
{
    sprite=Reset(gpu,4);Array.Fill(sprite.texture.Pixels,new Color32(255,255,255,255));
    sprite.packed=true;sprite.packingMode=SpritePackingMode.Tight;sprite.uv=new[]{new Vector2(0,0),new(.5f,0),new(0,.5f)};sprite.triangles=new ushort[]{0,1,2};
    sprite.texture.Pixels[0]=sprite.texture.Pixels[1]=sprite.texture.Pixels[4]=new(0,0,0,255);
    result=SpriteArtworkApi.Inspect(sprite,new(){MinimumDetailPixels=1});
    Check(result.Status==SpriteArtworkStatus.Inspected&&result.Statistics!.SampledPixels==3&&result.Statistics.DetailPixels==0&&!Boundary.Actions.Contains("texture-rect"),"tight UV triangle excludes bright packed neighbour outside mesh");
    sprite.triangles=new ushort[]{2,1,0};Check(SpriteArtworkApi.Inspect(sprite,new(){MinimumDetailPixels=1}).Statistics==result.Statistics,"reversed/rotated UV winding does not change sampled pixels");
}
sprite=Reset();sprite.associatedAlphaSplitTexture=new(2,2);Array.Fill(sprite.associatedAlphaSplitTexture.Pixels,new Color32(0,255,255,255));
Check(SpriteArtworkApi.Inspect(sprite).Statistics!.Content==ArtworkContent.Transparent,"split alpha uses raw red channel instead of source color alpha");
sprite=Reset();sprite.associatedAlphaSplitTexture=new(2,2){isReadable=false,isDataSRGB=false};Array.Fill(sprite.associatedAlphaSplitTexture.Pixels,new Color32(128,0,0,255));
result=SpriteArtworkApi.Inspect(sprite,new(){MinimumAlpha=128});Check(result.Statistics!.VisiblePixels==4&&result.ReadPath==ArtworkReadPath.Mixed,"split alpha GPU read supports mixed path and exact opacity threshold");
sprite.associatedAlphaSplitTexture=new(1,1);Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Unavailable,"mismatched split alpha dimensions never silently ignored");
foreach(var srgb in new[]{false,true})
{
    sprite=Reset();QualitySettings.activeColorSpace=ColorSpace.Linear;sprite.texture.isDataSRGB=srgb;Array.Fill(sprite.texture.Pixels,new Color32(16,16,16,255));
    var cpu=SpriteArtworkApi.Inspect(sprite,new(){MaximumDarkChannel=40});sprite.texture.isReadable=false;var gpu=SpriteArtworkApi.Inspect(sprite,new(){MaximumDarkChannel=40});
    Check(cpu.Statistics==gpu.Statistics&&cpu.Status==SpriteArtworkStatus.Inspected,"CPU and modeled GPU color-space paths agree for SRGB/linear data");
    Check(GL.sRGBWrite&&ReferenceEquals(RenderTexture.active,RenderTexture.Previous),"linear project restores caller render globals");
}
foreach(var phase in new[]{"allocate","blit","set-target","create-texture","read-pixels","gpu-pixels"})
{
    sprite=Reset(true);Boundary.Failures.Add(phase);result=SpriteArtworkApi.Inspect(sprite);
    Check(result.Status==SpriteArtworkStatus.Unavailable&&result.Statistics is null,"GPU phase failure never becomes black/transparent evidence");
    Check(GL.sRGBWrite&&ReferenceEquals(RenderTexture.active,RenderTexture.Previous),"GPU failure still restores caller globals");
    Check(RenderTexture.Last is null||RenderTexture.Last.Released,"allocated temporary released after read failure");
}
foreach(var phase in new[]{"restore-target","restore-srgb","destroy","release"})
{
    sprite=Reset(true);Boundary.Failures.Add(phase);result=SpriteArtworkApi.Inspect(sprite);
    Check(result.Status==SpriteArtworkStatus.CleanupFailed&&result.Statistics is null,"cleanup failure blocks otherwise successful content result");
    Check(new[]{"restore-target","restore-srgb","destroy","release"}.All(Boundary.Actions.Contains),"independent cleanup attempts all run");
    Check(!sprite.Destroyed&&!sprite.texture.Destroyed,"cleanup never destroys caller sprite or texture");
}
sprite=Reset();Check(SpriteArtworkApi.Inspect(sprite,new(){MaximumPixels=3}).Status==SpriteArtworkStatus.LimitExceeded&&!Boundary.Actions.Contains("cpu-read"),"pixel ceiling rejects before texture read");
sprite=Reset();sprite.Rectangle=new(.5f,0,2,2);Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Invalid,"nonintegral native crop rejected");
sprite=Reset();sprite.Rectangle=new(0,0,1,1);Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Invalid,"UV and crop mismatch cannot publish partial sprite evidence");
sprite=Reset();sprite.uv[0]=new(float.NaN,0);Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Invalid,"nonfinite UV rejected");
sprite=Reset();sprite.triangles=new ushort[]{0,1,99};Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Invalid,"invalid triangle index rejected");
sprite=Reset();sprite.triangles=new ushort[]{0,0,0};Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Invalid,"degenerate mesh not reported as empty artwork");
sprite=Reset();sprite.uv=new Vector2[1025];Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.LimitExceeded,"native UV count bounded before copy");
sprite=Reset(true);SpriteArtworkResult? nested=null;Graphics.Hook=()=>nested=SpriteArtworkApi.Inspect(sprite);
Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Inspected&&nested!.Reason!.Contains("REENTRY"),"native callback cannot recurse GPU inspection");
sprite=Reset();sprite.texture.OnRead=()=>sprite.texture=new(2,2);Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Unavailable,"source replacement during read invalidates evidence");
sprite=Reset();sprite.associatedAlphaSplitTexture=new(2,2);sprite.texture.OnRead=()=>sprite.associatedAlphaSplitTexture.width=1;
Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Unavailable,"resized split-alpha texture invalidates captured geometry");
sprite=Reset();sprite.texture.OnRead=()=>sprite.Pointer=new(99);Check(SpriteArtworkApi.Inspect(sprite).Status==SpriteArtworkStatus.Unavailable,"sprite identity change invalidates captured evidence");
sprite=Reset();Check(Task.Run(()=>SpriteArtworkApi.Inspect(sprite)).Result.Reason!.Contains("THREAD")&&!Boundary.Actions.Contains("cpu-read"),"worker denied before native texture read");
sprite=Reset();var item=new GameItem();Check(SpriteArtworkApi.InspectItem(item).Status==SpriteArtworkStatus.Inspected&&RenderHandler.Calls==1,"native raw sprite resolver used once without item factory");
sprite=Reset();RenderHandler.Sprite=null;Check(SpriteArtworkApi.InspectItem(item).Reason!.Contains("MISSING"),"missing raw sprite does not use native UNKNOWN fallback");
sprite=Reset();RenderHandler.Hook=()=>GameSession.Run=new(2,"test",2);Check(SpriteArtworkApi.InspectItem(item).Reason!.Contains("STALE")&&!Boundary.Actions.Contains("cpu-read"),"reload during resolver stops before old item pixels");
sprite=Reset();RenderHandler.Hook=()=>nested=SpriteArtworkApi.InspectItem(item);Check(SpriteArtworkApi.InspectItem(item).Status==SpriteArtworkStatus.Inspected&&nested!.Reason!.Contains("REENTRY")&&RenderHandler.Calls==1,"resolver reentry bounded before recursion");
sprite=Reset();GameBuildFingerprint.IsKnown=false;Check(SpriteArtworkApi.InspectItem(item).Reason!.Contains("BUILD")&&RenderHandler.Calls==0,"unknown game build denies item resolver");
sprite=Reset();Check(SpriteArtworkApi.InspectItem(item,new(){MaximumPixels=0}).Status==SpriteArtworkStatus.Invalid&&RenderHandler.Calls==0,"invalid options rejected before native resolver");
sprite=Reset();GameSession.Run=null;Check(SpriteArtworkApi.InspectItem(item).Status==SpriteArtworkStatus.Unavailable&&RenderHandler.Calls==0,"unloaded item run denied");
var meshUv=new float[]{0,0,1,0,1,1,0,1};var repeated=Enumerable.Range(0,30).SelectMany(_=>new ushort[]{0,1,2,0,2,3}).ToArray();
Reject(()=>ArtworkMesh.Build(1024,1024,meshUv,repeated,null,new()),"repeated triangle raster work budget rejects full result");
Console.WriteLine(JsonSerializer.Serialize(new{passed,failed=0,scope="real ArtworkPixels/ArtworkMesh/SpriteArtworkApi source with instrumented Unity texture/render/cleanup stand-ins; no native GPU/color-space/art or game acceptance"}));

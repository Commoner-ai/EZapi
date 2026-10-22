using UnityEngine;
namespace EZapi;

/// <summary>仅资产检查按钮触发。真实 GPU 非对称图集裁切/方向/透明度、目录解析与缓存寿命；不挂销毁探针。</summary>
internal static class DebugGuideImages
{
    internal static void Run(Action<bool,string> check)
    {
        Texture2D? source=null;
        try {
            RecipeGuideImages.Clear();
            var run=NativeAccess.RequireRun();var previous=RenderTexture.active;
            source=new Texture2D(8,8,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            Color Pixel(int x,int y)=>x<2?(y<2?new(1,0,0,1):new(0,0,1,1)):(y<2?new(0,1,0,1):new(1,1,0,.5f));
            for(var y=0;y<8;y++)for(var x=0;x<8;x++)source.SetPixel(x,y,x>=2&&x<6&&y>=1&&y<5?Pixel(x-2,y-1):new(1,0,1,1));
            source.Apply(false,true);check(!source.isReadable,"guide source CPU pixels discarded; thumbnail uses GPU readback");
            foreach(var rotation in new[]{0,1,2,3}) {
                Texture2D? thumbnail=null;Texture2D? readback=null;
                try {
                    var region=GuideImageRegion.Create(8,8,new(2,1,4,4),6,7,1,2,rotation);
                    thumbnail=RecipeGuideImages.CreateThumbnail(source,region);
                    check(thumbnail.width==4&&thumbnail.height==4&&!thumbnail.isReadable,"guide crop bounded and CPU copy discarded: "+rotation);
                    readback=Read(thumbnail);var ok=true;
                    foreach(var y in new[]{0,3})foreach(var x in new[]{0,3}){
                        var expected=Pixel(rotation is 1 or 3?3-x:x,rotation is 2 or 3?3-y:y);var actual=readback.GetPixel(x,y,0);
                        ok&=Math.Abs(actual.r-expected.r)<.06&&Math.Abs(actual.g-expected.g)<.06&&Math.Abs(actual.b-expected.b)<.06&&Math.Abs(actual.a-expected.a)<.06;
                    }
                    check(ok,"guide GPU crop excludes neighboring magenta, preserves orientation/alpha: "+rotation);
                    check(RenderTexture.active?.Pointer==previous?.Pointer,"guide render target restored: "+rotation);
                }finally{if(readback!=null)UnityEngine.Object.Destroy(readback);if(thumbnail!=null)UnityEngine.Object.Destroy(thumbnail);}
            }
            // 实际GPU读回与独立逐像素期望：UV四分之一转向、紧密三角形和完整透明边距。
            foreach(var turn in new[]{0,1,2,3})foreach(var tight in new[]{false,true}) {
                Texture2D? thumbnail=null;Texture2D? readback=null;
                try {
                    var v=new[]{new GuideImagePoint(1,2),new(5,2),new(5,6),new(1,6)};
                    var uv=new[]{new GuideImagePoint(0,0),new(1,0),new(1,1),new(0,1)}.Select(p=> {
                        var q=turn switch{1=>new GuideImagePoint(1-p.Y,p.X),2=>new(1-p.X,1-p.Y),3=>new(p.Y,1-p.X),_=>p};
                        return new GuideImagePoint((2+q.X*4)/8,(1+q.Y*4)/8);
                    }).ToArray();
                    var indices=tight?new ushort[]{0,1,3}:new ushort[]{0,1,2,0,2,3};
                    var mesh=GuideImageMesh.Create(8,8,6,7,0,0,1,v,uv,indices);
                    thumbnail=RecipeGuideImages.CreateMeshThumbnail(source,mesh);readback=Read(thumbnail);
                    var ok=true;
                    for(var y=0;y<7;y++)for(var x=0;x<6;x++) {
                        var px=x-1;var py=y-2;var inside=px>=0&&px<4&&py>=0&&py<4&&(!tight||px+py<=3);
                        var (sx,sy)=turn switch{1=>(3-py,px),2=>(3-px,3-py),3=>(py,3-px),_=>(px,py)};
                        var expected=inside?Pixel(sx,sy):new Color(0,0,0,0);var actual=readback.GetPixel(x,y,0);
                        ok&=Math.Abs(actual.r-expected.r)<.06&&Math.Abs(actual.g-expected.g)<.06&&Math.Abs(actual.b-expected.b)<.06&&Math.Abs(actual.a-expected.a)<.06;
                    }
                    check(ok,"mesh GPU all pixels preserve rotated UV, triangle mask and margins: "+turn+"/"+tight);
                    check(!thumbnail.isReadable&&thumbnail.width==6&&thumbnail.height==7&&RenderTexture.active?.Pointer==previous?.Pointer,"mesh bounded GPU resources and active render restored: "+turn+"/"+tight);
                }finally{if(readback!=null)UnityEngine.Object.Destroy(readback);if(thumbnail!=null)UnityEngine.Object.Destroy(thumbnail);}
            }
            Sprite? nativeSprite=null;Texture2D? nativeThumbnail=null;Texture2D? nativeReadback=null;Texture2D? nativeSource=null;
            try {
                // Unity只允许在可读纹理上OverrideGeometry；先建立真实网格，再丢弃CPU像素。
                // 使用自有GPU读回副本，绝不为测试修改共享源纹理。
                nativeSource=Read(source);
                nativeSprite=Sprite.Create(nativeSource,new Rect(2,1,4,4),new Vector2(.25f,.75f),2,0,SpriteMeshType.Tight);
                // OverrideGeometry输入是rect像素坐标；vertices读取结果才是经pivot/ppu转换的局部坐标。
                nativeSprite.OverrideGeometry(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector2>(new[]{new Vector2(0,0),new(4,0),new(0,4)}),
                    new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<ushort>(new ushort[]{0,1,2}));
                nativeSource.Apply(false,true);
                var mesh=RecipeGuideImages.ReadMesh(nativeSprite);
                nativeThumbnail=RecipeGuideImages.CreateMeshThumbnail(nativeSource,mesh);nativeReadback=Read(nativeThumbnail);var ok=true;var mismatches=new List<string>();
                for(var y=0;y<4;y++)for(var x=0;x<4;x++) {
                    var expected=x+y<=3?Pixel(x,y):new Color(0,0,0,0);var actual=nativeReadback.GetPixel(x,y,0);
                    var matched=Math.Abs(actual.r-expected.r)<.06&&Math.Abs(actual.g-expected.g)<.06&&Math.Abs(actual.b-expected.b)<.06&&Math.Abs(actual.a-expected.a)<.06;
                    ok&=matched;if(!matched)mismatches.Add($"{x},{y}: actual=({actual.r},{actual.g},{actual.b},{actual.a}) expected=({expected.r},{expected.g},{expected.b},{expected.a})");
                }
                if(!ok||nativeSprite.vertices.Length!=3)MelonLoader.MelonLogger.Warning("[EZapi] native Sprite mesh evidence: "+System.Text.Json.JsonSerializer.Serialize(new{vertices=nativeSprite.vertices.Select(v=>new{x=v.x,y=v.y}).ToArray(),uv=nativeSprite.uv.Select(v=>new{x=v.x,y=v.y}).ToArray(),triangles=nativeSprite.triangles.ToArray(),pivot=new{x=nativeSprite.pivot.x,y=nativeSprite.pivot.y},ppu=nativeSprite.pixelsPerUnit,mesh.Width,mesh.Height,mesh.Crop,mismatches}));
                check(nativeSprite.vertices.Length==3&&!nativeSource.isReadable&&ok,"actual Sprite OverrideGeometry vertices/triangles/UV/pivot/ppu reproduce tight shape without textureRect");
                check(RenderTexture.active?.Pointer==previous?.Pointer&&source.width==8,"actual Sprite mesh pipeline preserves shared source and render target");
            }finally{if(nativeReadback!=null)UnityEngine.Object.Destroy(nativeReadback);if(nativeThumbnail!=null)UnityEngine.Object.Destroy(nativeThumbnail);if(nativeSprite!=null)UnityEngine.Object.Destroy(nativeSprite);if(nativeSource!=null)UnityEngine.Object.Destroy(nativeSource);}
            foreach(var id in new[]{"ezapi.debug.factory_item","ezapi.sample.water_container","processed_juice","toothpaste"}) {
                var preview=RecipeGuideImages.Warm(id);var loads=RecipeGuideImages.Loads;
                check(preview.Error is null&&preview.Region is not null&&preview.Thumbnail!=null&&preview.Style is not null,"guide real directory sprite/thumbnail/style resolves: "+id);
                for(var i=0;i<30;i++)RecipeGuideImages.Warm(id);
                check(RecipeGuideImages.Loads==loads,"guide repeated cache hits do not create items: "+id);
                var sprite=preview.Sprite;var texture=preview.Texture;
                RecipeGuideImages.Clear();
                check(sprite!=null&&texture!=null&&sprite.texture?.Pointer==texture.Pointer&&texture.width>0,"guide cache clear preserves shared sprite/texture: "+id);
                check(preview.Thumbnail is null&&preview.Style is null,"guide cache clear drops owned thumbnail/style: "+id);
            }
            var successive=true;
            for(var i=0;i<12;i++){
                var preview=RecipeGuideImages.Warm("ezapi.debug.factory_amenity");
                successive&=preview.Error is null&&preview.Thumbnail!=null;
                RecipeGuideImages.Clear();
            }
            check(successive,"guide repeated detached factory creation/destruction does not mistake recycled native addresses for reused objects");
            var missing=RecipeGuideImages.Warm("ezapi.debug.image_missing");var negativeLoads=RecipeGuideImages.Loads;
            for(var i=0;i<30;i++)RecipeGuideImages.Warm("ezapi.debug.image_missing");
            check(missing.Error is not null&&RecipeGuideImages.Loads==negativeLoads,"guide missing IDs use negative cache");
            for(var i=0;i<128;i++)RecipeGuideImages.Warm("ezapi.debug.image_missing_"+i);
            check(RecipeGuideImages.Find("ezapi.debug.image_missing") is null&&RecipeGuideImages.Find("ezapi.debug.image_missing_127") is not null,"guide LRU128 evicts oldest entry");
            check(NativeAccess.SameRun(run),"guide checks preserve run identity");
        }finally{RecipeGuideImages.Clear();if(source!=null)UnityEngine.Object.Destroy(source);}
    }
    private static Texture2D Read(Texture2D source)
    {
        RenderTexture? render=null;Texture2D? result=null;var previous=RenderTexture.active;
        try {
            render=new RenderTexture(source.width,source.height,0,RenderTextureFormat.ARGB32);
            if(!render.Create())throw new InvalidOperationException("guide test readback target refused");
            Graphics.Blit(source,render);RenderTexture.active=render;
            result=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false);
            result.ReadPixels(new Rect(0,0,source.width,source.height),0,0,false);result.Apply(false,false);
            var completed=result;result=null;return completed;
        }finally{RenderTexture.active=previous;if(render!=null){render.Release();UnityEngine.Object.Destroy(render);}if(result!=null)UnityEngine.Object.Destroy(result);}
    }
}

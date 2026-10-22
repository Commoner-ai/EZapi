using Il2Cpp;
using UnityEngine;
namespace EZapi;

/// <summary>窗口期内最多缓存128个物品ID，最近使用者保留。仅Update预热临时实例和缩略图；Draw不创建物品或Unity资源。</summary>
internal static class RecipeGuideImages
{
    internal sealed record Preview(string ItemId,Sprite? Sprite,Texture2D? Texture,GuideImageRegion? Region,string? Error)
    {internal Texture2D? Thumbnail;internal GUIStyle? Style;}
    private static readonly Dictionary<string,Preview> Cache=new(StringComparer.Ordinal);
    private static readonly Dictionary<string,long> Access=new(StringComparer.Ordinal);private static long _clock;
    private static readonly HashSet<string> Loading=new(StringComparer.Ordinal);
    private static long _generation;
    private static string? _cleanupError;
    private static GameRunIdentity? _run;
    private static string? _locale;
    internal static int Loads{get;private set;}
    internal static int Draws{get;private set;}
    private static void Release(Preview preview)
    {
        try{if(preview.Thumbnail!=null)UnityEngine.Object.Destroy(preview.Thumbnail);}
        catch(Exception error){_cleanupError=error.ToString();MelonLoader.MelonLogger.Error("[EZapi] EZAPI_GUIDE_IMAGE_CLEANUP: "+error);}
        finally{preview.Thumbnail=null;preview.Style=null;}
    }
    internal static void Clear(){_generation++;foreach(var preview in Cache.Values)Release(preview);Cache.Clear();Access.Clear();_clock=0;_run=null;_locale=null;Loads=Draws=0;}
    private static void Observe(){var run=NativeAccess.RequireRun();var locale=NativeItemLocalization.CurrentLocaleCode;if(_run!=run||_locale!=locale){Clear();_run=run;_locale=locale;}}
    internal static Preview? Find(string id){Observe();if(Cache.TryGetValue(id,out var value)){Access[id]=++_clock;return value;}return null;}
    internal static Preview Warm(string id)
    {
        Observe();if(Find(id) is {} cached)return cached;
        // 切语言时原生工厂可能同步读取尚未完成的StringTable。延后且不缓存失败，避免异常及永久占位。
        if(!NativeGuideLocale.IsReady)return new(id,null,null,null,"EZAPI_GUIDE_IMAGE_LOCALE_PENDING");
        // 第三方工厂可能重入图鉴或替换目录；代际检查阻止旧加载结果进入新缓存。
        if(!Loading.Add(id))return new(id,null,null,null,"EZAPI_GUIDE_IMAGE_REENTRANT");
        var run=_run!;var locale=_locale;var generation=_generation;GameItem? item=null;Preview? result=null;Loads++;
        try {
            if(!Items.Exists(id))throw new ArgumentException("EZAPI_GUIDE_IMAGE_MISSING: "+id);
            item=Items.Create(id,false);
            if(!NativeAccess.SameRun(run))throw new InvalidOperationException("EZAPI_GUIDE_IMAGE_STALE");
            if(item.parentInventory is not null)throw new InvalidOperationException("EZAPI_GUIDE_IMAGE_ATTACHED: factory must return a detached item");
            var element=item.TryCast<GameItemElement>()??throw new InvalidOperationException("EZAPI_GUIDE_IMAGE_ELEMENT");
            var sprite=element.ResolveItemSprite();
            if(sprite==null||sprite.texture==null)throw new InvalidOperationException("EZAPI_GUIDE_IMAGE_SPRITE");
            var texture=sprite.texture;var mesh=ReadMesh(sprite);
            result=new(id,sprite,texture,mesh.Region,null);
            result.Thumbnail=CreateMeshThumbnail(texture,mesh);result.Style=new GUIStyle();result.Style.normal.background=result.Thumbnail;
        }catch(Exception error){if(result is not null)Release(result);result=new(id,null,null,null,error.GetType().Name+": "+error.Message);}
        finally{
            try{if(item is not null&&NativeAccess.SameRun(run)&&item.parentInventory is null)item.Destroy();}
            catch(Exception error){if(result is not null)Release(result);result=new(id,null,null,null,"EZAPI_GUIDE_IMAGE_ITEM_CLEANUP: "+error.Message);MelonLoader.MelonLogger.Error("[EZapi] "+result.Error);}
            finally{Loading.Remove(id);}
        }
        if(!NativeAccess.SameRun(run)||generation!=_generation||locale!=NativeItemLocalization.CurrentLocaleCode){if(result is not null)Release(result);return new(id,null,null,null,"EZAPI_GUIDE_IMAGE_STALE");}
        result??=new(id,null,null,null,"EZAPI_GUIDE_IMAGE_FAILED");
        if(Cache.Count>=128){var expired=Access.MinBy(p=>p.Value).Key;Release(Cache[expired]);Cache.Remove(expired);Access.Remove(expired);}
        Cache.Add(id,result);Access[id]=++_clock;return result;
    }
    internal static GuideImageMesh ReadMesh(Sprite sprite)
    {
        var texture=sprite.texture;var rect=sprite.rect;var pivot=sprite.pivot;
            var vertices=sprite.vertices;var uv=sprite.uv;var triangles=sprite.triangles;
            if(vertices is null||uv is null||triangles is null||vertices.Length>GuideImageMesh.MaxVertices||uv.Length!=vertices.Length||triangles.Length>GuideImageMesh.MaxIndices)
                throw new ArgumentException("EZAPI_GUIDE_IMAGE_MESH: bounded native geometry required.");
        return GuideImageMesh.Create(texture.width,texture.height,rect.width,rect.height,pivot.x,pivot.y,sprite.pixelsPerUnit,
                vertices.Select(v=>new GuideImagePoint(v.x,v.y)).ToArray(),uv.Select(v=>new GuideImagePoint(v.x,v.y)).ToArray(),triangles.ToArray());
    }
    internal static bool Draw(string id,Rect area)
    {
        if(!Cache.TryGetValue(id,out var preview)||preview.Region is null||preview.Sprite==null||preview.Texture==null||preview.Thumbnail==null||preview.Style is null)return false;
        if(preview.Sprite.texture?.Pointer!=preview.Texture.Pointer)return false;
        var box=preview.Region.Fit(new(area.x,area.y,area.width,area.height));
        GUI.Box(new(box.X,box.Y,box.Width,box.Height),"",preview.Style);Draws++;return true;
    }
    internal static Texture2D CreateThumbnail(Texture2D source,GuideImageRegion region)
    {
        var scale=Math.Min(1,128f/Math.Max(region.Width,region.Height));var width=Math.Max(1,(int)Math.Round(region.Width*scale));var height=Math.Max(1,(int)Math.Round(region.Height*scale));
        RenderTexture? render=null;Texture2D? thumbnail=null;var previous=RenderTexture.active;
        try {
            render=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32);if(!render.Create())throw new InvalidOperationException("EZAPI_GUIDE_IMAGE_RENDER_TARGET");
            var uv=region.Uv;Graphics.Blit(source,render,new Vector2(uv.Width,uv.Height),new Vector2(uv.X,uv.Y));RenderTexture.active=render;
            thumbnail=new Texture2D(width,height,TextureFormat.RGBA32,false);thumbnail.ReadPixels(new Rect(0,0,width,height),0,0,false);
            thumbnail.Apply(false,true);thumbnail.filterMode=FilterMode.Point;thumbnail.wrapMode=TextureWrapMode.Clamp;var result=thumbnail;thumbnail=null;return result;
        }finally{RenderTexture.active=previous;if(render!=null){render.Release();UnityEngine.Object.Destroy(render);}if(thumbnail!=null)UnityEngine.Object.Destroy(thumbnail);}
    }
    /// <summary>GPU读回限定UV区域后按三角形/UV映射组装透明缩略图。源纹理即使不可读也可用，不修改源过滤或像素。</summary>
    internal static Texture2D CreateMeshThumbnail(Texture2D source,GuideImageMesh mesh)
    {
        RenderTexture? render=null;Texture2D? readback=null;Texture2D? thumbnail=null;var previous=RenderTexture.active;
        try {
            render=new RenderTexture(mesh.ReadWidth,mesh.ReadHeight,0,RenderTextureFormat.ARGB32);
            if(!render.Create())throw new InvalidOperationException("EZAPI_GUIDE_IMAGE_RENDER_TARGET");
            var crop=mesh.Crop;
            Graphics.Blit(source,render,new Vector2(crop.Width/source.width,crop.Height/source.height),new Vector2(crop.X/source.width,crop.Y/source.height));
            RenderTexture.active=render;
            readback=new Texture2D(mesh.ReadWidth,mesh.ReadHeight,TextureFormat.RGBA32,false);
            readback.ReadPixels(new Rect(0,0,mesh.ReadWidth,mesh.ReadHeight),0,0,false);readback.Apply(false,false);
            var pixels=readback.GetPixels32();
            if(pixels.Length!=mesh.ReadWidth*mesh.ReadHeight)throw new InvalidOperationException("EZAPI_GUIDE_IMAGE_READBACK: unexpected pixel count.");
            var output=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32>(mesh.Width*mesh.Height);
            for(var i=0;i<mesh.Samples.Length;i++){var sample=mesh.Samples[i];output[i]=sample<0?new Color32(0,0,0,0):pixels[sample];}
            thumbnail=new Texture2D(mesh.Width,mesh.Height,TextureFormat.RGBA32,false);
            thumbnail.SetPixels32(output);thumbnail.Apply(false,true);thumbnail.filterMode=FilterMode.Point;thumbnail.wrapMode=TextureWrapMode.Clamp;
            var result=thumbnail;thumbnail=null;return result;
        }finally{
            RenderTexture.active=previous;
            if(render!=null){render.Release();UnityEngine.Object.Destroy(render);}
            if(readback!=null)UnityEngine.Object.Destroy(readback);
            if(thumbnail!=null)UnityEngine.Object.Destroy(thumbnail);
        }
    }
    internal static object Report()=>new{cached=Cache.Count,loads=Loads,draws=Draws,limit=128,localeState=NativeGuideLocale.State,localeError=NativeGuideLocale.Error,cleanupError=_cleanupError,entries=Cache.Values.Select(p=>new{p.ItemId,ready=p.Region is not null&&p.Thumbnail!=null&&p.Style is not null,p.Error,region=p.Region}).ToArray(),
        scope="bounded LRU128 triangle/UV thumbnails up to128px; GPU source crop readback up to512px; shared source sprites never destroyed; tight/rotated geometry and transparent margins supported"};
}

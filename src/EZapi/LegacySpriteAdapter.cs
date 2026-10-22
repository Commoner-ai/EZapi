using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
namespace EZapi;

/// <summary>
/// 当前 MelonLoader 构建的资源兼容桥；不冒充官方 LoadedMod，不修改游戏资源加载器。
/// PNG 每进程解码一次，通过公开 atlasCache 与 SetSprite 接回游戏的正常精灵解析。
/// </summary>
internal static class LegacySpriteAdapter
{
    private static readonly Dictionary<string,(string? File,int Width,int Height)> Sources=new();
    private static readonly Dictionary<string,Sprite> Cache=new();
    private static readonly Dictionary<string,byte[]> Embedded=new();
    private static readonly HashSet<string> Failed=new();
    internal static string Key(ItemDefinition d)=>"ezapi:"+d.Id;
    internal static string? SourcePath(string key)
        => Sources.TryGetValue(key, out var source) ? source.File : null;
    private const string AtlasPrefix="EZapi.Content/";
    private static readonly Dictionary<string,Il2CppSystem.Collections.Generic.Dictionary<string,Sprite>> OwnedAtlases=new(StringComparer.Ordinal);
    internal static string? Prepare(ItemDefinition d,string? packRoot)=>PngAssets.Prepare(d,packRoot);
    internal static void Commit(ItemDefinition d,string? path)=>Sources.Add(Key(d),(path,d.Width??1,d.Height??1));
    internal static void CommitEmbedded(ItemDefinition d,byte[] png){Sources.Add(Key(d),(null,d.Width??1,d.Height??1));Embedded.Add(Key(d),png.ToArray());}
    internal static string? SourcePath(ItemDefinition d)=>SourcePath(Key(d));

    internal static string EmbeddedAtlasId(string ownerId,string atlasKey)=>"EZapi.Assets/"+ownerId+"/"+atlasKey;
    internal static string EmbeddedSpriteCacheKey(string atlasId,string spriteKey)=>"embedded:"+atlasId+"/"+spriteKey;

    /// <summary>Checks the current game's public atlas cache before allocating any textures.</summary>
    internal static void RequireAtlasSlot(string atlasId)
    {
        var atlases=RenderHandler.atlasCache;
        if(atlases is null)throw new SpriteAssetNotReadyException("EZAPI_ASSET_NOT_READY: native sprite atlas cache is not ready.");
        if(atlases.ContainsKey(atlasId)) {
            if(!OwnedAtlases.TryGetValue(atlasId,out var owned)||atlases[atlasId].Pointer!=owned.Pointer)
                throw new SpriteAssetCollisionException("EZAPI_ASSET_COLLISION: "+atlasId);
        }
    }

    /// <summary>Creates one validated embedded sprite through the same lifetime/cache bridge as JSON items.</summary>
    internal static Sprite CreateEmbeddedSprite(string key,byte[] png,int width,int height,float pivotX,float pivotY,SpriteFilter filter,bool keepReadable)
    {
        if(Cache.TryGetValue(key,out var existing)&&existing!=null)return existing;
        if(Failed.Contains(key))throw new InvalidDataException("EZAPI_ASSET: prior sprite creation failed: "+key);
        Texture2D? texture=null;Sprite? sprite=null;
        try {
            var gridSize=(UISettings.current??throw new SpriteAssetNotReadyException("EZAPI_ASSET_NOT_READY: native UI settings are not ready.")).gridSize;
            if(gridSize<1||gridSize>128)throw new SpriteAssetNotReadyException("EZAPI_ASSET_NOT_READY: native UI grid size is outside the supported range.");
            texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!ImageConversion.LoadImage(texture,new Il2CppStructArray<byte>(png),false))
                throw new InvalidDataException("EZAPI_ASSET: Unity could not decode embedded PNG "+key);
            if(texture.width>2048||texture.height>2048)throw new InvalidDataException("EZAPI_ASSET: PNG exceeds 2048 pixels per side: "+key);
            texture.filterMode=filter==SpriteFilter.Point?FilterMode.Point:FilterMode.Bilinear;
            texture.wrapMode=TextureWrapMode.Clamp;
            texture.Apply(false,!keepReadable);
            var pixelsPerUnit=texture.width/(gridSize/100f*width);
            if(!float.IsFinite(pixelsPerUnit)||pixelsPerUnit<=0)throw new InvalidDataException("EZAPI_ASSET: invalid pixels-per-unit for "+key);
            sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(pivotX,pivotY),pixelsPerUnit);
            sprite.name=key;
            UnityEngine.Object.DontDestroyOnLoad(texture);UnityEngine.Object.DontDestroyOnLoad(sprite);
            Cache[key]=sprite;
            return sprite;
        } catch {
            if(sprite!=null)UnityEngine.Object.Destroy(sprite);
            if(texture!=null)UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    /// <summary>Installs all keys as one owned atlas; the game's dictionary is updated only after staging succeeds.</summary>
    internal static void InstallAtlas(string atlasId,IReadOnlyDictionary<string,Sprite> sprites)
    {
        var atlases=RenderHandler.atlasCache??throw new SpriteAssetNotReadyException("EZAPI_ASSET_NOT_READY: native sprite atlas cache is not ready.");
        if(OwnedAtlases.TryGetValue(atlasId,out var entries)) {
            if(atlases.ContainsKey(atlasId)&&atlases[atlasId].Pointer!=entries.Pointer)
                throw new SpriteAssetCollisionException("EZAPI_ASSET_COLLISION: "+atlasId);
            foreach(var sprite in sprites) {
                if(entries.ContainsKey(sprite.Key)&&entries[sprite.Key].Pointer!=sprite.Value.Pointer)
                    throw new SpriteAssetCollisionException("EZAPI_ASSET_SPRITE_COLLISION: "+atlasId+"/"+sprite.Key);
            }
            foreach(var sprite in sprites)entries[sprite.Key]=sprite.Value;
            if(!atlases.ContainsKey(atlasId))atlases.Add(atlasId,entries);
            return;
        }
        if(atlases.ContainsKey(atlasId))throw new SpriteAssetCollisionException("EZAPI_ASSET_COLLISION: "+atlasId);
        entries=new Il2CppSystem.Collections.Generic.Dictionary<string,Sprite>();
        foreach(var sprite in sprites)entries.Add(sprite.Key,sprite.Value);
        atlases.Add(atlasId,entries);
        try { OwnedAtlases.Add(atlasId,entries); }
        catch { if(atlases.ContainsKey(atlasId)&&atlases[atlasId].Pointer==entries.Pointer)atlases.Remove(atlasId); throw; }
    }

    internal static void ForgetSprite(string key)
    {
        Sources.Remove(key);Embedded.Remove(key);Failed.Remove(key);
        if(Cache.Remove(key,out var sprite)&&sprite!=null) {
            var referenced=Cache.Values.Any(other=>other!=null&&other.texture!=null&&other.texture.Pointer==sprite.texture.Pointer);
            if(!referenced)UnityEngine.Object.Destroy(sprite.texture);
            UnityEngine.Object.Destroy(sprite);
        }
    }

    /// <summary>
    /// 本构建缺少官方 SetSpriteAndShapeFromMod。把本包 PNG 放入公开 atlasCache 的隔离图集，
    /// 使用 GameItem.SetSprite 交回原生渲染。JSON 的显式 GridShape 由工厂单独 SetShape。
    /// 不修补 ResolveSpriteByName/ResolveItemSprite，不直接写 inner/fakeInner UI。
    /// </summary>
    internal static void Bind(GameItem item,ItemDefinition d)
    {
        var sprite=Find(Key(d));
        if(sprite==null)throw new InvalidDataException("EZAPI_ASSET: unable to create sprite for "+d.Id);
        var atlasId=AtlasPrefix+(d.ModId??"ezapi.code");
        var atlases=RenderHandler.atlasCache??throw new InvalidOperationException("EZAPI_ASSET_NOT_READY: native atlas cache is not ready.");
        if(!OwnedAtlases.TryGetValue(atlasId,out var entries)) {
            if(atlases.ContainsKey(atlasId))throw new InvalidOperationException("EZAPI_ASSET_COLLISION: "+atlasId);
            entries=new Il2CppSystem.Collections.Generic.Dictionary<string,Sprite>();
            atlases.Add(atlasId,entries); OwnedAtlases.Add(atlasId,entries);
        } else if(!atlases.ContainsKey(atlasId)) atlases.Add(atlasId,entries);
        else if(atlases[atlasId].Pointer!=entries.Pointer)throw new InvalidOperationException("EZAPI_ASSET_COLLISION: "+atlasId);
        entries[d.Id]=sprite;
        // CreateEmptyItem 的默认精灵分支由游戏创建；useSpriteFromMod 是只读属性，保持原生值。
        item.SetSprite(atlasId,d.Id);
    }
    internal static Sprite? Find(string key)
    {
        if(key is null||Failed.Contains(key))return null;
        if(!Sources.TryGetValue(key,out var source))return null;
        if(Cache.TryGetValue(key,out var sprite)&&sprite!=null)return sprite;
        Texture2D? texture=null;
        try {
            var gridSize=(UISettings.current??throw new InvalidOperationException("Native UI settings are not ready.")).gridSize;
            if(gridSize<1||gridSize>128)throw new InvalidDataException("Native UI grid size is outside supported bounds.");
            texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(source.File is not null||Embedded.ContainsKey(key)) {
                if(!ImageConversion.LoadImage(texture,new Il2CppStructArray<byte>(source.File is null?Embedded[key]:File.ReadAllBytes(source.File)),false))
                    throw new InvalidDataException("PNG decoding failed.");
                if(texture.width>2048||texture.height>2048)throw new InvalidDataException("PNG exceeds 2048 pixels per side.");
            } else {
                // 原生部分精灵按像素裁切。2×2 方图不能代表 1×2 / 2×1 物品。
                // 占位图按当前格子像素及声明长宽生成，保持形状之外的真实比例。
                UnityEngine.Object.Destroy(texture);
                var width=checked(source.Width*gridSize);var height=checked(source.Height*gridSize);
                texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
                var pixels=new Color[checked(width*height)];
                var tile=Math.Max(1,gridSize/2);
                for(var y=0;y<height;y++)for(var x=0;x<width;x++)pixels[y*width+x]=((x/tile+y/tile)&1)==0?Color.white:Color.gray;
                texture.SetPixels(new Il2CppStructArray<Color>(pixels));texture.Apply();
            }
            texture.filterMode=FilterMode.Point;
            texture.wrapMode=TextureWrapMode.Clamp;
            sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(0.5f,0.5f),texture.width/(gridSize/100f*source.Width));
            sprite.name=key;
            UnityEngine.Object.DontDestroyOnLoad(texture);UnityEngine.Object.DontDestroyOnLoad(sprite);
            Cache[key]=sprite;
            return sprite;
        } catch(Exception e) {
            if(texture!=null)UnityEngine.Object.Destroy(texture);
            Failed.Add(key);
            MelonLoader.MelonLogger.Error("[EZapi] EZAPI_ASSET: "+key+": "+e);
            return null;
        }
    }
    internal static void Clear()
    {
        if(OwnedAtlases.Count>0) {
            var atlases=RenderHandler.atlasCache;
            if(atlases is not null)foreach(var entry in OwnedAtlases)
                if(atlases.ContainsKey(entry.Key)&&atlases[entry.Key].Pointer==entry.Value.Pointer)atlases.Remove(entry.Key);
        }
        OwnedAtlases.Clear();
        foreach(var sprite in Cache.Values)if(sprite!=null){UnityEngine.Object.Destroy(sprite.texture);UnityEngine.Object.Destroy(sprite);}
        Cache.Clear();Sources.Clear();Failed.Clear();Embedded.Clear();
    }
}


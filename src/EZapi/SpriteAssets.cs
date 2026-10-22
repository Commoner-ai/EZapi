using System.Reflection;
using Il2Cpp;
using UnityEngine;

namespace EZapi;

/// <summary>Creates process-owned atlases from PNG resources embedded in a mod DLL.</summary>
public static class SpriteAssets
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, EmbeddedSpriteAtlas> Atlases = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads all resources and validates the complete declaration before returning a handle.
    /// No Unity objects or native atlas entries are created until TryInstall is called.
    /// </summary>
    public static EmbeddedSpriteAtlas CreateEmbeddedAtlas(
        string ownerId,
        string atlasKey,
        Assembly assembly,
        IReadOnlyList<SpriteDefinition> definitions,
        SpriteFilter filter = SpriteFilter.Point)
    {
        if (!EmbeddedSpriteResources.IsOwnerId(ownerId))
            throw new ArgumentException("Owner IDs must use 1..128 letters, digits, '.', '_' or '-'.", nameof(ownerId));
        if (!EmbeddedSpriteResources.IsKey(atlasKey))
            throw new ArgumentException("Atlas keys must use 1..128 letters, digits, '.', '_' or '-'.", nameof(atlasKey));
        if (filter is not SpriteFilter.Point and not SpriteFilter.Bilinear)
            throw new ArgumentOutOfRangeException(nameof(filter));

        var resources = EmbeddedSpriteResources.Read(assembly, definitions);
        var nativeAtlasId = LegacySpriteAdapter.EmbeddedAtlasId(ownerId, atlasKey);
        lock (Gate)
        {
            if (Atlases.ContainsKey(nativeAtlasId))
                throw new InvalidOperationException("EZAPI_ASSET_DUPLICATE: atlas already registered: " + nativeAtlasId);
            var atlas = new EmbeddedSpriteAtlas(ownerId, atlasKey, nativeAtlasId, resources, filter);
            Atlases.Add(nativeAtlasId, atlas);
            return atlas;
        }
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            // 先从当前肖像字典撤下自己的引用，再让宿主释放图集的 Unity 对象。
            PortraitSprites.Clear();
            foreach (var atlas in Atlases.Values) atlas.OnRuntimeCleared();
            Atlases.Clear();
        }
    }
}

/// <summary>
/// A validated embedded atlas. Installation and Unity object access must run on the
/// initialized game thread. The returned sprite objects live until EZapi unloads.
/// </summary>
public sealed class EmbeddedSpriteAtlas
{
    private readonly IReadOnlyList<PreparedEmbeddedSprite> _resources;
    private readonly SpriteFilter _filter;
    private Dictionary<string, Sprite>? _sprites;
    private bool _installed;
    private bool _failed;
    private string? _failure;

    internal EmbeddedSpriteAtlas(
        string ownerId,
        string atlasKey,
        string nativeAtlasId,
        IReadOnlyList<PreparedEmbeddedSprite> resources,
        SpriteFilter filter)
    {
        OwnerId = ownerId;
        AtlasKey = atlasKey;
        NativeAtlasId = nativeAtlasId;
        _resources = resources;
        _filter = filter;
    }

    /// <summary>Mod ID that owns this atlas.</summary>
    public string OwnerId { get; }

    /// <summary>Caller-selected key within the owner namespace.</summary>
    public string AtlasKey { get; }

    /// <summary>Isolated key used by the current game's public sprite atlas cache.</summary>
    public string NativeAtlasId { get; }

    /// <summary>True after every declared sprite has been installed.</summary>
    public bool Installed => _installed;

    /// <summary>True after a permanent resource or atlas conflict; not set for a not-ready game.</summary>
    public bool Failed => _failed;

    /// <summary>Number of validated sprite definitions.</summary>
    public int Count => _resources.Count;

    /// <summary>The last installation failure, if any.</summary>
    public string? Failure => _failure;

    /// <summary>Returns an installed sprite, or null before installation/for an unknown key.</summary>
    public Sprite? GetSprite(string spriteKey)
    {
        GameSession.RequireThread();
        if (!_installed || _sprites is null || string.IsNullOrWhiteSpace(spriteKey)) return null;
        return _sprites.TryGetValue(spriteKey, out var sprite) && sprite != null ? sprite : null;
    }

    /// <summary>
    /// 把已安装图片注册到当前游戏的肖像字典，返回可传给 ClientVisitApi/SupplierApi 的稳定 spriteName。
    /// 新场景若重建 SpriteDict，应在使用前重新调用；重复注册同一图片幂等，冲突绝不覆盖。
    /// </summary>
    public string RegisterPortrait(string spriteKey)
    {
        GameSession.RequireThread();
        if (!_resources.Any(resource => string.Equals(resource.Definition.Key, spriteKey, StringComparison.Ordinal)))
            throw new ArgumentException("EZAPI_PORTRAIT_KEY: unknown sprite key: " + spriteKey, nameof(spriteKey));
        var sprite = GetSprite(spriteKey);
        if (!_installed || _failed || sprite == null)
            throw new InvalidOperationException("EZAPI_ASSET_NOT_INSTALLED: install the atlas before registering a portrait.");
        return PortraitSprites.Register(OwnerId, AtlasKey, spriteKey, sprite);
    }

    /// <summary>
    /// Installs the complete atlas once. A native-cache-not-ready result may be retried;
    /// resource decode and namespace collisions permanently fail this handle.
    /// </summary>
    public bool TryInstall(Action? onInstalled = null, Action<Exception>? onFailure = null)
    {
        GameSession.RequireThread();
        if (_installed) return true;
        if (_failed) return false;

        var cacheKeys = new List<string>(_resources.Count);
        try
        {
            LegacySpriteAdapter.RequireAtlasSlot(NativeAtlasId);
            var sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (var resource in _resources)
            {
                var cacheKey = LegacySpriteAdapter.EmbeddedSpriteCacheKey(NativeAtlasId, resource.Definition.Key);
                var sprite = LegacySpriteAdapter.CreateEmbeddedSprite(
                    cacheKey, resource.Png, resource.Definition.Width, resource.Definition.Height,
                    resource.Definition.PivotX, resource.Definition.PivotY, _filter, resource.Definition.KeepReadable);
                cacheKeys.Add(cacheKey);
                sprites.Add(resource.Definition.Key, sprite);
            }
            LegacySpriteAdapter.InstallAtlas(NativeAtlasId, sprites);
            _sprites = sprites;
            _installed = true;
            InvokeInstalled(onInstalled);
            return true;
        }
        catch (Exception error)
        {
            foreach (var cacheKey in cacheKeys) LegacySpriteAdapter.ForgetSprite(cacheKey);
            _failure = error.ToString();
            _failed = error is not SpriteAssetNotReadyException;
            InvokeFailure(onFailure, error);
            return false;
        }
    }

    /// <summary>
    /// Assigns this atlas and sprite key through GameItem.SetSprite, then checks native readback.
    /// With allowStaging=true, the item may be assigned before atlas installation; call TryInstall
    /// before the game tries to render that item.
    /// </summary>
    public void Assign(GameItem item, string spriteKey, bool allowStaging = false)
    {
        GameSession.RequireThread();
        if (item is null || item.Pointer == IntPtr.Zero) throw new ArgumentException("A live GameItem is required.", nameof(item));
        if (!_resources.Any(resource => string.Equals(resource.Definition.Key, spriteKey, StringComparison.Ordinal)))
            throw new ArgumentException("Unknown sprite key: " + spriteKey, nameof(spriteKey));
        if (_failed) throw new InvalidOperationException("EZAPI_ASSET_FAILED: " + (_failure ?? NativeAtlasId));
        if (!_installed && !allowStaging)
            throw new InvalidOperationException("EZAPI_ASSET_NOT_INSTALLED: call TryInstall first or explicitly allow staging.");

        item.SetSprite(NativeAtlasId, spriteKey);
        if (!string.Equals(item.spriteAtlasPath, NativeAtlasId, StringComparison.Ordinal) ||
            !string.Equals(item.spritePath, spriteKey, StringComparison.Ordinal))
            throw new InvalidOperationException("EZAPI_ASSET_ASSIGN_READBACK: GameItem.SetSprite did not retain the requested atlas and sprite key.");
    }

    internal void OnRuntimeCleared()
    {
        _sprites = null;
        _installed = false;
        _failed = true;
        _failure = "EZAPI_ASSET_RELEASED: the runtime released this atlas; declare a new handle after restart.";
    }

    private void InvokeInstalled(Action? callback)
    {
        if (callback is null) return;
        try { callback(); }
        catch (Exception error) { MelonLoader.MelonLogger.Warning("[EZapi] Sprite atlas installed, but its callback failed: " + error); }
    }

    private void InvokeFailure(Action<Exception>? callback, Exception error)
    {
        if (callback is null) return;
        try { callback(error); }
        catch (Exception callbackError) { MelonLoader.MelonLogger.Warning("[EZapi] Sprite atlas failure callback failed: " + callbackError); }
    }
}

internal sealed class SpriteAssetNotReadyException : InvalidOperationException
{
    internal SpriteAssetNotReadyException(string message) : base(message) { }
}

internal sealed class SpriteAssetCollisionException : InvalidOperationException
{
    internal SpriteAssetCollisionException(string message) : base(message) { }
}

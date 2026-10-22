using System.Security.Cryptography;
using System.Text;
using Il2Cpp;
using UnityEngine;
using PortraitDictionary = Il2CppSystem.Collections.Generic.Dictionary<string, UnityEngine.Sprite>;

namespace EZapi;

/// <summary>
/// 单点适配当前公开 SpriteDict。只记录托管键与用于所有权比较的指针数值，
/// 不持有场景 SpriteDict/字典包装器，不覆盖别人的图片，不安装解析补丁。
/// </summary>
internal static class PortraitSprites
{
    private static readonly Dictionary<string, IntPtr> Owned = new(StringComparer.Ordinal);

    // 长 owner/atlas/key 也生成固定长度、无路径分隔符的稳定键，可直接传给访客 API。
    internal static string Key(string ownerId, string atlasKey, string spriteKey)
    {
        var identity = ownerId + "\0" + atlasKey + "\0" + spriteKey;
        return "ezapi.portrait." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    internal static string Register(string ownerId, string atlasKey, string spriteKey, Sprite sprite)
    {
        GameSession.RequireThread();
        if (sprite == null || sprite.Pointer == IntPtr.Zero)
            throw new InvalidOperationException("EZAPI_PORTRAIT_SPRITE: installed live sprite required.");
        var registry = SpriteDict.Instance;
        var data = registry == null ? null : registry.spriteDictionary;
        if (data is null || data.Pointer == IntPtr.Zero)
            throw new SpriteAssetNotReadyException("EZAPI_PORTRAIT_NOT_READY: wait for the native portrait dictionary.");
        var key = Key(ownerId, atlasKey, spriteKey);
        var inserted = false;
        try
        {
            if (data.ContainsKey(key))
            {
                // 即便图片相同，也不能认领其他来源先创建的键。
                if (!Owned.TryGetValue(key, out var pointer) || pointer != sprite.Pointer ||
                    data[key] == null || data[key].Pointer != sprite.Pointer)
                    throw new InvalidOperationException("EZAPI_PORTRAIT_COLLISION: " + key);
            }
            else
            {
                if (!SameDictionary(registry!, data))
                    throw new InvalidOperationException("EZAPI_PORTRAIT_CONTEXT: native dictionary changed before registration.");
                // Add 若抛错也可能已产生副作用；异常路径只移除仍匹配本 Sprite 的本次新键。
                inserted = true;
                data.Add(key, sprite);
            }
            if (!SameDictionary(registry!, data) || !data.ContainsKey(key) || data[key] == null ||
                data[key].Pointer != sprite.Pointer)
                throw new InvalidOperationException("EZAPI_PORTRAIT_READBACK: native dictionary did not retain the requested sprite.");
            var resolved = registry!.GetSprite(key);
            if (resolved == null || resolved.Pointer != sprite.Pointer || !SameDictionary(registry!, data))
                throw new InvalidOperationException("EZAPI_PORTRAIT_READBACK: native GetSprite did not resolve the requested sprite.");
            Owned[key] = sprite.Pointer;
            return key;
        }
        catch
        {
            if (inserted)
            {
                try { RemoveIfOwned(data, key, sprite.Pointer); }
                catch (Exception error)
                {
                    Owned[key] = sprite.Pointer; // 保留清理凭据，宿主停止时仍可再次核对。
                    MelonLoader.MelonLogger.Warning("[EZapi] EZAPI_PORTRAIT_CLEANUP: " + error.Message);
                }
            }
            throw;
        }
    }

    private static bool SameDictionary(SpriteDict registry, PortraitDictionary data)
    {
        var current = SpriteDict.Instance;
        return current != null && current.Pointer == registry.Pointer && current.spriteDictionary?.Pointer == data.Pointer;
    }

    private static void RemoveIfOwned(PortraitDictionary data, string key, IntPtr pointer)
    {
        if (data.ContainsKey(key) && data[key] != null && data[key].Pointer == pointer) data.Remove(key);
    }

    // 宿主在主线程停止资源前调用；GameSession 此时可能已 Stop，不能要求仍有有效周目。
    internal static void Clear()
    {
        if (Owned.Count == 0) return;
        try
        {
            var registry = SpriteDict.Instance;
            var data = registry == null ? null : registry.spriteDictionary;
            if (data is not null && data.Pointer != IntPtr.Zero)
                foreach (var entry in Owned)
                    try { RemoveIfOwned(data, entry.Key, entry.Value); }
                    catch (Exception error) { MelonLoader.MelonLogger.Warning("[EZapi] EZAPI_PORTRAIT_CLEANUP: " + error.Message); }
        }
        catch (Exception error) { MelonLoader.MelonLogger.Warning("[EZapi] EZAPI_PORTRAIT_CLEANUP: " + error.Message); }
        finally { Owned.Clear(); }
    }
}

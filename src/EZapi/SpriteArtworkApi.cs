using Il2Cpp;
using UnityEngine;

namespace EZapi;

/// <summary>Only Inspected carries content evidence. Failures never imply a black or empty image.</summary>
public enum SpriteArtworkStatus { Inspected, Invalid, Unavailable, LimitExceeded, CleanupFailed }
/// <summary>Observed read path; Mixed means color and split alpha used different CPU/GPU paths.</summary>
public enum ArtworkReadPath { None, ReadableTexture, GpuReadback, Mixed }

/// <summary>Detached managed result; no Sprite, Texture or pixel array retained or exported.</summary>
public sealed record SpriteArtworkResult(SpriteArtworkStatus Status, ArtworkStatistics? Statistics = null,
    ArtworkCrop? Crop = null, ArtworkReadPath ReadPath = ArtworkReadPath.None, string? Reason = null);

/// <summary>Explicit bounded native Sprite inspection, including unreadable textures, tight meshes and split alpha.</summary>
public static class SpriteArtworkApi
{
    private static bool _reading;
    private static bool _resolvingItem;

    /// <summary>
    /// Inspect one existing sprite on the initialized Unity main thread. No factory, file export, source mutation or caching.
    /// GPU readback is synchronous; budget calls across frames, never run once per OnGUI/card redraw.
    /// </summary>
    public static SpriteArtworkResult Inspect(Sprite sprite, ArtworkOptions? options = null)
    {
        if (!GameSession.OnThread) return Failure(SpriteArtworkStatus.Unavailable, "THREAD");
        if (_reading) return Failure(SpriteArtworkStatus.Unavailable, "REENTRY");
        ArtworkOptions settings;
        try { settings = ArtworkPixels.Validate(options); }
        catch (ArgumentException) { return Failure(SpriteArtworkStatus.Invalid, "OPTIONS"); }
        _reading = true;
        try
        {
            if (sprite == null || sprite.Pointer == IntPtr.Zero) return Failure(SpriteArtworkStatus.Invalid, "SPRITE");
            var spritePointer = sprite.Pointer;
            var texture = sprite.texture;
            if (texture == null) return Failure(SpriteArtworkStatus.Unavailable, "TEXTURE");
            var texturePointer = texture.Pointer; var width = texture.width; var height = texture.height;
            var alpha = sprite.associatedAlphaSplitTexture;
            var alphaPointer = alpha == null ? IntPtr.Zero : alpha.Pointer;
            if (alpha != null && (alpha.width != width || alpha.height != height)) return Failure(SpriteArtworkStatus.Unavailable, "ALPHA");
            var nativeUv = sprite.uv; var nativeIndices = sprite.triangles;
            if (nativeUv is null || nativeIndices is null || nativeUv.Length > ArtworkMesh.MaximumVertices || nativeIndices.Length > ArtworkMesh.MaximumIndices)
                return Failure(SpriteArtworkStatus.LimitExceeded, "LIMIT");
            var uv = new float[checked((int)nativeUv.Length * 2)]; var indices = new ushort[(int)nativeIndices.Length];
            for (var i = 0; i < nativeUv.Length; i++) { uv[i * 2] = nativeUv[i].x; uv[i * 2 + 1] = nativeUv[i].y; }
            for (var i = 0; i < indices.Length; i++) indices[i] = nativeIndices[i];
            ArtworkCrop? rectangle = null;
            // textureRect is unavailable for tightly packed sprites. Their actual UV
            // triangle mask gives a safe crop without sampling neighbouring atlas art.
            if (!sprite.packed || sprite.packingMode != SpritePackingMode.Tight)
            {
                var rect = sprite.textureRect;
                rectangle = new(Integral(rect.x), Integral(rect.y), Integral(rect.width), Integral(rect.height));
            }
            var mesh = ArtworkMesh.Build(width, height, uv, indices, rectangle, settings);
            RequireCurrent();
            var color = ReadCrop(texture, mesh.Crop, settings, false);
            RequireCurrent();
            var path = color.Path;
            if (alpha != null)
            {
                var opacity = ReadCrop(alpha, mesh.Crop, settings, true);
                RequireCurrent();
                for (var i = 0; i < color.Bytes.Length; i += 4) color.Bytes[i + 3] = opacity.Bytes[i];
                if (opacity.Path != path) path = ArtworkReadPath.Mixed;
            }
            var statistics = ArtworkPixels.Analyze(mesh.Crop.Width, mesh.Crop.Height, color.Bytes, mesh.Mask, settings);
            RequireCurrent();
            return new(SpriteArtworkStatus.Inspected, statistics, mesh.Crop, path);

            void RequireCurrent()
            {
                if (sprite == null || sprite.Pointer != spritePointer || sprite.texture == null || sprite.texture.Pointer != texturePointer || texture.width != width || texture.height != height ||
                    (sprite.associatedAlphaSplitTexture?.Pointer ?? IntPtr.Zero) != alphaPointer || alpha != null && (alpha.width != width || alpha.height != height))
                    throw new InvalidOperationException("source changed during inspection");
            }
        }
        catch (ArtworkCleanupException error) { return Failure(SpriteArtworkStatus.CleanupFailed, "CLEANUP", error); }
        catch (ArtworkLimitException error) { return Failure(SpriteArtworkStatus.LimitExceeded, "LIMIT", error); }
        catch (ArgumentException error) { return Failure(SpriteArtworkStatus.Invalid, "GEOMETRY", error); }
        catch (Exception error) { return Failure(SpriteArtworkStatus.Unavailable, "READ", error); }
        finally { _reading = false; }
    }

    /// <summary>Resolve the current item's raw native atlas sprite; no UNKNOWN fallback or item factory is used.</summary>
    public static SpriteArtworkResult InspectItem(GameItem item, ArtworkOptions? options = null)
    {
        if (!GameSession.OnThread) return Failure(SpriteArtworkStatus.Unavailable, "THREAD");
        if (_reading || _resolvingItem) return Failure(SpriteArtworkStatus.Unavailable, "REENTRY");
        try { _ = ArtworkPixels.Validate(options); }
        catch (ArgumentException) { return Failure(SpriteArtworkStatus.Invalid, "OPTIONS"); }
        _resolvingItem = true;
        try
        {
            var run = NativeAccess.RequireRun();
            if (!GameBuildFingerprint.IsKnown) return Failure(SpriteArtworkStatus.Unavailable, "BUILD");
            NativeAccess.RequireItem(item);
            var pointer = item.Pointer; var instance = item.uniqueId; var id = item.identifier;
            var atlas = item.spriteAtlasPath; var key = item.spritePath;
            if (string.IsNullOrWhiteSpace(atlas) || string.IsNullOrWhiteSpace(key)) return Failure(SpriteArtworkStatus.Unavailable, "KEY");
            var sprite = RenderHandler.LoadFromAtlasRaw(atlas, key);
            if (!Current()) return Failure(SpriteArtworkStatus.Unavailable, "STALE");
            if (sprite == null) return Failure(SpriteArtworkStatus.Unavailable, "MISSING");
            var result = Inspect(sprite, options);
            return Current() ? result : Failure(SpriteArtworkStatus.Unavailable, "STALE");
            bool Current() => NativeAccess.SameRun(run) && item.Pointer == pointer && item.uniqueId == instance && item.identifier == id &&
                item.unitCount > 0 && item.spriteAtlasPath == atlas && item.spritePath == key;
        }
        catch (Exception error) { return Failure(SpriteArtworkStatus.Unavailable, "ITEM", error); }
        finally { _resolvingItem = false; }
    }

    private static int Integral(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 65536 || Math.Abs(value - Math.Round(value)) > 0.01)
            throw new ArgumentException("nonintegral native rectangle");
        return (int)Math.Round(value);
    }

    private static (byte[] Bytes, ArtworkReadPath Path) ReadCrop(Texture2D texture, ArtworkCrop crop, ArtworkOptions options, bool rawData)
    {
        var linearProject = QualitySettings.activeColorSpace == ColorSpace.Linear;
        var sourceSrgb = texture.isDataSRGB;
        if (texture.isReadable)
        {
            try
            {
                var pixels = texture.GetPixels(crop.X, crop.Y, crop.Width, crop.Height);
                if (pixels is null || pixels.Length != (long)crop.Width * crop.Height) throw new InvalidOperationException("pixel count mismatch");
                var bytes = new byte[checked(crop.Width * crop.Height * 4)];
                for (var i = 0; i < pixels.Length; i++)
                {
                    var value = !rawData && linearProject && !sourceSrgb ? pixels[i].gamma : pixels[i];
                    var c = (Color32)value; var offset = i * 4;
                    bytes[offset] = c.r; bytes[offset + 1] = c.g; bytes[offset + 2] = c.b; bytes[offset + 3] = c.a;
                }
                return (bytes, ArtworkReadPath.ReadableTexture);
            }
            catch when (options.AllowGpuReadback) { /* Readable compressed formats can still reject GetPixels. */ }
        }
        if (!options.AllowGpuReadback || !SystemInfo.supportsRenderTextures) throw new InvalidOperationException("GPU readback disabled or unavailable");
        var previousTarget = RenderTexture.active;
        var previousSrgb = GL.sRGBWrite;
        RenderTexture? target = null; Texture2D? readback = null;
        byte[]? result = null;
        try
        {
            // Display RGB for artwork, raw red channel for split alpha. Explicit
            // sRGB settings avoid inheriting the caller's current render target state.
            var encode = !rawData || sourceSrgb;
            target = RenderTexture.GetTemporary(crop.Width, crop.Height, 0, RenderTextureFormat.ARGB32,
                encode ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            if (target == null) throw new InvalidOperationException("temporary render target unavailable");
            target.filterMode = FilterMode.Point;
            GL.sRGBWrite = encode && linearProject;
            Graphics.Blit(texture, target, new Vector2((float)crop.Width / texture.width, (float)crop.Height / texture.height),
                new Vector2((float)crop.X / texture.width, (float)crop.Y / texture.height));
            RenderTexture.active = target;
            readback = new Texture2D(crop.Width, crop.Height, TextureFormat.RGBA32, false);
            readback.ReadPixels(new Rect(0, 0, crop.Width, crop.Height), 0, 0, false);
            var pixels = readback.GetPixels32();
            if (pixels is null || pixels.Length != (long)crop.Width * crop.Height) throw new InvalidOperationException("GPU pixel count mismatch");
            result = new byte[checked(crop.Width * crop.Height * 4)];
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i]; var offset = i * 4;
                result[offset] = c.r; result[offset + 1] = c.g; result[offset + 2] = c.b; result[offset + 3] = c.a;
            }
        }
        finally
        {
            // Each cleanup is attempted even if another restoration fails. Never
            // destroy or change the source Sprite/Texture owned by the game/Mod.
            var failures = new List<Exception>();
            try { RenderTexture.active = previousTarget; } catch (Exception error) { failures.Add(error); }
            try { GL.sRGBWrite = previousSrgb; } catch (Exception error) { failures.Add(error); }
            try { if (readback != null) UnityEngine.Object.Destroy(readback); } catch (Exception error) { failures.Add(error); }
            try { if (target != null) RenderTexture.ReleaseTemporary(target); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new ArtworkCleanupException();
        }
        return (result!, ArtworkReadPath.GpuReadback);
    }

    private static SpriteArtworkResult Failure(SpriteArtworkStatus status, string code, Exception? error = null)
        => new(status, Reason: "EZAPI_ARTWORK_" + code + (error is null ? "" : ": " + error.GetType().Name));
    private sealed class ArtworkCleanupException : InvalidOperationException { }
}

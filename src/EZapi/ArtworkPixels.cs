namespace EZapi;

/// <summary>A configurable content heuristic, not a sale-eligibility or artistic-quality rule.</summary>
public sealed record ArtworkOptions
{
    /// <summary>Alpha byte1..255; pixels below this threshold contribute neither visible count nor hidden RGB.</summary>
    public byte MinimumAlpha { get; init; } = 16;
    /// <summary>Detail requires at least one RGB byte strictly greater than this0..255 threshold.</summary>
    public byte MaximumDarkChannel { get; init; } = 8;
    /// <summary>Absolute detail-pixel minimum; default2 rejects a lone stray bright texel. Range1..1048576.</summary>
    public int MinimumDetailPixels { get; init; } = 2;
    /// <summary>Finite0..1 fraction of visible pixels, rounded upward; combined with the absolute minimum.</summary>
    public double MinimumDetailFraction { get; init; } = 0.01;
    /// <summary>Whole-crop pixel ceiling1..1048576, checked before reads; never returns a truncated sample.</summary>
    public int MaximumPixels { get; init; } = 1024 * 1024;
    /// <summary>Allow synchronous cropped GPU readback when native CPU pixels are unavailable.</summary>
    public bool AllowGpuReadback { get; init; } = true;
}

/// <summary>Transparent means no pixels meet MinimumAlpha; Dark means insufficient non-dark visible detail.</summary>
public enum ArtworkContent { Transparent, Dark, Artwork }

/// <summary>Pure managed counts; excluded atlas pixels are not included in SampledPixels or the denominator.</summary>
public sealed record ArtworkStatistics(int SampledPixels, int VisiblePixels, int DetailPixels,
    int RequiredDetailPixels, ArtworkContent Content);

/// <summary>Unity-independent RGBA content analysis. Can be used offline with caller-owned bytes.</summary>
public static class ArtworkPixels
{
    /// <summary>Hard whole-crop ceiling; consumers may lower it through ArtworkOptions.MaximumPixels.</summary>
    public const int HardMaximumPixels = 1024 * 1024;

    /// <summary>RGBA byte order, four bytes per pixel, exact width*height. Does not copy or mutate the input.</summary>
    public static ArtworkStatistics AnalyzeRgba(int width, int height, byte[] rgba, ArtworkOptions? options = null)
        => Analyze(width, height, rgba, null, Validate(options));

    internal static ArtworkOptions Validate(ArtworkOptions? options)
    {
        var value = options ?? new();
        if (value.MaximumPixels < 1 || value.MaximumPixels > HardMaximumPixels || value.MinimumAlpha == 0 ||
            value.MinimumDetailPixels < 1 || value.MinimumDetailPixels > HardMaximumPixels ||
            !double.IsFinite(value.MinimumDetailFraction) || value.MinimumDetailFraction < 0 || value.MinimumDetailFraction > 1)
            throw new ArgumentException("EZAPI_ARTWORK_OPTIONS: nonzero alpha/detail thresholds, fraction0..1 and pixel budget1..1048576 required.");
        return value;
    }

    internal static int Area(int width, int height, ArtworkOptions options)
    {
        if (width < 1 || height < 1) throw new ArgumentException("EZAPI_ARTWORK_SIZE: positive dimensions required.");
        var area = (long)width * height;
        if (area > options.MaximumPixels) throw new ArtworkLimitException();
        return (int)area;
    }

    internal static ArtworkStatistics Analyze(int width, int height, byte[] rgba, bool[]? mask, ArtworkOptions options)
    {
        var area = Area(width, height, options);
        if (rgba is null || rgba.Length != area * 4 || mask is not null && mask.Length != area)
            throw new ArgumentException("EZAPI_ARTWORK_RGBA: exact RGBA length required.");
        var sampled = 0; var visible = 0; var detail = 0;
        for (var pixel = 0; pixel < area; pixel++)
        {
            if (mask is not null && !mask[pixel]) continue;
            sampled++;
            var offset = pixel * 4;
            if (rgba[offset + 3] < options.MinimumAlpha) continue;
            visible++;
            if (Math.Max(rgba[offset], Math.Max(rgba[offset + 1], rgba[offset + 2])) > options.MaximumDarkChannel) detail++;
        }
        if (sampled == 0) throw new ArgumentException("EZAPI_ARTWORK_MESH: mesh covers no pixel centers.");
        var required = Math.Max(options.MinimumDetailPixels, (int)Math.Ceiling(visible * options.MinimumDetailFraction));
        return new(sampled, visible, detail, required, visible == 0 ? ArtworkContent.Transparent : detail >= required ? ArtworkContent.Artwork : ArtworkContent.Dark);
    }
}

/// <summary>Native atlas coordinates, not the displayed item shape, pivot or unpacked sprite size.</summary>
public sealed record ArtworkCrop(int X, int Y, int Width, int Height);

internal sealed class ArtworkLimitException : InvalidOperationException
{
    internal ArtworkLimitException() : base("EZAPI_ARTWORK_LIMIT: pixel/mesh work budget exceeded; no partial result.") { }
}

/// <summary>Rasterize actual sprite UV triangles to exclude neighbouring tightly packed atlas artwork.</summary>
internal static class ArtworkMesh
{
    internal const int MaximumVertices = 1024, MaximumIndices = 6144;
    internal const long MaximumRasterWork = 8 * 1024 * 1024;

    internal static (ArtworkCrop Crop, bool[] Mask) Build(int textureWidth, int textureHeight, float[] uv, ushort[] indices,
        ArtworkCrop? rectangle, ArtworkOptions options)
    {
        if (textureWidth < 1 || textureHeight < 1 || textureWidth > 65536 || textureHeight > 65536 || uv is null ||
            uv.Length < 6 || uv.Length % 2 != 0 || uv.Length / 2 > MaximumVertices || indices is null ||
            indices.Length < 3 || indices.Length % 3 != 0 || indices.Length > MaximumIndices)
            throw new ArgumentException("EZAPI_ARTWORK_MESH: bounded UV/triangle geometry required.");
        var points = new (double X, double Y)[uv.Length / 2];
        for (var i = 0; i < points.Length; i++)
        {
            var u = uv[i * 2]; var v = uv[i * 2 + 1];
            if (!float.IsFinite(u) || !float.IsFinite(v) || u < 0 || u > 1 || v < 0 || v > 1)
                throw new ArgumentException("EZAPI_ARTWORK_MESH: UV outside native texture.");
            points[i] = ((double)u * textureWidth, (double)v * textureHeight);
        }
        if (indices.Any(i => i >= points.Length)) throw new ArgumentException("EZAPI_ARTWORK_MESH: invalid triangle index.");
        var x = (int)Math.Floor(points.Min(p => p.X)); var y = (int)Math.Floor(points.Min(p => p.Y));
        var maxX = (int)Math.Ceiling(points.Max(p => p.X)); var maxY = (int)Math.Ceiling(points.Max(p => p.Y));
        var crop = rectangle ?? new(x, y, maxX - x, maxY - y);
        if (crop.X < 0 || crop.Y < 0 || (long)crop.X + crop.Width > textureWidth || (long)crop.Y + crop.Height > textureHeight)
            throw new ArgumentException("EZAPI_ARTWORK_CROP: rectangle outside texture.");
        if (points.Any(p => p.X < crop.X - 0.01 || p.Y < crop.Y - 0.01 || p.X > (long)crop.X + crop.Width + 0.01 || p.Y > (long)crop.Y + crop.Height + 0.01))
            throw new ArgumentException("EZAPI_ARTWORK_CROP: UV geometry disagrees with native rectangle.");
        var mask = new bool[ArtworkPixels.Area(crop.Width, crop.Height, options)];
        long work = 0;
        for (var triangle = 0; triangle < indices.Length; triangle += 3)
        {
            var a = points[indices[triangle]]; var b = points[indices[triangle + 1]]; var c = points[indices[triangle + 2]];
            var area = Edge(a, b, c.X, c.Y);
            if (Math.Abs(area) < 1e-10) continue;
            var left = Math.Max(crop.X, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
            var bottom = Math.Max(crop.Y, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            var right = Math.Min(crop.X + crop.Width, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            var top = Math.Min(crop.Y + crop.Height, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            if (left >= right || bottom >= top) continue;
            work += (long)(right - left) * (top - bottom);
            if (work > MaximumRasterWork) throw new ArtworkLimitException();
            for (var py = bottom; py < top; py++) for (var px = left; px < right; px++)
            {
                var ab = Edge(a, b, px + 0.5, py + 0.5); var bc = Edge(b, c, px + 0.5, py + 0.5); var ca = Edge(c, a, px + 0.5, py + 0.5);
                if (area > 0 ? ab >= -1e-8 && bc >= -1e-8 && ca >= -1e-8 : ab <= 1e-8 && bc <= 1e-8 && ca <= 1e-8)
                    mask[(py - crop.Y) * crop.Width + px - crop.X] = true;
            }
        }
        if (!mask.Any(value => value)) throw new ArgumentException("EZAPI_ARTWORK_MESH: mesh covers no pixel centers.");
        return (crop, mask);
    }

    private static double Edge((double X, double Y) a, (double X, double Y) b, double x, double y)
        => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}

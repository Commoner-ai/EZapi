using EZapi;
namespace Author.Artwork;

/// <summary>Pure managed helper for caller-owned RGBA bytes, usable without Unity or the game.</summary>
public static class Artwork
{
    public static ArtworkStatistics Inspect(int width, int height, byte[] rgba) =>
        ArtworkPixels.AnalyzeRgba(width, height, rgba, new ArtworkOptions { MinimumDetailPixels = 2 });
}

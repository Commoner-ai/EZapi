using EZapi;
using Il2Cpp;
namespace Author.Stock;

/// <summary>Example author policy: reject unconfirmed, transparent and dark stock art. Not a framework-wide rule.</summary>
public static class StockArtwork
{
    /// <summary>Call explicitly on the current game main thread, not from each card's OnGUI.</summary>
    public static ArtworkStatistics RequireVisible(GameItem item)
    {
        var result = SpriteArtworkApi.InspectItem(item);
        if (result.Status != SpriteArtworkStatus.Inspected)
            throw new InvalidOperationException(result.Reason);
        if (result.Statistics!.Content != ArtworkContent.Artwork)
            throw new InvalidOperationException("Author policy rejected image content: " + result.Statistics.Content);
        // Item value, legality, source generation and delivery eligibility are separate checks.
        return result.Statistics;
    }
}

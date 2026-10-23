using EZapi;
using Il2Cpp;
namespace Author.PreparedRewards;

/// <summary>A prepared object is a native factory result, not a delivered/persisted reward.</summary>
public static class PreparedReward
{
    /// <summary>Use only from a current loaded main-thread callback. Caller owns exactly one detached returned object.</summary>
    public static GameItem CreateSecurityCrate()
    {
        var result = PreparedItemApi.Create(PreparedItemKind.SecurityCrate);
        if (result.Status != PreparedItemStatus.Created || result.Item is not {} item)
            throw new InvalidOperationException(result.Status + "; attempted=" + result.FactoryAttempted + "; cleanup=" + result.Cleanup + "; " + result.Reason);
        // Root ID, recipe, nonempty native graph, runtime lock and root ownership were checked.
        // Preserve randomized native contents. Do not call a fill/refill factory again.
        return item;
    }

    /// <summary>Native randomness is rolled exactly once; fixed heat is a separate explicit author policy.</summary>
    public static (GameItem Item, ItemOriginWriteResult Origin) CreateStolenModule()
    {
        var result = PreparedItemApi.Create(PreparedItemKind.PerformanceModule);
        if (result.Status != PreparedItemStatus.Created || result.Item is not {} item)
            throw new InvalidOperationException(result.Reason);
        var origin = ItemOriginApi.StageStolen(item, heatPercent: 100);
        // Retain the successfully created object even if the origin write is uncertain;
        // the caller must reconcile/clean up deliberately, never roll a second random module.
        return (item, origin);
    }
}

using EZapi;
using Il2Cpp;

namespace Author.Origin;

/// <summary>Call explicitly on the current main thread, after creating a detached delivery item.</summary>
public static class Origin
{
    /// <summary>Only changes memory state. Caller owns payment, delivery, cleanup and persistence.</summary>
    public static ItemOriginWriteResult Prepare(GameItem item, bool stolen) => stolen
        ? ItemOriginApi.StageStolen(item, heatPercent: 100)
        : ItemOriginApi.StageClean(item);

    /// <summary>Indeterminate is deliberately excluded: stop and reconcile instead of blind delivery.</summary>
    public static bool CanContinue(ItemOriginWriteResult result) =>
        result.Status is ItemOriginStatus.StagedInMemory or ItemOriginStatus.Unchanged;
}

using EZapi;
using Il2Cpp;

namespace EZapi.Sample;

/// <summary>Author-owned per-item data. Called explicitly by a gameplay action, never at mod initialization.</summary>
public static class ItemDataExamples
{
    public sealed record UpgradeState(int Level, string Note);

    /// <summary>Stages only JSON. Payment, upgrades and disk saving are separate author responsibilities.</summary>
    public static ItemDataWriteResult StageLevel(GameItem item, int level)
    {
        if (level is < 0 or > 100)
            return new(ItemDataStatus.Invalid, Reason: "Level must be between 0 and 100.");
        var current = ItemDataApi.Read<UpgradeState>(item, "author.example", "upgrade", 1);
        if (current.Status is not (ItemDataStatus.Missing or ItemDataStatus.Present))
            return new(current.Status, current.Json, current.Reason);
        // Read and Stage must belong to the same loaded item. No retained native object or token.
        return ItemDataApi.Stage(item, current.Revision!, new UpgradeState(level, "作者备注 / Author note"), 1);
    }
}

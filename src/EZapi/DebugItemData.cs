using System.Text.Json;
using Il2Cpp;

namespace EZapi;

/// <summary>Temporary detached native fixtures for item JSON. No player items or disk save calls.</summary>
internal static class DebugItemData
{
    internal static void Run(Action<bool, string> check)
    {
        void Check(bool ok, string name) => check(ok, "item-data: " + name);
        var run = NativeAccess.RequireRun();
        var store = GameSession.Resolve(run);
        var playerBefore = store.FindAllItem(true).ToArray().Select(x => x.uniqueId).OrderBy(x => x).ToArray();
        GameItem? item = null;
        GameItem? restored = null;
        const string owner = "ezapi.debug.itemdata";
        try
        {
            item = ItemApi.Create("ezapi.debug.factory_item", true);
            var missing = ItemDataApi.Read<Data>(item, owner, "upgrade");
            Check(missing.Status == ItemDataStatus.Missing && missing.Revision is not null, "official readonly missing tag supplies revision");
            if (missing.Revision is null) throw new InvalidOperationException(missing.Reason ?? "missing revision");
            var first = ItemDataApi.Stage(item, missing.Revision, new Data(2, "测试 / Test"));
            Check(first.Status == ItemDataStatus.StagedInMemory, "official ModifyTag stages first envelope: " + first.Reason);
            var read = ItemDataApi.Read<Data>(item, owner, "upgrade");
            Check(read.Status == ItemDataStatus.Present && read.Value == new Data(2, "测试 / Test"), "typed bilingual JSON readback");
            if (read.Revision is null) throw new InvalidOperationException(read.Reason ?? "read revision absent");
            var nativeTag = item.GetTagReadonly(ItemDataApi.TagPrefix + owner + "|upgrade");
            Check(nativeTag is not null && nativeTag.IsEnabled() && nativeTag.GetString() == first.Json, "native tag matches API JSON");
            Check(ItemDataApi.Stage(item, missing.Revision, new Data(9, "stale")).Status == ItemDataStatus.Conflict, "stale raw revision refuses overwrite");
            var migrated = ItemDataApi.Stage(item, read.Revision, new Data(3, "迁移 / Migrated"), 2);
            Check(migrated.Status == ItemDataStatus.StagedInMemory, "existing official tag accepts explicit version change: " + migrated.Reason);
            var oldReader = ItemDataApi.Read<JsonElement>(item, owner, "upgrade", 1);
            Check(oldReader.Status == ItemDataStatus.VersionMismatch && oldReader.Version == 2 && oldReader.Json == migrated.Json, "version mismatch retains migration data");
            var current = ItemDataApi.Read<Data>(item, owner, "upgrade", 2);
            Check(current.Value == new Data(3, "迁移 / Migrated"), "new reader consumes migrated version");
            using (var lease = InventoryFreezeApi.TryAcquire(new[] { item }))
            {
                Check(lease is not null, "real inventory freeze acquired for detached item");
                if (lease is not null) Check(ItemDataApi.Stage(item, current.Revision!, new Data(4, "blocked"), 2).Status == ItemDataStatus.Frozen, "real frozen item refuses state write");
            }
            var another = ItemDataApi.Read<Data>(item, "ezapi.debug.other", "upgrade");
            Check(ItemDataApi.Stage(item, another.Revision!, new Data(11, "other")).Status == ItemDataStatus.StagedInMemory, "another owner can add independent data");
            Check(ItemDataApi.Read<Data>(item, owner, "upgrade", 2).Value == current.Value, "other owner preserves original data");
            var broken = ItemDataApi.TagPrefix + owner + "|broken";
            item.state.GetTag(broken).Enable().SetString("{broken"); item.SyncModifiedState();
            Check(ItemDataApi.Read<Data>(item, owner, "broken").Status == ItemDataStatus.Invalid && item.GetTagReadonly(broken)?.GetString() == "{broken", "malformed tag is preserved");
            var oversized = ItemDataApi.Read<string>(item, owner, "oversized");
            Check(ItemDataApi.Stage(item, oversized.Revision!, new string('中', ItemDataApi.MaxJsonUtf8Bytes)).Status == ItemDataStatus.Invalid && !item.state.dict.ContainsKey(ItemDataApi.TagPrefix + owner + "|oversized"), "UTF8 limit rejects before creating tag");
            // Exercise native item graph save/restore in memory; this does not prove disk persistence.
            var json = SaveGraphApi.Encode(item);
            item.Destroy(); item = null;
            restored = SaveGraphApi.Decode(json);
            Check(ItemDataApi.Read<Data>(restored, owner, "upgrade", 2).Value == new Data(3, "迁移 / Migrated"), "native graph codec restores owned JSON/version");
            Check(ItemDataApi.Read<Data>(restored, "ezapi.debug.other", "upgrade").Value == new Data(11, "other"), "native graph codec restores independent owner");
            Check(restored.parentInventory is null, "restored fixture stays detached");
        }
        finally
        {
            restored?.Destroy(); item?.Destroy();
            Check(playerBefore.SequenceEqual(store.FindAllItem(true).ToArray().Select(x => x.uniqueId).OrderBy(x => x)), "all player-owned item identities unchanged");
        }
    }
    private sealed record Data(int Level, string Note);
}

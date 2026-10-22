using System.Text.Json;
using EZapi;
using Il2Cpp;

const string owner = "author.tools", key = "author.tools.quest";
const string oldJson = "{\"version\":1,\"count\":2}", nextJson = "{\"version\":1,\"count\":3}";
var checks = new List<string>();
void Check(bool valid, string name) { if (!valid) throw new Exception(name); checks.Add(name); }
PlayerStore Reset()
{
    var player = new PlayerStore(); PlayerStore.instance = player;
    GameSession.Start(); GameSession.Observe(GameEvent.GameLoadedInit); return player;
}
RunDataResult Stage(PlayerStore player, string? expected = null, string next = oldJson)
    => RunDataApi.Stage(player, owner, key, player.runID, player.saveSlotId, expected, next);

Check(RunDataApi.Read(null, owner, key).Status == RunDataStatus.Disabled, "not initialized returns Disabled");
var p = Reset(); var d = p.modData!;
Check(RunDataApi.Read(null, owner, key).Status == RunDataStatus.Unavailable, "null player unavailable");
Check(RunDataApi.Read(new(), owner, key).Status == RunDataStatus.Unavailable, "foreign player unavailable");
Check(RunDataApi.Read(p, "Author.tools", key).Status == RunDataStatus.Invalid, "invalid owner rejected");
foreach (var invalid in new[] { "author.tools", "author.tools.", "other.mod.quest", "author.tools/quest", "author.tools.quest\n", new string('a', 321) })
    Check(RunDataApi.Read(p, owner, invalid).Status == RunDataStatus.Invalid, "invalid full key: " + invalid.Length + ":" + invalid[..Math.Min(invalid.Length, 25)]);
Check(RunDataApi.Read(p, owner, key).Status == RunDataStatus.Missing && d.Count == 0, "missing reads do not create data");
Check(RunDataApi.Read(owner, key).Status == RunDataStatus.Missing, "simple read overload resolves current player");
var capturedIdentity = GameSession.Identity!;
Check(RunDataApi.Stage(owner, "author.tools.overload", capturedIdentity, null, oldJson).Status == RunDataStatus.StagedInMemory,
    "simple stage overload uses current event snapshot");
GameSession.Observe(GameEvent.GameLoadedInit);
Check(RunDataApi.Stage(owner, "author.tools.overload", capturedIdentity, oldJson, nextJson).Status == RunDataStatus.Conflict,
    "same-run same-slot reload rejects old event snapshot");
Check(RunDataApi.Stage(owner, key, null!, null, oldJson).Status == RunDataStatus.Invalid, "null event identity invalid");
p.modData = null;
Check(RunDataApi.Read(p, owner, key).Status == RunDataStatus.Unavailable && Stage(p).Status == RunDataStatus.Unavailable && p.modData is null,
    "absent dictionary remains unavailable and unmodified");
p.modData = d;
Check(Stage(p).Status == RunDataStatus.StagedInMemory && d[key] == oldJson, "missing expected null inserts");
Check(RunDataApi.Read(p, owner, key) == new RunDataResult(RunDataStatus.Present, oldJson), "read preserves exact JSON");
var writes = d.Writes;
Check(Stage(p).Status == RunDataStatus.Conflict && d.Writes == writes, "expected null cannot overwrite existing");
Check(Stage(p, nextJson).Status == RunDataStatus.Conflict && d.Writes == writes, "wrong revision does not write");
Check(Stage(p, oldJson.Replace(":", ": ")).Status == RunDataStatus.Conflict, "comparison uses original text including whitespace");
Check(Stage(p, oldJson, nextJson).Status == RunDataStatus.StagedInMemory && d[key] == nextJson, "matching revision updates and reads back");
Check(RunDataApi.Stage(p, owner, "author.tools.absent", p.runID, 533, oldJson, nextJson).Status == RunDataStatus.Conflict,
    "expected existing cannot insert missing");
Check(RunDataApi.Stage(p, owner, key, "different", 533, nextJson, oldJson).Status == RunDataStatus.Conflict, "different run conflicts");
Check(RunDataApi.Stage(p, owner, key, p.runID, 534, nextJson, oldJson).Status == RunDataStatus.Conflict, "different slot conflicts");
Check(RunDataApi.Stage(p, owner, key, "", -1, null, oldJson).Status == RunDataStatus.Invalid, "invalid identity rejected");
foreach (var json in new[] { "", "[]", "null", "1", "{", "{\"x\":1,}", "{\"x\":/*comment*/1}" })
{
    writes = d.Writes;
    Check(Stage(p, nextJson, json).Status == RunDataStatus.Invalid && d.Writes == writes, "invalid next JSON: " + json);
}
Check(Stage(p, "[]").Status == RunDataStatus.Invalid, "expected JSON also validated");
d[key] = "broken"; writes = d.Writes;
Check(RunDataApi.Read(p, owner, key).Status == RunDataStatus.Invalid && d[key] == "broken" && d.Writes == writes,
    "corrupt stored record preserved");
var large = "{\"text\":\"" + new string('中', RunDataApi.MaxJsonCharacters - 11) + "\"}";
Check(large.Length == RunDataApi.MaxJsonCharacters, "large fixture exactly character limit");
d.Remove(key);
Check(Stage(p, next: large).Status == RunDataStatus.StagedInMemory && RunDataApi.Read(p, owner, key).Json == large,
    "1Mi characters including Unicode supported beyond simple-store quota");
writes = d.Writes;
Check(Stage(p, large, large + " ").Status == RunDataStatus.Invalid && d.Writes == writes, "over limit rejected before write");
d[key] = large + " ";
Check(RunDataApi.Read(p, owner, key).Status == RunDataStatus.Invalid, "oversized saved JSON rejected");
string Nested(int arrays) => "{\"x\":" + new string('[', arrays) + "0" + new string(']', arrays) + "}";
d.Remove(key);
Check(Stage(p, next: Nested(63)).Status == RunDataStatus.StagedInMemory, "depth 64 accepted");
Check(Stage(p, Nested(63), Nested(64)).Status == RunDataStatus.Invalid, "depth 65 rejected");

p = Reset(); d = p.modData!;
var simple = RunData.Open(owner); simple.SetJson("quest", new { count = 9 });
d["other.mod.quest"] = oldJson;
Check(Stage(p).Status == RunDataStatus.StagedInMemory && simple.TryGetJson<JsonElement>("quest", 1, out var legacy) &&
    legacy.GetProperty("count").GetInt32() == 9 && d["other.mod.quest"] == oldJson, "legacy namespace and other owners preserved");
Check(Task.Run(() => RunDataApi.Read(p, owner, key)).Result.Status == RunDataStatus.Disabled &&
    Task.Run(() => Stage(p)).Result.Status == RunDataStatus.Disabled, "off-thread reads and writes disabled");
var previous = p; p = Reset();
Check(RunDataApi.Read(previous, owner, key).Status == RunDataStatus.Unavailable, "old player rejected after reload");
Check(RunDataApi.Read(p, owner, key).Status == RunDataStatus.Missing, "new load has independent data");
GameSession.Invalidate();
Check(Stage(p).Status == RunDataStatus.Unavailable, "scene invalidation blocks writes");

// Emulate native exceptions and reentrant identity/dictionary changes around each write boundary.
p = Reset(); d = p.modData!; d.OnRead = () => throw new Exception("read fail");
Check(RunDataApi.Read(p, owner, key).Status == RunDataStatus.Unavailable && Stage(p).Status == RunDataStatus.Unavailable && d.Writes == 0,
    "prewrite read failure unavailable without writes");
p = Reset(); d = p.modData!; d.BeforeWrite = () => throw new Exception("write fail");
Check(Stage(p).Status == RunDataStatus.Indeterminate && !d.ContainsKey(key), "throwing native setter treated as unknown write");
p = Reset(); d = p.modData!; d.AfterWrite = () => throw new Exception("after write fail");
Check(Stage(p).Status == RunDataStatus.Indeterminate && d[key] == oldJson, "post-write exception does not roll back or report success");
p = Reset(); d = p.modData!; d.AfterWrite = () => d.OnRead = () => throw new Exception("verify fail");
Check(Stage(p).Status == RunDataStatus.Indeterminate && d[key] == oldJson, "readback failure requires reconciliation");
p = Reset(); d = p.modData!; d.AfterWrite = () => p.modData = new();
Check(Stage(p).Status == RunDataStatus.Indeterminate && d[key] == oldJson && p.modData!.Count == 0, "dictionary replacement after write indeterminate");
p = Reset(); d = p.modData!; d.AfterWrite = () => { d.AfterWrite = null; d[key] = nextJson; };
Check(Stage(p).Status == RunDataStatus.Indeterminate && d[key] == nextJson, "changed value after write indeterminate");
p = Reset(); d = p.modData!; d.AfterWrite = () => GameSession.Invalidate();
Check(Stage(p).Status == RunDataStatus.Indeterminate, "epoch change after write indeterminate");
p = Reset(); d = p.modData!; d.OnRead = () => { d.OnRead = null; p.modData = new(); };
Check(Stage(p).Status == RunDataStatus.Conflict && d.Writes == 0, "dictionary replacement in preflight conflicts");
p = Reset(); d = p.modData!; var reads = 0;
d.OnRead = () => { if (++reads == 2) GameSession.Invalidate(); };
Check(Stage(p).Status == RunDataStatus.Conflict && d.Writes == 0, "second preflight identity change blocks write");
p = Reset(); d = p.modData!; d.OnRead = () => { d.OnRead = null; p.modData = new(); };
Check(RunDataApi.Read(p, owner, key).Status == RunDataStatus.Unavailable, "read rejects changed dictionary");
GameSession.Stop();
Check(RunDataApi.Read(p, owner, key).Status == RunDataStatus.Disabled, "shutdown disables records");
var report = new { passed = checks.Count, failed = 0, checks, nativeRuntimeVerified = false,
    scope = "real RunDataApi, RunData and GameSession; native player/dictionary substituted; no disk persistence or game launch" };
Directory.CreateDirectory("work");
File.WriteAllText("work/ezapi-run-record-tests.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));

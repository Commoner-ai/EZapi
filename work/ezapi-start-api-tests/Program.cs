using EZapi;
using EZapi.Registration;
using Il2Cpp;

var cases = new List<(string Name, Func<bool> Test)>();
void Add(string name, Func<bool> test) => cases.Add((name, test));
void Reset()
{
    StartApi.Stop();
    ItemRegistry.Started = true;
    ItemRegistry.TooLate = false;
    StartApi.Start();
}
bool Result(SubmitResult result, SubmitStatus status) => result.Status == status;

Add("not ready returns Invalid", () => {
    StartApi.Stop();
    return Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.start", 2), SubmitStatus.Invalid);
});
Add("official native enum overload registers", () => {
    Reset();
    var result = StartApi.RegisterStart("ezapi.sample", "ezapi.sample.apprentice", NewGameData.StartType.APPRENTICE);
    var view = StartApi.Snapshot().Single();
    return result.Accepted && result.Status == SubmitStatus.Accepted && view.OwnerId == "ezapi.sample" &&
        view.StartId == "ezapi.sample.apprentice" && view.NativeStartType == 2;
});
Add("integer overload accepts current native enum values", () => {
    Reset();
    for (var value = 0; value <= 13; value++)
        if (!Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.type_" + value, value), SubmitStatus.Accepted)) return false;
    return StartApi.Snapshot().Count == 14;
});
Add("unknown enum value is rejected", () => {
    Reset();
    return Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.invalid", 14), SubmitStatus.Invalid) && StartApi.Snapshot().Count == 0;
});
Add("negative enum value is rejected", () => {
    Reset();
    return Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.invalid", -1), SubmitStatus.Invalid);
});
Add("same mapping is idempotent", () => {
    Reset();
    var first = StartApi.RegisterStart("ezapi.sample", "ezapi.sample.apprentice", 2);
    var second = StartApi.RegisterStart("ezapi.sample", "ezapi.sample.apprentice", 2);
    return first.Status == SubmitStatus.Accepted && second.Status == SubmitStatus.AlreadyPresent && second.Accepted && StartApi.Snapshot().Count == 1;
});
Add("same ID with a different native type conflicts", () => {
    Reset();
    StartApi.RegisterStart("ezapi.sample", "ezapi.sample.apprentice", 2);
    return Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.apprentice", 3), SubmitStatus.Conflict) && StartApi.Snapshot().Count == 1;
});
Add("overlapping namespaces cannot claim one start ID", () => {
    Reset();
    StartApi.RegisterStart("ezapi", "ezapi.sample.start", 2);
    return Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.start", 2), SubmitStatus.Conflict);
});
Add("owner must be lowercase namespaced ID", () => {
    Reset();
    return Result(StartApi.RegisterStart("Bad.Mod", "Bad.Mod.start", 2), SubmitStatus.Invalid);
});
Add("start ID must be namespaced to owner", () => {
    Reset();
    return Result(StartApi.RegisterStart("ezapi.sample", "someone.else.start", 2), SubmitStatus.Invalid);
});
Add("duplicate is recognized after registration window closes", () => {
    Reset();
    StartApi.RegisterStart("ezapi.sample", "ezapi.sample.apprentice", 2);
    ItemRegistry.TooLate = true;
    return Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.apprentice", 2), SubmitStatus.AlreadyPresent);
});
Add("new mapping is rejected after registration window closes", () => {
    Reset();
    ItemRegistry.TooLate = true;
    return Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.apprentice", 2), SubmitStatus.Invalid);
});
Add("registration is main-thread only", () => {
    Reset();
    return Task.Run(() => Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.background", 2), SubmitStatus.Invalid)).Result && StartApi.Snapshot().Count == 0;
});
Add("snapshot is sorted and read-only", () => {
    Reset();
    StartApi.RegisterStart("z.mod", "z.mod.start", 2);
    StartApi.RegisterStart("a.mod", "a.mod.start", 3);
    var snapshot = StartApi.Snapshot();
    return snapshot[0].OwnerId == "a.mod" && snapshot[1].OwnerId == "z.mod" &&
        snapshot is IList<StartRegistrationView> list && list.IsReadOnly;
});
Add("stop clears registry and closes API", () => {
    Reset();
    StartApi.RegisterStart("ezapi.sample", "ezapi.sample.start", 2);
    StartApi.Stop();
    var stopped = Result(StartApi.RegisterStart("ezapi.sample", "ezapi.sample.other", 2), SubmitStatus.Invalid);
    StartApi.Start();
    return stopped && StartApi.Snapshot().Count == 0;
});

var results = new List<object>();
foreach (var test in cases)
{
    Reset();
    try
    {
        var passed = test.Test();
        results.Add(new { name = test.Name, passed });
    }
    catch (Exception error)
    {
        results.Add(new { name = test.Name, passed = false, error = error.ToString() });
    }
}
var failed = results.Count(item => !(bool)item.GetType().GetProperty("passed")!.GetValue(item)!);
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
{
    suite = "start-api",
    passed = results.Count - failed,
    failed,
    cases = results,
    realProductSources = new[] { "StartApi.cs", "Registration.cs" },
    currentNativeEnum = "NewGameData.StartType 0..13 verified from game reference assembly",
    scope = "registration, ownership, idempotence, duplicate/conflict, phase/thread and snapshot semantics; no native menu or starting-loadout mutation"
}));
if (failed != 0) Environment.Exit(1);

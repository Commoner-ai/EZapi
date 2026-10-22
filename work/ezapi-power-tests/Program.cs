using EZapi;
using Il2Cpp;

var checks = new List<(string Name, bool Passed)>();
void Check(bool condition, string name) => checks.Add((name, condition));
void Reject(Action action, string name)
{
    try { action(); Check(false, name); }
    catch (ArgumentException) { Check(true, name); }
}
GameItem Item(int id, int units = 1)
{
    return new() { Pointer = new IntPtr(id), uniqueId = id, unitCount = units };
}
void Reset(GameItem item, int energy = 100)
{
    PowerHelper.Reset();
    PowerHelper.SetEnergy(item, energy);
    GameSession.Identity = new();
    GameSession.OnThread = true;
}

var sources = new[] { new PowerSourceAmount(3, 4), new PowerSourceAmount(1, 7), new PowerSourceAmount(2, 5) };
var plan = PowerBatchMath.Plan(sources, 10);
Check(plan.Select(x => x.InstanceId).SequenceEqual(new[] { 1, 2 }) && plan.Select(x => x.Debit).SequenceEqual(new[] { 7, 3 }), "planner orders stable IDs and allocates exact cost");
Check(plan.Sum(x => x.Debit) == 10 && plan.All(x => x.Debit > 0 && x.Debit <= x.Before), "planner never exceeds snapshot energy");
Check(PowerBatchMath.Plan(sources, 0).Count == 0, "zero-cost plan is empty");
Check(PowerBatchMath.Plan(sources, 17).Count == 0, "insufficient total energy has no partial plan");
var overflowPlan = PowerBatchMath.Plan(new[] { new PowerSourceAmount(1, int.MaxValue), new PowerSourceAmount(2, int.MaxValue) }, int.MaxValue);
Check(overflowPlan.Count == 1 && overflowPlan[0].Debit == int.MaxValue, "aggregate total uses 64-bit arithmetic");
Reject(() => PowerBatchMath.Plan(sources, -1), "negative cost rejected");
Reject(() => PowerBatchMath.Plan(new[] { new PowerSourceAmount(0, 5) }, 1), "nonpositive instance ID rejected");
Reject(() => PowerBatchMath.Plan(new[] { new PowerSourceAmount(1, -1) }, 1), "negative source energy rejected");
Reject(() => PowerBatchMath.Plan(new[] { new PowerSourceAmount(1, 5), new PowerSourceAmount(1, 6) }, 1), "duplicate instance ID rejected");

var single = Item(10);
Reset(single);
var debit = Power.TryDebit(single, 7);
Check(debit.Status == EnergyDebitStatus.Debited && debit.Before == 100 && debit.After == 93, "single source exact native debit");

var stacked = Item(11, 5);
Reset(stacked);
debit = Power.TryDebit(stacked, 7);
Check(debit.Status == EnergyDebitStatus.Debited && debit.After == 93 && stacked.unitCount == 5, "stack source debits once without scaling or splitting stack");

var insufficient = Item(12, 3);
Reset(insufficient, 2);
debit = Power.TryDebit(insufficient, 7);
Check(debit.Status == EnergyDebitStatus.Insufficient && Power.GetEnergy(insufficient) == 2, "insufficient energy does not write");

var zero = Item(13, 4);
Reset(zero);
var drawCalls = PowerHelper.DrawCalls;
debit = Power.TryDebit(zero, 0);
Check(debit.Status == EnergyDebitStatus.Debited && PowerHelper.DrawCalls == drawCalls && Power.GetEnergy(zero) == 100, "zero debit requires no write");

var denied = Item(14);
Reset(denied);
PowerHelper.CanDrawOverride = (_, _) => false;
debit = Power.TryDebit(denied, 0);
Check(debit.Status == EnergyDebitStatus.Rejected && PowerHelper.DrawCalls == 0, "native CanDraw rejection preserved even at zero cost");

var restored = Item(15);
Reset(restored);
PowerHelper.DrawOverride = (item, amount) => { PowerHelper.SetEnergy(item, 40); return false; };
debit = Power.TryDebit(restored, 7);
Check(debit.Status == EnergyDebitStatus.Restored && debit.After == 100, "partial rejected native write restores exact energy");

var thrown = Item(16);
Reset(thrown);
PowerHelper.DrawOverride = (item, amount) => { PowerHelper.SetEnergy(item, 89); throw new InvalidOperationException("draw failed"); };
debit = Power.TryDebit(thrown, 7);
Check(debit.Status == EnergyDebitStatus.Restored && Power.GetEnergy(thrown) == 100, "draw exception after mutation is compensated");

var staleCount = Item(17, 5);
Reset(staleCount);
PowerHelper.AfterDraw = (item, _) => item.unitCount--;
debit = Power.TryDebit(staleCount, 7);
Check(debit.Status == EnergyDebitStatus.RecoveryFailed && Power.GetEnergy(staleCount) == 93, "changed stack identity is never blindly restored");

var staleId = Item(18);
Reset(staleId);
PowerHelper.AfterDraw = (item, _) => item.uniqueId++;
debit = Power.TryDebit(staleId, 7);
Check(debit.Status == EnergyDebitStatus.RecoveryFailed && Power.GetEnergy(staleId) == 93, "changed instance ID is never blindly restored");

var wrongOwner = Item(19);
Reset(wrongOwner); wrongOwner.Owned = false;
debit = Power.TryDebit(wrongOwner, 7);
Check(debit.Status == EnergyDebitStatus.Rejected && Power.GetEnergy(wrongOwner) == 100, "non-owned source is not written");

var detached = Item(20);
Reset(detached); detached.parentInventory = null;
debit = Power.TryDebit(detached, 7);
Check(debit.Status == EnergyDebitStatus.Rejected && Power.GetEnergy(detached) == 100, "detached source is not written");

var external = Item(21); external.External = true;
Reset(external);
debit = Power.TryDebit(external, 7);
Check(debit.Status == EnergyDebitStatus.Rejected && debit.Error?.Contains("EZAPI_EXTERNAL_POWER_CONTEXT", StringComparison.Ordinal) == true, "external provider still requires machine transaction context");

var reentrant = Item(22);
Reset(reentrant);
EnergyDebitResult? nested = null;
PowerHelper.AfterDraw = (item, _) => nested = Power.TryDebit(item, 1);
debit = Power.TryDebit(reentrant, 7);
Check(debit.Status == EnergyDebitStatus.Debited && nested?.Status == EnergyDebitStatus.Reentrant, "same-item nested debit rejected");

var failedRestore = Item(23);
Reset(failedRestore);
PowerHelper.ThrowOnRestore = true;
PowerHelper.DrawOverride = (item, _) => { PowerHelper.SetEnergy(item, 50); return false; };
debit = Power.TryDebit(failedRestore, 7);
Check(debit.Status == EnergyDebitStatus.RecoveryFailed, "restore exception reports recovery failure");

var negativeThrown = false;
try { Power.TryDebit(single, -1); }
catch (ArgumentOutOfRangeException) { negativeThrown = true; }
Check(negativeThrown, "negative native debit rejected");

var report = checks.Select(x => new { name = x.Name, passed = x.Passed }).ToArray();
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { passed = checks.Count(x => x.Passed), failed = checks.Count(x => !x.Passed), checks = report }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
if (checks.Any(x => !x.Passed)) Environment.ExitCode = 1;

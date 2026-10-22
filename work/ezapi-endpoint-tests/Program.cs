using System.Text.Json;
using EZapi;

const int slot = 533;
const string run = "fresh-run";
var endpoint = new InventoryEndpointSaved("test.mod", "test.mod.cache", "test.mod.container", 1, true, "{\"saveItems\":[]}");
var encoded = InventoryEndpointCodec.Serialize(slot, run, new[] { endpoint });
var cases = new List<(string Name, Func<bool> Run)>();

void Add(string name, Func<bool> test) => cases.Add((name, test));
void Reject(string name, string? json, int expectedSlot = slot, string expectedRun = run)
    => Add(name, () => {
        try { InventoryEndpointCodec.Parse(json!, expectedSlot, expectedRun); return false; }
        catch (InvalidDataException) { return true; }
    });

Add("round trip preserves all endpoint fields", () => {
    var value = InventoryEndpointCodec.Parse(encoded, slot, run);
    return value.Version == 1 && value.SlotId == slot && value.RunId == run && value.Endpoints.Count == 1 && value.Endpoints[0] == endpoint;
});
Add("empty endpoint set round trips", () => InventoryEndpointCodec.Parse(
    InventoryEndpointCodec.Serialize(slot, run, Array.Empty<InventoryEndpointSaved>()), slot, run).Endpoints.Count == 0);
Reject("slot mismatch", encoded, slot + 1);
Reject("run mismatch", encoded, slot, "different-run");
Reject("negative expected slot", encoded, -1);
Reject("empty expected run", encoded, slot, " ");
Reject("null input", null);
Reject("malformed JSON", "{");
Reject("wrong root type", "[]");
Reject("unknown top-level field", encoded.Replace("\"Version\"", "\"Future\":0,\"Version\"", StringComparison.Ordinal));
Reject("duplicate top-level property", encoded.Replace("\"Version\":1", "\"Version\":1,\"Version\":1", StringComparison.Ordinal));
Reject("future version", encoded.Replace("\"Version\":1", "\"Version\":2", StringComparison.Ordinal));
Reject("missing endpoint array", encoded.Replace(",\"Endpoints\":[", "", StringComparison.Ordinal));
Reject("wrong endpoint item namespace", encoded.Replace("test.mod.container", "other.container", StringComparison.Ordinal));
Reject("wrong endpoint namespace", encoded.Replace("test.mod.cache", "other.cache", StringComparison.Ordinal));
Reject("unknown endpoint field", encoded.Replace("\"Graph\"", "\"Future\":0,\"Graph\"", StringComparison.Ordinal));
Reject("duplicate endpoint field", encoded.Replace("\"InstanceId\":1", "\"InstanceId\":1,\"InstanceId\":1", StringComparison.Ordinal));
Reject("empty graph", JsonSerializer.Serialize(new InventoryEndpointDocument(1, slot, run, new[] { endpoint with { Graph = "" } })));
Reject("null graph", JsonSerializer.Serialize(new InventoryEndpointDocument(1, slot, run, new[] { endpoint with { Graph = null! } })));
Reject("non-boolean charging policy", encoded.Replace("\"ChargingOnly\":true", "\"ChargingOnly\":1", StringComparison.Ordinal));
Reject("duplicate endpoint ID", JsonSerializer.Serialize(new InventoryEndpointDocument(1, slot, run, new[] { endpoint, endpoint })));
Reject("duplicate root instance ID", JsonSerializer.Serialize(new InventoryEndpointDocument(1, slot, run, new[] { endpoint, endpoint with { EndpointId = "test.mod.other" } })));
Reject("zero instance ID", JsonSerializer.Serialize(new InventoryEndpointDocument(1, slot, run, new[] { endpoint with { InstanceId = 0 } })));
Reject("negative instance ID", JsonSerializer.Serialize(new InventoryEndpointDocument(1, slot, run, new[] { endpoint with { InstanceId = -1 } })));

var maximum = Enumerable.Range(1, InventoryEndpointCodec.MaxEndpoints)
    .Select(i => new InventoryEndpointSaved("test.mod", "test.mod.e" + i, "test.mod.container", i, false, "graph"))
    .ToArray();
Add("128 endpoint limit is inclusive", () => InventoryEndpointCodec.Parse(
    InventoryEndpointCodec.Serialize(slot, run, maximum), slot, run).Endpoints.Count == InventoryEndpointCodec.MaxEndpoints);
Reject("129 endpoint limit", JsonSerializer.Serialize(new InventoryEndpointDocument(1, slot, run,
    maximum.Append(new InventoryEndpointSaved("test.mod", "test.mod.e129", "test.mod.container", 129, false, "graph")).ToArray())));
Add("oversize input is rejected before parsing", () => {
    try { InventoryEndpointCodec.Parse(new string(' ', InventoryEndpointCodec.MaxBytes + 1), slot, run); return false; }
    catch (InvalidDataException) { return true; }
});

var results = new List<(string Name, bool Passed, string? Error)>();
foreach (var test in cases) {
    try { results.Add((test.Name, test.Run(), null)); }
    catch (Exception error) { results.Add((test.Name, false, error.Message)); }
}
foreach (var result in results.Where(result => !result.Passed))
    Console.Error.WriteLine("FAIL " + result.Name + (result.Error is null ? "" : ": " + result.Error));
var passed = results.Count(result => result.Passed);
var failed = results.Count - passed;
Console.WriteLine(JsonSerializer.Serialize(new {
    suite = "inventory-endpoint-codec", passed, failed,
    cases = results.Select(result => new { name = result.Name, passed = result.Passed, error = result.Error }).ToArray(),
    target = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    linkedRealProductSources = new[] { "InventoryEndpointData.cs", "PresentationResult.cs" },
    scope = "strict persistent-record parser/serializer; no mocked IL2CPP objects or saves"
}));
if (failed != 0) Environment.Exit(1);

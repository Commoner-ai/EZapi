namespace EZapi;

// This suite verifies lifecycle hooks. Keep the independent persistent endpoint runtime inert.
internal static class InventoryEndpointRuntime
{
    internal static NativeHookStatus Status()
        => new("ezapi.inventory.endpoint", false, Array.Empty<string>(), "not installed in this suite");
}

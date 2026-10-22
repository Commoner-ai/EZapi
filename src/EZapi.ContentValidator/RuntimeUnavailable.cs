namespace EZapi;
internal static class ItemRegistry
{
    internal static ItemRegistrationResult Register(ItemDefinition definition)
        =>throw new NotSupportedException("Offline CLI only parses definitions; it cannot register game items.");
    internal static IReadOnlyList<ItemRegistrationResult> RegisterBatch(IReadOnlyList<ItemDefinition> definitions)
        =>throw new NotSupportedException("Offline CLI only parses definitions; it cannot register game items.");
}

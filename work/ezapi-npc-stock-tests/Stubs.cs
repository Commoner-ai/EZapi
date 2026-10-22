namespace EZapi;
public static class ItemRegistry
{
    public static IReadOnlyList<ItemRegistrationResult> RegisterBatch(IReadOnlyList<ItemDefinition> items)
        =>throw new NotSupportedException("Offline parser tests never register native content.");
}

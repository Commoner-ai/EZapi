namespace EZapi;

/// <summary>框架声明契约。能力表示入口已实现，不表示玩家已验证对应游戏行为。</summary>
public static class FrameworkInfo
{
    public const string Version = "0.1.73";
    public const string JsonProtocolVersion = "0.1";
    private static readonly IReadOnlyList<FrameworkCapability> Capabilities = Array.AsReadOnly(new[] {
        "localization.game-language", "progression.readonly", "requirements.declarative", "logging.per-mod", "input.window-capture", "machines.terminal-id-normalization", "registration.batches", "items.register", "items.json", "items.png", "assets.embedded-sprite-atlas", "items.rectangle", "items.shape-mask", "items.text",
        "items.types", "items.custom-type-names", "items.tags", "items.use-count", "items.contraband",
        "items.native-flags", "items.sound-ids", "items.npc-stock", "tooltip.lines", "tooltip.labels", "tooltip.source-once",
        "debug.overlay", "debug.native-check", "diagnostics", "items.activation.json", "items.activation.csharp",
        "lifecycle.events", "save.mod-data", "save.item-data", "loot.tables", "packs.validation", "packs.dependencies", "items.food", "items.liquid-container", "localization.resources", "items.container", "transactions", "runtime.items", "runtime.item-inspection", "runtime.power", "runtime.liquids", "presentation.workshop",
        "presentation.owner-display-name", "presentation.module-text", "presentation.effect-text",
        "runtime.inventory-observation", "runtime.inventory-placement-preview", "runtime.inventory-transfer", "runtime.inventory-freeze", "runtime.inventory-admission", "runtime.list-inventory", "runtime.hook-validation", "runtime.save-graph", "runtime.inventory-endpoint", "lifecycle.ordered", "content.notifications", "effects.register", "production.value", "production.power-math", "runtime.liquid-transfer", "production.liquid-composition", "production.liquid-metadata", "runtime.modules", "items.night-stock", "items.native-factories", "items.module", "items.node", "machines.layout", "machines.required-power", "machines.borrowed-inventory", "machines.liquid-output", "machines.container-conditions", "machines.material-graph", "recipes.guide", "recipes.guide-images", "recipes.guide-localization", "machines.transactions", "machines.recipes", "machines.registration", "machines.automation", "machines.nightly", "machines.manufacturing-terminal", "runtime.external-power", "save.readback", "presentation.departure-buttons", "presentation.reading", "presentation.dialogue", "presentation.phone", "presentation.client-visits", "presentation.suppliers", "transactions.run-purchase", "registration.starts", "runtime.capability-snapshot"
    }.Select(id => new FrameworkCapability(id, "Preview")).ToArray());
    public static IReadOnlyList<FrameworkCapability> GetCapabilities() => Capabilities;
    internal static bool Supports(string id) => Capabilities.Any(c => c.Id == id);
}
public sealed record FrameworkCapability(string Id, string Stability);

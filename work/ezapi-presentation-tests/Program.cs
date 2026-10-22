using EZapi;
using Il2Cpp;
using Il2Cpp.ModuleEffectHelper;
using MelonLoader;

var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value) throw new Exception("FAIL: " + name);
    checks.Add(name);
}

Check(!PresentationApi.RegisterOwnerDisplayName("test.mod", "测试 Mod", "Test Mod").Accepted,
    "registration before framework start is rejected");
PresentationApi.Start();
Check(PresentationApi.RegisterOwnerDisplayName("test.mod", "测试 Mod", "Test Mod").Accepted,
    "owner display name registered");
Check(PresentationApi.ResolveOwnerDisplayName("test.mod", "zh-CN") == "测试 Mod" &&
      PresentationApi.ResolveOwnerDisplayName("test.mod", "en-US") == "Test Mod",
    "owner display name resolves by locale");
Check(!PresentationApi.RegisterOwnerDisplayName("test.mod", "重复", "Duplicate").Accepted,
    "duplicate owner display name rejected");
Check(!PresentationApi.RegisterOwnerDisplayName("other.mod", "", "Other").Accepted,
    "blank owner display name rejected");

Check(PresentationApi.GetRegisteredItemName("test.mod.json") == "JSON name" &&
      PresentationApi.GetRegisteredItemDescription("test.mod.json") == "JSON description",
    "JSON registered name and description exposed");
Check(PresentationApi.GetRegisteredItemName("test.mod.native", true) == "Native EN" &&
      PresentationApi.GetRegisteredItemName("test.mod.native") == "Native 中文" &&
      PresentationApi.GetRegisteredItemDescription("test.mod.native", true) == "Description EN",
    "DLL registered fields honor the explicit language selector");
Check(PresentationApi.GetRegisteredItemName("unknown.item") is null &&
      PresentationApi.GetRegisteredItemDescription("unknown.item") is null,
    "unknown registered item returns null");

var callbackCalls = 0;
var observedEnglish = false;
Check(PresentationApi.RegisterModuleText("test.mod", "test.mod.module", (item, english) => {
        callbackCalls++; observedEnglish = english;
        return new ModuleText("Additional effect", new[] { "Line A", "Line B" });
    }).Accepted,
    "module callback registered");
Check(!PresentationApi.RegisterModuleText("test.mod", "other.mod.module", (_, _) => null).Accepted,
    "cross-owner module ID rejected");
Check(!PresentationApi.RegisterModuleText("test.mod", "test.mod.unknown", (_, _) => null).Accepted,
    "module text requires an existing owned module declaration");
Check(!PresentationApi.RegisterModuleText("test.mod", "test.mod.module", (_, _) => null).Accepted,
    "duplicate module callback rejected");
var item = new GameItem { identifier = "test.mod.module", Pointer = new(12) };
ItemRegistry.OwnedModule = false;
Check(!PresentationApi.TryGetModuleText(item.identifier, item, out _) && callbackCalls == 0,
    "module callback stays dormant when the item is not this owner's applied module");
ItemRegistry.OwnedModule = true;
Check(PresentationApi.TryGetModuleText(item.identifier, item, out var moduleText) && callbackCalls == 1 && observedEnglish &&
      moduleText.AdditionalEffect == "Additional effect" && moduleText.Lines!.SequenceEqual(new[] { "Line A", "Line B" }),
    "module callback receives instance/language and returns copied display lines");
Check(!PresentationApi.TryGetModuleText(item.identifier, new GameItem { identifier = "foreign.item", Pointer = new(13) }, out _) && callbackCalls == 1,
    "callback is not invoked for an item whose identifier differs");
GameSession.OnThread = false;
Check(!PresentationApi.TryGetModuleText(item.identifier, item, out _) && callbackCalls == 1,
    "module callback is never invoked off the game thread");
GameSession.OnThread = true;
NativeItemLocalization.IsEnglish = false;
Check(PresentationApi.TryGetModuleText(item.identifier, item, out _) && !observedEnglish,
    "module callback receives the selected Chinese language flag");
Check(PresentationApi.RegisterModuleText("test.mod", "test.mod.other", (_, _) =>
        new ModuleText(Lines: Enumerable.Repeat("x", 129).ToArray())).Accepted,
    "second module callback registered for validation test");
Check(!PresentationApi.TryGetModuleText("test.mod.other", new GameItem { identifier = "test.mod.other" }, out _) &&
      MelonLogger.Warnings.Any(value => value.Contains("EZAPI_MODULE_TEXT_CALLBACK", StringComparison.Ordinal)),
    "oversized dynamic lines are rejected and diagnosed");

EffectApi.Declared.Add(("test.mod", "test.mod.effect"));
NativeItemLocalization.IsEnglish = true;
Check(!PresentationApi.RegisterEffectText("other.mod", "test.mod.effect", new("名称", "Name"), new("说明", "Description")).Accepted,
    "effect text requires an effect declared by the same owner");
Check(!PresentationApi.RegisterEffectText("test.mod", "test.mod.effect", new(null, null), new("说明", "Description")).Accepted,
    "localized effect name must provide nonblank text");
Check(PresentationApi.RegisterEffectText("test.mod", "test.mod.effect", new("效果名", "Effect name"), new("效果说明", "Effect description")).Accepted,
    "localized effect name and description registered");
var effect = new ModuleEffect { identifier = "test.mod.effect", displayName = "Original name", description = "Original description", Pointer = new(21) };
EffectApi.Applied.Add(effect.identifier, ("test.mod", effect));
GameSession.OnThread = false;
PresentationApi.RefreshEffectTexts();
Check(effect.displayName == "Original name" && effect.description == "Original description",
    "native effect fields are not changed off the game thread");
GameSession.OnThread = true;
PresentationApi.RefreshEffectTexts();
Check(effect.displayName == "Effect name" && effect.description == "Effect description",
    "applied owned effect is localized to English");
NativeItemLocalization.IsEnglish = false;
PresentationApi.RefreshEffectTexts();
Check(effect.displayName == "效果名" && effect.description == "效果说明",
    "owned effect updates when locale changes");
effect.displayName = "External override";
NativeItemLocalization.IsEnglish = true;
PresentationApi.RefreshEffectTexts();
Check(effect.displayName == "External override" && effect.description == "Effect description",
    "external effect field changes are preserved independently");
var foreignEffect = new ModuleEffect { identifier = effect.identifier, displayName = "Foreign name", description = "Foreign description", Pointer = new(22) };
EffectApi.Applied[effect.identifier] = ("other.mod", foreignEffect);
PresentationApi.RefreshEffectTexts();
Check(foreignEffect.displayName == "Foreign name" && foreignEffect.description == "Foreign description" &&
      effect.displayName == "External override" && effect.description == "Original description",
    "same-ID foreign replacement is never modified and stale owned text is restored");
PresentationApi.Stop();
Check(effect.displayName == "External override" && effect.description == "Original description",
    "shutdown restores only fields still owned by EZapi");
Check(PresentationApi.ResolveOwnerDisplayName("test.mod", "en-US") is null,
    "shutdown releases owner and callback registrations");

Console.WriteLine($"PASS {checks.Count}: " + string.Join("; ", checks));

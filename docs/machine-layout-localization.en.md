> 0.1.72-preview.1 packaging: developer tools default off. See [installation](install-and-debug.en.md). The 415-check/save-reload reports below apply to the recorded pre-packaging DLL hash4238A189…; they are historical for the new binary.

# Machine window and slot localization (0.1.72 development)

Declare Chinese and English text directly on a machine layout. The factory resolves the current game locale; display-name reads and reopening a window refresh text through the `ezapi.items.localization` hook group. For a custom window that stays open, call `MachineLayoutApi.RefreshLocalization(machine)` after changing the game language. See the [AI index](machine-layout-localization.index.json).

```csharp
MachineLayoutApi.Create(freshMachine, new MachineLayoutDefinition {
    LocalizedTitle = new("装配台", "Assembler"),
    Output = new() {
        Kind = "container", Size = new(3, 3),
        LocalizedLabel = new("产物容器", "Output container")
    },
    Battery = new(new(2, 2)), ManualSlot = true,
    LiquidInputs = new[] {
        new MachineContainerSlot {
            SlotId = "water", LocalizedLabel = new("净水容器", "Water vessel")
        }
    }
});
```

| Property | Standalone JSON field | Fallback |
| --- | --- | --- |
| `MachineLayoutDefinition.LocalizedTitle` | `localizedTitle` | Literal `Title`, then current item name |
| `MachineContainerSlot.LocalizedLabel` | `liquidInputs[].localizedLabel` | Literal `Label`, including an empty label |
| `MachineOutputTemplate.LocalizedLabel` | `output.localizedLabel` | “产物容器” / “Output container”; unused for the items grid |
| `MachineLayoutDefinition.LocalizedBatteryLabel` | `localizedBatteryLabel` | “电池” / “Battery” |
| `MachineLayoutDefinition.LocalizedManualLabel` | `localizedManualLabel` | “说明书” / “Manual” |

Each text object needs at least one of `chinese` or `english`. Each supplied translation must be readable, at most 200 characters, and control-free. Chinese locales (`zh*`) select Chinese; other locales select English. A missing side falls back to the supplied side. Provide both translations to support both game languages. Existing literal author text is preserved.

A standalone machine layout has no owning content-pack catalog, so CSV `key` is rejected. A DLL author can resolve a catalog before setting literal fields, or build an inline `LocalizedItemText` from both translations. Ordinary item JSON has its own [pack CSV binding path](item-localization.index.json).

Use the [complete JSON example](examples/machine-layout-bilingual.json), [schema](../schemas/machine-layout.schema.json), and [compiled sample](../src/EZapi.Sample/MachineGuideExamples.cs). Parse the file explicitly and call Create in each fresh item factory. EZapiPacks does not automatically import standalone machine layouts.

```powershell
dotnet tools/ezapi-validate/ezapi-validate.dll --machine-layout docs/examples/machine-layout-bilingual.json
```

Refresh requires the initialized game main thread. It returns `false` for an unmarked item or a stopped framework. Invalid saved declarations or mismatched dimensions, slots or parents throw before any text is changed. Automatic display callbacks contain and log exceptions. A native text setter failure may leave part of the text updated; a later successful refresh sets the visible text again without changing inventory content.

Refresh updates only window titles and slot tooltip text. It preserves window and inventory instances, admission delegates, stable IDs, power requirements and the persistent layout marker. It neither requests a save nor synchronizes item state. `TryGetInventory` remains a pure layout read. Old layouts without localized fields serialize identically; adding declarations to an existing saved layout still changes the specification and requires an explicit migration.

48 offline checks link production parsing, graph validation and refresh sources to instrumented native stubs. Current-DLL English/Chinese text, native title/tooltip, reopening and five-node save/reload checks passed; see [scoped evidence](verification/disk-supplier-native-0.1.72.json). Title strings/layout properties were checked; container-title pixel appearance was not separately reviewed. This is still a development candidate.

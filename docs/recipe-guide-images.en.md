> Current API guide. See [release status](current-status.en.md): current-DLL native acceptance is pending; older test counts apply only to their historical binaries.

# Recipe guide images

Use an already registered `RecipeGuideFigure.ItemId`. No image path or new JSON field is required. The guide creates a temporary detached default item through the current directory and calls the official `GameItemElement.ResolveItemSprite`. For automatic machine guides, `RecipeGuideDisplay.IllustrationItemId` controls the displayed item independently of actual recipe inputs/outputs.

Names, requirements and quantity labels support `LocalizedName`, `LocalizedRequirement` and `LocalizedQuantity`; machine names and notes also support typed bilingual text. See [localization](recipe-guide-localization.en.md). Guide generation never executes processing conditions, dynamic resolvers or transactions.

## Geometry and resources

The renderer uses public Sprite vertices, UVs, triangles, rect, pivot and pixelsPerUnit. It supports tight irregular meshes, quarter-turn UV rotations, transparent margins and valid arbitrary UV layouts without textureRect or packingRotation assumptions. Invalid/missing images show a text placeholder; diagnostics report the reason. It does not alter inventory shapes.

Thumbnails are at most128px on the longest side. GPU readback is limited to the UV bounding region, at most512px. Limits:4096vertices,24576indices,2million candidate pixels. Invalid/nonfinite coordinates, indices and all-degenerate triangles are rejected. Pixels outside triangles remain transparent.

Update warms at most one uncached ID per frame. Draw only uses cached resources. The LRU holds128IDs including failed lookups. Closing/reopening, catalog changes and run/scene changes release owned resources. Shared sprites/textures remain untouched; RenderTexture.active is restored on success and failure. Non-readable source textures are supported.

Factories must return detached new instances without rewards, saves or player mutations. Arbitrary third-party factory side effects cannot be undone. Images represent default instances, not live item state, material tint or animation. Close/reopen after changing source content.

## Evidence

Initial previews observe valid, successful, completed public initialization/locale/preload handles. Subsequent locale changes use the public SelectedLocaleChanged completion event without reentering the initialization getter. On050REV5-L a narrow calendar Update prefix defers text refresh while the locale event is pending; the native test deferred only2frames and then resumed. Pending lookups never run factories or cache failures; no WaitForCompletion. Locale changes release previous previews, including results invalidated inside author factories. Diagnostics expose localeState/localeError. This prevents synchronous liquid helper localization from racing language loading.

> Historical verification note (0.1.66); not evidence for the current DLL.
> Final DLL:49 offline suites; images43/0; catalog/JSON131/0; unified registration/automatic guide181/0. Nine native groups954/0 including60asset checks, guide UI7/0, actual en/zh switch8/0 including liquid previews, prior machine state19/0; all536ES3 files unchanged. See verification (historical identifier `verification/guide-mesh-native-0.1.66.json`; original artifact not distributed), public contract audit (historical identifier `recipe-guide-public-contract.audit.json`; original artifact not distributed) and [AI index](recipe-guide-images.index.json).

Earlier fixture rejections are retained: OverrideGeometry requires readable fixture data and positions in Sprite.rect pixel space. It converts pivot/ppu automatically; the vertices getter returns local coordinates. The production image reader never calls OverrideGeometry.

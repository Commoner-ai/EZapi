# Artwork inspection: black, transparent and native sprites

Use `SpriteArtworkApi.InspectItem(item)` for an existing item's raw native sprite, or `Inspect(sprite)` for an existing Sprite. Results retain managed statistics only; original artwork is neither retained nor exported. Current-DLL acceptance is recorded in [release status](release-status.json).

```csharp
var result = SpriteArtworkApi.InspectItem(item);
if (result.Status != SpriteArtworkStatus.Inspected)
{
    Log(result.Reason); // No reliable image evidence; never treat failure as transparent.
    return;
}
var content = result.Statistics!.Content; // Artwork, Dark or Transparent.
// Your Mod chooses eligibility; this API does not impose a sales rule.
```

| Task | API | Requirements |
| --- | --- | --- |
| Current item artwork | `SpriteArtworkApi.InspectItem(item,options)` | Current loaded run/main thread and known game build |
| Existing sprite/icon | `SpriteArtworkApi.Inspect(sprite,options)` | Initialized Unity main thread; no item/run required |
| Caller-owned RGBA/offline tooling | `ArtworkPixels.AnalyzeRgba(width,height,rgba,options)` | Managed only, worker threads allowed |

Buildable native consumer: [StockArtwork.cs](examples/sprite-inspection-dll/StockArtwork.cs), [project](examples/sprite-inspection-dll/Inspection.csproj). Pure RGBA consumer: [Artwork.cs](examples/artwork-dll/Artwork.cs), [project](examples/artwork-dll/Artwork.csproj); it needs only the SDK DLL, without game assemblies.

Default policy ignores alpha below16, including hidden RGB. Visible pixels have detail when any RGB channel exceeds8. Artwork requires at least `max(2,ceil(VisiblePixels*0.01))` detail pixels. A lone bright texel cannot approve a black placeholder. Zero visible pixels means Transparent; remaining insufficient detail means Dark. Adjust ArtworkOptions for your content, such as MinimumDetailPixels=1 for a valid tiny image. A real black silhouette can be Dark; this is a configurable content heuristic, not artistic-quality, style or sale-eligibility validation. Colored placeholders may pass.

Statistics report SampledPixels, VisiblePixels, DetailPixels, RequiredDetailPixels and Content. Only actual UV triangle coverage participates, including the denominator. Rectangle sprites use their textureRect; tightly packed sprites derive a crop from UVs without requesting their unsupported textureRect. Triangle masks exclude neighbouring atlas images outside the sprite mesh. Invalid/nonintegral geometry is rejected; no whole-atlas fallback is used. UV rotation/flipping remains within the same source geometry.

Readable textures use cropped GetPixels. Unreadable or unsupported compressed formats can use original-size GPU crop readback. AllowGpuReadback=false or unavailable rendering returns Unavailable, never empty evidence. Split alpha textures are supported through their corresponding raw red channel; mismatched dimensions are rejected. RGB follows current-project display color treatment, opacity remains data, and the GPU path explicitly manages sRGBWrite. Real graphics backend/color/compression behavior still requires native verification. This inspects source sprite content, not final custom materials, shaders, tint, lighting or screen appearance.

GPU cleanup independently restores RenderTexture.active and GL.sRGBWrite, destroys only the newly created readback texture, and releases the temporary render target. Every cleanup is attempted even if another fails. Cleanup failures suppress statistics and return CleanupFailed. Source sprites/textures, filters, wrapping, pixels and item state are not modified.

Default/hard maximum:1,048,576 pixels per crop; lower MaximumPixels when needed. Geometry is limited to1024 UV vertices,6144 indices and8,388,608 cumulative triangle bounding-box raster operations. Exceeding a bound rejects the entire inspection, with no partial result masquerading as complete.

Calls are synchronous. GPU ReadPixels can stall; a started native call cannot be interrupted by a frame budget. Inspect a small number per Update, not per OnGUI/card/tooltip redraw. There is no implicit scanner or cache. Cache managed results only with appropriate resource/policy/run invalidation; never retain expired Sprite/GameItem wrappers across loads. InspectItem uses RenderHandler.LoadFromAtlasRaw with the current item keys, no item factory and no intentional generic missing-art fallback. Reload/item/key changes invalidate the result; it does not replace other Mods' resource loaders.

AnalyzeRgba requires exactly width*height*4 caller-owned bytes in RGBA order and neither copies nor mutates them. Do not concurrently change the array. Invalid inputs throw parameter exceptions; limit rejection throws an exception containing EZAPI_ARTWORK_LIMIT. Native inspection instead returns explicit statuses.

Only Inspected carries content evidence. Invalid means input/geometry rejection; Unavailable means wrong thread, missing resources or read failure; LimitExceeded means budget rejection; CleanupFailed means temporary-resource or render-state restoration failed. None of these failures means Dark or Transparent. Statuses/counts/English diagnostics are locale-independent; use GameLanguage.Resolve for your own Chinese/English messages. There are no new content JSON fields or automatic sale rules.

Source-linked tests use explicit Unity texture/render/cleanup stand-ins, including modelled color branches. They do not establish real GPU/material/color-space or arbitrary-Mod correctness. Native acceptance remains pending. See the [machine index](artwork-inspection.index.json) and [error index](errors.index.json).

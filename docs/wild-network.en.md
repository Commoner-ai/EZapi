# Wild network browser

0.1.74-preview.1 keeps the original detail and unlock controls on the left and places live category tabs, search, filters and a card grid on the right. The vertical scrollbar is always visible: its handle fills the track when everything fits and becomes draggable when entries overflow. Extra categories can scroll horizontally in the tab strip.

The feature ships in the same `Mods/EZapi.dll`. Developer tools are optional.

## Mod integration

The adapter does not enumerate six hardcoded categories or known upgrade IDs. It scans only the open native window every 0.35 seconds. Active `NetworkElement` entries appended to native category roots are discovered, including late additions and new category containers. Existing headings are associated by nearby layout; localized upgrade subtitles and container names provide fallbacks. Category roots remain available, with cards temporarily moved to a scroll-content child.

- Keep stable IDs, legitimate native button callbacks and pointer events on real entries. Inactive templates stay inactive.
- Parent new elements under the native window/category hierarchy. Use a separate RectTransform container for a new category, with a nearby sibling TMP heading or a meaningful localized upgrade subtitle.
- Resolve objects again after reopening/loading. Query descendants rather than assuming every card remains an immediate category child.
- Preserve native/mod payment and unlock behavior. EZapi never unlocks, deducts currency or saves for this layout.

Search matches every query word against title, subtitle or ID. Filters are All, Purchased and Affordable. Affordability compares native `GetCost()` with current credits; it does not imply eligibility, cooldown readiness or demo availability. Unknown private costs are not guessed.

Private canvases, independent buttons without NetworkElement, and another complete replacement of this window require explicit integration. Normal additions to the native network are discovered automatically. The existing DeepSpace Hacker entry appeared in the current UI.

## Lifecycle and diagnostics

The independent `ezapi.presentation.wild-browser` group checks exact OpenUI(bool)/CloseUI signatures. Both native methods always execute. Before open/close, on scene change and at shutdown, the adapter restores original parents, sibling order, geometry and component states, revokes owned callbacks and destroys owned UI. Unsupported structures/errors restore the original presentation and log `EZAPI_WILD_BROWSER`.

The optional developer panel provides a disposable 84-card/new-category scrolling probe. It adds UI duplicates only, not upgrades or save data; closing the window removes them. Duplicates retain original IDs and must not be used to repeat purchase tests.

`wildBrowser` diagnostics include hook readiness, discovered counts, selection, filters and vertical scroll range. See [release status](release-status.json) for current scoped evidence and remaining full native acceptance.

using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EZapi;

/// <summary>
/// Adapted from the user's DeepSpace RaidEntry/MapButtonLayout. Only owns EZapi
/// clones. All native references are bound to a run, map, panel and parent, then
/// discarded on close/reload/unload. No Harmony, original-click or travel hooks.
/// </summary>
internal static class DepartureRuntime
{
    private sealed record Binding(DepartureEntry Entry, GameObject Root, RectTransform Rect, Button Button,
        TextMeshProUGUI[] Tmp, Text[] Text, Action ManagedClick, UnityAction NativeClick, long Generation);
    private static readonly Dictionary<string, Binding> Bindings = new(StringComparer.Ordinal);
    private static Il2CppStructArray<Vector3>? _corners;
    private static GameRunIdentity? _run;
    private static IntPtr _map, _panel, _parent;
    private static long _generation;
    private static float _nextRefresh, _nextClick;
    private static string? _lastError;
    private static bool _refreshing, _clicking;

    internal static void InvalidateLayout() => _nextRefresh = 0;
    private static bool Alive(Object? value) => value is not null && value.Pointer != IntPtr.Zero && value;

    internal static void Remove(string id)
    {
        if (Bindings.Remove(id, out var binding)) Destroy(binding);
        InvalidateLayout();
    }

    private static void Destroy(Binding binding)
    {
        try
        {
            if (Alive(binding.Root))
            {
                // Destroy is deferred by Unity: deactivate and revoke the listener immediately.
                binding.Root.SetActive(false);
                if (Alive(binding.Button)) binding.Button.onClick = new Button.ButtonClickedEvent();
                Object.Destroy(binding.Root);
            }
        }
        catch (Exception error) { Warn("EZAPI_DEPARTURE_CLEANUP", error); }
        finally { binding.Entry.Clear(); }
    }

    internal static void SceneChanged()
    {
        _generation++;
        foreach (var binding in Bindings.Values.ToArray()) Destroy(binding);
        Bindings.Clear();
        foreach (var entry in DepartureApi.Catalog.Entries.Values) entry.Clear();
        _run = null; _map = _panel = _parent = IntPtr.Zero;
        _corners = null; _nextRefresh = _nextClick = 0;
    }

    private static bool TryMap(out MapUIManager map, out RectTransform source, out RectTransform parent)
    {
        map = null!; source = parent = null!;
        if (!DepartureApi.IsAvailable || !GameSession.TryResolve(null, out _)) return false;
        map = MapUIManager.Instance;
        if (!Alive(map) || !Alive(map.mapPanel) || !map.mapPanel.activeInHierarchy || !Alive(map.dumpingGroundButton)) return false;
        source = map.dumpingGroundButton.GetComponent<RectTransform>();
        if (!Alive(source) || !Alive(source.parent)) return false;
        parent = source.parent.GetComponent<RectTransform>();
        return Alive(parent) && parent.gameObject.activeInHierarchy;
    }

    private static bool Current(long generation, GameRunIdentity run, IntPtr mapPointer, IntPtr panelPointer, IntPtr parentPointer)
    {
        if (generation != _generation || !GameSession.TryResolve(run.Epoch, out _) || GameSession.Identity != run) return false;
        return TryMap(out var map, out _, out var parent) && map.Pointer == mapPointer &&
            map.mapPanel.Pointer == panelPointer && parent.Pointer == parentPointer;
    }

    private static DepartureContext Context(MapUIManager map, GameRunIdentity run)
    {
        if (!GameSession.TryResolve(run.Epoch, out var player)) throw new InvalidOperationException("EZAPI_DEPARTURE_STALE");
        return new(run.Epoch, run.RunId, run.SaveSlotId, player.visitLeftTonight,
            map.inTransit || (Alive(map.confirmationPanel) && map.confirmationPanel.activeInHierarchy), GameLanguage.Current);
    }

    internal static void Maintain()
    {
        if (_refreshing || _clicking || DepartureApi.Catalog.Busy) return;
        _refreshing = true;
        try
        {
            if (DepartureApi.Catalog.Entries.Count == 0 || !TryMap(out var map, out var source, out var parent))
            {
                if (_map != IntPtr.Zero || Bindings.Count != 0) SceneChanged();
                return;
            }
            var run = GameSession.Identity!;
            if (_run != run || _map != map.Pointer || _panel != map.mapPanel.Pointer || _parent != parent.Pointer)
            {
                SceneChanged();
                _run = run; _map = map.Pointer; _panel = map.mapPanel.Pointer; _parent = parent.Pointer;
            }
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + .25f;
            long generation = _generation;
            var mapPointer = _map; var panelPointer = _panel; var parentPointer = _parent;
            bool Scope() => Current(generation, run, mapPointer, panelPointer, parentPointer);
            var context = Context(map, run);
            var visible = new List<Binding>();
            foreach (var entry in DepartureApi.Catalog.Entries.Values.OrderBy(e => e.Definition.Order).ThenBy(e => e.Definition.Id, StringComparer.Ordinal).ToArray())
            {
                if (!Scope()) { SceneChanged(); return; }
                try
                {
                    if (!DepartureApi.Catalog.Evaluate(entry, context, Scope, out var label, out var show, out var enabled) || !show)
                    {
                        entry.Visible = entry.Placed = entry.Interactable = false;
                        if (Bindings.TryGetValue(entry.Definition.Id, out var hidden) && Alive(hidden.Root)) hidden.Root.SetActive(false);
                        continue;
                    }
                    if (!Bindings.TryGetValue(entry.Definition.Id, out var binding) || !Alive(binding.Root) || !Alive(binding.Button) ||
                        !Alive(binding.Rect.parent) || binding.Rect.parent.Pointer != parent.Pointer)
                    {
                        Remove(entry.Definition.Id);
                        binding = Create(entry, map, parent, generation);
                        Bindings.Add(entry.Definition.Id, binding);
                    }
                    entry.Applied = entry.Visible = true;
                    entry.Placed = false; entry.Interactable = enabled;
                    var font = map.dumpingGroundButton.GetComponentInChildren<TextMeshProUGUI>(true)?.font;
                    foreach (var text in binding.Tmp)
                    {
                        if (!Alive(text)) continue;
                        if (text.text != label) text.text = label;
                        if (Alive(font)) text.font = font;
                        text.enableAutoSizing = true;
                    }
                    foreach (var text in binding.Text) if (Alive(text) && text.text != label) text.text = label;
                    binding.Button.interactable = enabled;
                    CopyGeometry(binding.Rect, source);
                    visible.Add(binding);
                }
                catch (Exception error)
                {
                    Remove(entry.Definition.Id);
                    entry.Error = "EZAPI_DEPARTURE_CREATE: " + error.GetType().Name;
                    Warn(entry.Error, error);
                }
            }
            if (!Scope()) { SceneChanged(); return; }
            if (visible.Count == 0) return;
            var origin = Footprint(source, parent);
            var canvas = parent.GetComponentInParent<Canvas>();
            Camera? camera = !Alive(canvas) || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            // Four screen corners also handle rotated parents. A non-clipping layout
            // parent can have a zero/small Rect: it must not suppress valid free slots.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, Vector2.zero, camera, out var a) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(Screen.width, 0), camera, out var b) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(0, Screen.height), camera, out var c) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(Screen.width, Screen.height), camera, out var d))
                throw new InvalidOperationException("EZAPI_DEPARTURE_VIEWPORT");
            var screen = Rect.MinMaxRect(Mathf.Min(a.x, b.x, c.x, d.x), Mathf.Min(a.y, b.y, c.y, d.y),
                Mathf.Max(a.x, b.x, c.x, d.x), Mathf.Max(a.y, b.y, c.y, d.y));
            var area = ToRect(screen);
            for (Transform? ancestor = parent; Alive(ancestor); ancestor = ancestor!.parent)
            {
                var mask = ancestor!.GetComponent<RectMask2D>();
                var stencil = ancestor.GetComponent<Mask>();
                if ((Alive(mask) && mask.enabled) || (Alive(stencil) && stencil.enabled))
                {
                    var clip = ancestor.GetComponent<RectTransform>();
                    if (Alive(clip)) area = Intersect(area, Bounds(clip, parent));
                }
            }
            area = area.Expand(-8);
            var occupied = new List<DepartureRect>();
            bool Ours(Transform transform) => Bindings.Values.Any(e => Alive(e.Root) && (transform.Pointer == e.Root.transform.Pointer || transform.IsChildOf(e.Root.transform)));
            foreach (var graphic in map.mapPanel.GetComponentsInChildren<Graphic>(false))
            {
                if (!Alive(graphic) || !graphic.enabled || !graphic.gameObject.activeInHierarchy || Ours(graphic.transform)) continue;
                var rect = Bounds(graphic.rectTransform, parent);
                // Keep DeepSpace's map-art filter, while retaining wide text and
                // full selectable footprints below. A decorative map image must
                // not make an otherwise empty viewport appear completely occupied.
                bool text = Alive(graphic.GetComponent<Text>()) || Alive(graphic.GetComponent<TextMeshProUGUI>());
                if (!rect.Valid || !text && (rect.Width > origin.Width * 1.5f || rect.Height > origin.Height * 1.7f)) continue;
                occupied.Add(rect);
            }
            foreach (var selectable in map.mapPanel.GetComponentsInChildren<Selectable>(false))
            {
                if (!Alive(selectable) || !selectable.gameObject.activeInHierarchy || Ours(selectable.transform)) continue;
                var rect = selectable.GetComponent<RectTransform>();
                if (Alive(rect)) occupied.Add(Footprint(rect, parent));
            }
            var left = Alive(map.upgradeMerchantButton) ? map.upgradeMerchantButton.GetComponent<RectTransform>() : null;
            float gapX = 8 * Mathf.Abs(source.localScale.x), gapY = 8 * Mathf.Abs(source.localScale.y);
            float leftX = Alive(left) && left!.parent.Pointer == parent.Pointer ? Bounds(left, parent).CenterX : origin.CenterX - 2 * (origin.Width + gapX);
            var plan = DepartureLayout.Plan(area, origin, leftX, gapX, gapY, occupied, visible.Count,
                visible.Any(e => e.Entry.Definition.ReserveLegacySlots));
            for (int i = 0; i < visible.Count; i++)
            {
                var binding = visible[i]; var entry = binding.Entry;
                if (plan[i] is not { } target)
                {
                    entry.Placed = entry.Interactable = false; entry.Error = "EZAPI_DEPARTURE_NO_SPACE";
                    binding.Button.interactable = false;
                    binding.Root.SetActive(false);
                    continue;
                }
                // Convert parent-local bounds movement into anchored movement; no hardcoded pixels.
                binding.Rect.localPosition = source.localPosition + new Vector3(target.CenterX - origin.CenterX, target.CenterY - origin.CenterY, 0);
                binding.Rect.SetAsLastSibling();
                entry.Placed = true; entry.Error = null;
                binding.Root.SetActive(true);
            }
        }
        catch (Exception error)
        {
            foreach (var binding in Bindings.Values)
            {
                binding.Entry.Placed = binding.Entry.Interactable = false;
                binding.Entry.Error = "EZAPI_DEPARTURE_LAYOUT: " + error.GetType().Name;
                try { if (Alive(binding.Root)) binding.Root.SetActive(false); } catch { /* First error is logged below. */ }
            }
            Warn("EZAPI_DEPARTURE_LAYOUT", error);
        }
        finally { _refreshing = false; }
    }

    private static Binding Create(DepartureEntry entry, MapUIManager map, RectTransform parent, long generation)
    {
        var root = Object.Instantiate(map.dumpingGroundButton, parent);
        try
        {
            root.SetActive(false);
            root.name = "EZapi.Departure." + entry.Definition.Id;
            foreach (var localizer in root.GetComponentsInChildren<TMPLocalizer>(true))
            { localizer.enabled = false; Object.Destroy(localizer); }
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour.GetIl2CppType().Name == "LocalizeStringEvent")
                { behaviour.enabled = false; Object.Destroy(behaviour); }
            (root.GetComponent<LayoutElement>() ?? root.AddComponent<LayoutElement>()).ignoreLayout = true;
            var buttons = root.GetComponentsInChildren<Button>(true);
            if (buttons.Length != 1) throw new InvalidOperationException("EZAPI_DEPARTURE_TEMPLATE: exactly one Button required.");
            var button = buttons[0];
            // Overwrite the cloned event: never retain the original dumping-ground action.
            button.onClick = new Button.ButtonClickedEvent();
            Action click = () => Click(entry, generation);
            var nativeClick = DelegateSupport.ConvertDelegate<UnityAction>(click) ?? throw new InvalidOperationException("EZAPI_DEPARTURE_DELEGATE");
            ((UnityEvent)button.onClick).AddListener(nativeClick);
            return new(entry, root, root.GetComponent<RectTransform>(), button,
                root.GetComponentsInChildren<TextMeshProUGUI>(true).ToArray(), root.GetComponentsInChildren<Text>(true).ToArray(), click, nativeClick, generation);
        }
        catch { root.SetActive(false); Object.Destroy(root); throw; }
    }

    private static void Click(DepartureEntry entry, long generation)
    {
        if (_refreshing || _clicking || Time.unscaledTime < _nextClick || _run is not { } run) return;
        _clicking = true;
        try
        {
            var mapPointer = _map; var panelPointer = _panel; var parentPointer = _parent;
            bool Scope()
            {
                if (!Current(generation, run, mapPointer, panelPointer, parentPointer) || !TryMap(out var currentMap, out _, out _)) return false;
                var current = Context(currentMap, run);
                return !current.InTransit && (!entry.Definition.RequireVisit || current.RemainingVisits > 0);
            }
            if (!Scope() || !Bindings.TryGetValue(entry.Definition.Id, out var binding) || binding.Generation != generation ||
                !Alive(binding.Root) || !binding.Root.activeInHierarchy || !TryMap(out var map, out _, out _)) return;
            _nextClick = Time.unscaledTime + .35f;
            if (!DepartureApi.Catalog.Invoke(entry, Context(map, run), Scope) && entry.Error is { } error) Warn(error, null);
        }
        catch (Exception error) { Warn("EZAPI_DEPARTURE_CLICK", error); }
        finally { _clicking = false; InvalidateLayout(); }
    }

    private static void CopyGeometry(RectTransform target, RectTransform source)
    {
        target.anchorMin = source.anchorMin; target.anchorMax = source.anchorMax;
        target.pivot = source.pivot; target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale; target.localRotation = source.localRotation;
    }

    private static DepartureRect Bounds(RectTransform rect, RectTransform space)
    {
        _corners ??= new(4);
        rect.GetWorldCorners(_corners);
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            var point = space.InverseTransformPoint(_corners[i]);
            x0 = Mathf.Min(x0, point.x); y0 = Mathf.Min(y0, point.y);
            x1 = Mathf.Max(x1, point.x); y1 = Mathf.Max(y1, point.y);
        }
        return new(x0, y0, x1 - x0, y1 - y0);
    }

    private static DepartureRect Footprint(RectTransform source, RectTransform parent)
    {
        var result = Bounds(source, parent);
        foreach (var graphic in source.GetComponentsInChildren<Graphic>(true))
        {
            if (!Alive(graphic) || !graphic.enabled || !graphic.gameObject.activeSelf) continue;
            var child = Bounds(graphic.rectTransform, parent);
            if (!child.Valid) continue;
            float x = Math.Min(result.X, child.X), y = Math.Min(result.Y, child.Y);
            result = new(x, y, Math.Max(result.Right, child.Right) - x, Math.Max(result.Top, child.Top) - y);
        }
        return result;
    }

    private static DepartureRect ToRect(Rect rect) => new(rect.x, rect.y, rect.width, rect.height);
    private static DepartureRect Intersect(DepartureRect a, DepartureRect b)
    {
        float x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Min(a.Right, b.Right) - x, Math.Min(a.Top, b.Top) - y);
    }

    private static void Warn(string code, Exception? error)
    {
        string message = code + (error is null ? "" : ": " + error.GetType().Name + ": " + error.Message);
        if (_lastError == message) return;
        _lastError = message;
        MelonLogger.Warning("[EZapi] " + message);
    }
}

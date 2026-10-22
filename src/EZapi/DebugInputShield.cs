using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace EZapi;

/// <summary>透明 UI 射线遮挡仅覆盖调试控件，防止点击穿透到店铺门和原版按钮。</summary>
internal sealed class DebugInputShield
{
    private GameObject? _root;
    private RectTransform? _toggle, _panel;

    internal void Update(Rect toggle, Rect panel, bool open)
    {
        if (_root == null)
        {
            _root = new GameObject("EZapi.DebugInputShield");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            _root.AddComponent<GraphicRaycaster>();
            _toggle = Create("Toggle"); _panel = Create("Panel");
        }
        Place(_toggle!, toggle);
        Place(_panel!, panel);
        _panel!.gameObject.SetActive(open);
    }

    private RectTransform Create(string name)
    {
        var go = new GameObject("EZapi.Debug." + name,
            new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Type>(new[] { Il2CppType.Of<RectTransform>() }));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(_root!.transform, false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0, 0, 0, 0.001f);
        image.raycastTarget = true;
        return rect;
    }

    private static void Place(RectTransform rect, Rect bounds)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(bounds.x, -bounds.y);
        rect.sizeDelta = new Vector2(bounds.width, bounds.height);
    }

    internal void Stop()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null; _toggle = null; _panel = null;
    }
}

// 游戏自己的库存输入不经过 Unity UI，故只对面板内的新鼠标输入另加窄范围保护。
internal static class DebugInventoryPressGuard
{
    internal static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        var signature = new[] { typeof(Il2CppSystem.Collections.Generic.ISet<KeyCode>) };
        foreach (var type in new[] { typeof(ItemMouseHandler), typeof(ItemMouseDragHandler), typeof(ItemMouseDoubleClickHandler) })
            yield return AccessTools.DeclaredMethod(type, "OnEventPress", signature)
                ?? throw new MissingMethodException(type.FullName, "OnEventPress");
    }
    internal static bool Prefix() => !EZapiDebugOverlay.PointerOver();
}

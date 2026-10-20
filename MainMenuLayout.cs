using System;
using System.Collections.Generic;
using StarVaders;
using UnityEngine;
using UnityEngine.UI;

namespace StarVadersUltrawide;

// The native language selector assumes a bottom-left anchored 1920x1080 frame.
public class MainMenuLayout : MonoBehaviour
{
    public MainMenuLayout(IntPtr ptr) : base(ptr) { }
    readonly Dictionary<int, SavedRect> _original = new();
    MainMenuSceneController _menu;
    float _nextSearch;
    bool _pressed, _opened, _verified;
    readonly bool _layoutTest = Environment.GetEnvironmentVariable("STARVADERS_UW_LAYOUT_TEST") == "1";

    void LateUpdate()
    {
        if (_menu == null && Time.unscaledTime >= _nextSearch)
        {
            _nextSearch = Time.unscaledTime + .25f;
            _menu = UnityEngine.Object.FindObjectOfType<MainMenuSceneController>(true);
        }
        bool test = _layoutTest;
        if (test && _menu == null && Time.unscaledTime > 8)
        {
            var boot = UnityEngine.Object.FindObjectOfType<BootupController>();
            if (boot != null && boot.IsFinishedLoadingGame && !_pressed)
            { _pressed = true; boot.OnPressAnything(); }
        }
        if (!Plugin.Enabled.Value || Screen.height <= 0 || (float)Screen.width / Screen.height <= 16f/9f + .01f)
        { Restore(); return; }
        if (_menu == null || _menu.LanguageSelector == null) return;
        var root = _menu.LanguageSelector.transform.TryCast<RectTransform>();
        if (root == null || root.parent == null) return;
        Stretch(root, Vector2.zero);
        var scrim = root.Find("Image")?.TryCast<RectTransform>();
        if (scrim != null)
        {
            var saved = Remember(scrim);
            // Retain the original overscan around the screen.
            Stretch(scrim, saved.Size - new Vector2(1920, 1080), saved.Position);
        }
        if (test && !_opened)
        { _opened = true; _menu.LanguageSelector.gameObject.SetActive(true); }
        else if (test && !_verified && root.gameObject.activeInHierarchy)
        {
            _verified = true;
            foreach (string name in new[] { "ExtraButtonsLeft", "ExtraButtonsRight" })
            {
                var group = root.parent.Find(name);
                if (group == null) continue;
                var layout = group.GetComponent<HorizontalLayoutGroup>();
                Plugin.Instance.Log.LogInfo($"TOP GROUP {name}: spacing={layout?.spacing}; padding={layout?.padding.left},{layout?.padding.right}; alignment={layout?.childAlignment}; expand={layout?.childForceExpandWidth}; control={layout?.childControlWidth}");
                for (int i=0;i<group.childCount;i++) { var r=group.GetChild(i).TryCast<RectTransform>(); Plugin.Instance.Log.LogInfo($"TOP ICON {r.name}: pos={r.anchoredPosition}; size={r.rect.size}"); }
            }
            var canvas = root.GetComponentInParent<Canvas>().rootCanvas;
            var page = root.Find("Page").TryCast<RectTransform>();
            var centre = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, page.TransformPoint(page.rect.center));
            bool centred = Math.Abs(centre.x - Screen.width * .5f) < 2 && Math.Abs(centre.y - Screen.height * .5f) < 2;
            bool covered = scrim != null && scrim.rect.width >= root.rect.width && scrim.rect.height >= root.rect.height;
            Plugin.Instance.Log.LogInfo($"LANGUAGE LAYOUT {(centred && covered ? "PASS" : "FAIL")}: page centre={centre}; screen={Screen.width}x{Screen.height}; frame={root.rect.size}; scrim={scrim?.rect.size}");
        }
    }
    SavedRect Remember(RectTransform rect)
    {
        int id = rect.GetInstanceID();
        if (!_original.TryGetValue(id, out var saved) || saved.Rect == null)
            _original[id] = saved = new SavedRect(rect);
        return saved;
    }
    void Stretch(RectTransform rect, Vector2 size, Vector2 position = default)
    {
        Remember(rect);
        if (rect.anchorMin != Vector2.zero) rect.anchorMin = Vector2.zero;
        if (rect.anchorMax != Vector2.one) rect.anchorMax = Vector2.one;
        if (rect.pivot != Vector2.one * .5f) rect.pivot = Vector2.one * .5f;
        if (rect.sizeDelta != size) rect.sizeDelta = size;
        if (rect.anchoredPosition != position) rect.anchoredPosition = position;
    }
    void Restore()
    {
        foreach (var saved in _original.Values) saved.Restore();
        _original.Clear();
    }
    void OnDestroy() => Restore();
    sealed class SavedRect
    {
        internal readonly RectTransform Rect;
        internal readonly Vector2 Min, Max, Pivot, Size, Position;
        internal SavedRect(RectTransform r)
        { Rect=r; Min=r.anchorMin; Max=r.anchorMax; Pivot=r.pivot; Size=r.sizeDelta; Position=r.anchoredPosition; }
        internal void Restore()
        {
            if (Rect == null) return;
            Rect.anchorMin=Min; Rect.anchorMax=Max; Rect.pivot=Pivot; Rect.sizeDelta=Size; Rect.anchoredPosition=Position;
        }
    }
}

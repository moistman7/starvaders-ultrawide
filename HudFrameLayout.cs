using System.Collections.Generic;
using UnityEngine;

namespace StarVadersUltrawide;

public partial class HudSizing
{
    const string FramePrefix = "UltrawideFrame_";
    // Wrapper per side panel, keyed by the panel's instance ID.
    readonly Dictionary<int, RectTransform> _frameWrappers = new();
    RectTransform _leftGroup, _rightGroup;

    // Each side panel is moved into an invisible parent that exactly matches
    // its original parent. Centering offsets only that parent, so the game's
    // own panel animations and sway keep working unchanged underneath, and
    // full width is an exact zero offset.
    RectTransform Wrap(RectTransform panel)
    {
        var parent = panel?.parent?.TryCast<RectTransform>();
        if (parent == null) return null;
        if (parent.name == FramePrefix + panel.name) return parent;
        var obj = new GameObject(FramePrefix + panel.name);
        obj.layer = panel.gameObject.layer;
        var wrapper = obj.AddComponent<RectTransform>();
        wrapper.SetParent(parent, false);
        wrapper.SetSiblingIndex(panel.GetSiblingIndex());
        wrapper.anchorMin = Vector2.zero;
        wrapper.anchorMax = Vector2.one;
        wrapper.pivot = parent.pivot;
        wrapper.offsetMin = wrapper.offsetMax = Vector2.zero;
        wrapper.localScale = Vector3.one;
        wrapper.localRotation = Quaternion.identity;
        panel.SetParent(wrapper, false);
        return wrapper;
    }

    void Frame(RectTransform panel)
    {
        var wrapper = Wrap(panel);
        var parent = wrapper?.parent?.TryCast<RectTransform>();
        if (parent == null) return;
        _frameWrappers[panel.GetInstanceID()] = wrapper;
        float width = Mathf.Min(parent.rect.width, parent.rect.height * (16f / 9f));
        float shift = (.5f - (panel.anchorMin.x + panel.anchorMax.x) * .5f) * (parent.rect.width - width);
        var desired = new Vector2(shift, 0f);
        if (wrapper.anchoredPosition != desired) wrapper.anchoredPosition = desired;
    }

    void RestoreHudFrame()
    {
        foreach (var wrapper in _frameWrappers.Values)
            if (wrapper != null && wrapper.anchoredPosition != Vector2.zero) wrapper.anchoredPosition = Vector2.zero;
        _frameWrappers.Clear();
    }

    static RectTransform FindPanel(Transform root, string name) =>
        (root?.Find(name) ?? root?.Find(FramePrefix + name + "/" + name))?.TryCast<RectTransform>();

    void UpdateHudFrame()
    {
        // Panels are looked up once per encounter view, not every frame.
        if (_leftGroup == null || _rightGroup == null)
        {
            _frameWrappers.Clear();
            var panels = _view?._encounterPortraitView?.GetComponentInParent<Canvas>()?.rootCanvas.transform;
            _leftGroup = FindPanel(panels, "LeftPanel");
            _rightGroup = FindPanel(panels, "RightPanel");
        }
        // Wrappers stay in place at zero offset; full width needs no tracking.
        if (!_centerHud.Value) { RestoreHudFrame(); return; }
        if (_leftGroup != null) Frame(_leftGroup);
        if (_rightGroup != null) Frame(_rightGroup);
    }

    // Scale a side-panel element about its panel's top outer corner instead of
    // its own pivot, so the whole panel grows like one picture: spacing between
    // its parts scales too and neighbours cannot grow into each other.
    bool ScaleWithinGroup(Transform target, Vector3 nativePosition, float scale, out Vector3 position)
    {
        position = nativePosition;
        var group = _leftGroup != null && target.IsChildOf(_leftGroup) ? _leftGroup :
            _rightGroup != null && target.IsChildOf(_rightGroup) ? _rightGroup : null;
        if (group == null || target == group || target.parent == null) return false;
        var bounds = group.rect;
        var corner = group.TransformPoint(new Vector3(group == _leftGroup ? bounds.xMin : bounds.xMax, bounds.yMax, 0f));
        var anchor = target.parent.InverseTransformPoint(corner);
        float spread = _groupSpread.TryGetValue(group.GetInstanceID(), out float fitted) ? Mathf.Clamp(fitted, 1f, scale) : scale;
        position = new Vector3(anchor.x + (nativePosition.x - anchor.x) * scale,
            anchor.y + (nativePosition.y - anchor.y) * spread, nativePosition.z);
        return true;
    }

    // Vertical spacing factor per side panel. Equal to the HUD scale unless the
    // enlarged panel would reach the reroll/End Turn buttons below it.
    readonly Dictionary<int, float> _groupSpread = new();
    float _nextGroupFit;
    const float RackGap = 12f;

    // Close up a panel's vertical spacing just enough to keep its lowest part
    // above the bottom buttons. Element sizes are unchanged. Measured from
    // rendered bounds a few times per second, so it settles over a few frames.
    void FitGroupsAboveRack()
    {
        if (Time.unscaledTime < _nextGroupFit) return;
        _nextGroupFit = Time.unscaledTime + .5f;
        // At 100% HUD nothing grows, so there is nothing to fit.
        if (_hudScale.Value <= 1f) { _groupSpread.Clear(); return; }
        foreach (var group in new[] { _leftGroup, _rightGroup })
        {
            if (group == null) continue;
            int id = group.GetInstanceID();
            float scale = _hudScale.Value;
            float spread = _groupSpread.TryGetValue(id, out float fitted) ? Mathf.Clamp(fitted, 1f, scale) : scale;
            if (!VisibleExtent(group, out var panel) || !ScreenExtent(group, out var frame)) continue;
            float limit = float.MinValue;
            foreach (var button in new[] { _view?._chronoButtonView?.transform, _view?._endTurnView?.transform })
                if (VisibleExtent(button, out var rack) && rack.xMin < panel.xMax && rack.xMax > panel.xMin)
                    limit = Mathf.Max(limit, rack.yMax + RackGap);
            if (limit == float.MinValue) { _groupSpread.Remove(id); continue; }
            // Screen Y grows upward; the panel hangs from its top edge.
            float height = frame.yMax - panel.yMin, available = frame.yMax - limit;
            if (height <= 1f || available <= 1f) continue;
            float next = Mathf.Clamp(spread * available / height, 1f, scale);
            // Only spread back out when there is clear room, to avoid jitter.
            if (next > spread && panel.yMin - limit < 4f) continue;
            if (Mathf.Abs(next - spread) > .002f) _groupSpread[id] = next;
        }
    }

    // Screen bounds of the visible graphics under a root. Zero-width roots and
    // full-screen backdrops would otherwise make the bounds meaningless.
    bool VisibleExtent(Transform root, out Rect extent)
    {
        extent = default;
        if (root == null || !root.gameObject.activeInHierarchy) return false;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
        {
            if (!graphic.enabled || graphic.color.a < .05f || graphic.canvasRenderer.GetAlpha() < .05f) continue;
            if (!ScreenExtent(graphic.rectTransform, out var rect) || rect.width > Screen.width * .5f) continue;
            xMin = Mathf.Min(xMin, rect.xMin); xMax = Mathf.Max(xMax, rect.xMax);
            yMin = Mathf.Min(yMin, rect.yMin); yMax = Mathf.Max(yMax, rect.yMax);
        }
        if (xMin > xMax) return false;
        extent = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    bool ScreenExtent(RectTransform rect, out Rect extent)
    {
        extent = default;
        var canvas = rect.GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null) return false;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        rect.GetWorldCorners(_corners);
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            var point = RectTransformUtility.WorldToScreenPoint(camera, _corners[i]);
            xMin = Mathf.Min(xMin, point.x); xMax = Mathf.Max(xMax, point.x);
            yMin = Mathf.Min(yMin, point.y); yMax = Mathf.Max(yMax, point.y);
        }
        extent = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return true;
    }

    // Hand extent in screen X, measured once per frame for both bottom buttons.
    int _handFrame = -1;
    float _handLeft, _handRight;

    void MeasureHand(Canvas root, Camera camera)
    {
        if (_handFrame == Time.frameCount) return;
        _handFrame = Time.frameCount;
        _handLeft = float.MaxValue; _handRight = float.MinValue;
        var hand = root.transform.Find("HandView")?.TryCast<RectTransform>();
        if (hand == null) return;
        float parked = hand.rect.width * .6f;
        for (int i = 0; i < hand.childCount; i++)
        {
            var card = hand.GetChild(i).TryCast<RectTransform>();
            // Pile cards are parked far outside the hand and must not reserve space.
            if (card == null || !card.gameObject.activeInHierarchy || !card.name.StartsWith("CardView") || Mathf.Abs(card.localPosition.x) > parked) continue;
            card.GetWorldCorners(_corners);
            for (int j=0;j<4;j++)
            {
                float x = RectTransformUtility.WorldToScreenPoint(camera, _corners[j]).x;
                _handLeft = Mathf.Min(_handLeft, x); _handRight = Mathf.Max(_handRight, x);
            }
        }
    }

    void KeepClearOfHand(Transform button, Vector3 nativePosition)
    {
        var root = _view?.GridView?.GetComponentInParent<Canvas>()?.rootCanvas;
        var rect = button.TryCast<RectTransform>();
        var parent = button.parent?.TryCast<RectTransform>();
        if (root == null || rect == null || parent == null) return;
        var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        MeasureHand(root, camera);
        float left = _handLeft, right = _handRight;
        if (left == float.MaxValue) return;
        rect.GetWorldCorners(_corners);
        float min = float.MaxValue, max = float.MinValue;
        for (int j=0;j<4;j++)
        {
            float x = RectTransformUtility.WorldToScreenPoint(camera, _corners[j]).x;
            min = Mathf.Min(min, x); max = Mathf.Max(max, x);
        }
        bool onLeft = button == _view._chronoButtonView?.transform;
        float shift = onLeft ? Mathf.Min(0, left - 24f - max) : Mathf.Max(0, right + 24f - min);
        if (Mathf.Abs(shift) < .1f) return;
        var screen = RectTransformUtility.WorldToScreenPoint(camera, button.position);
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screen + new Vector2(shift,0), camera, out Vector3 world)) button.position = world;
    }
}

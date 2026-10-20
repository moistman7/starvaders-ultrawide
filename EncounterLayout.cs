using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using StarVaders;
using UnityEngine;
using UnityEngine.UI;

namespace StarVadersUltrawide;

// The rack art (T_UIRack_2, 2048x240) is three blocks separated by transparent
// gaps: a left end under the deck, a center block with bases under the reroll
// and End Turn buttons, and a right end under the discard pile. At 16:9 the
// ends sit on the screen edges. Stretching the whole image to ultrawide slides
// the bases off the buttons, so draw the center block at native size and pin
// each end block to its screen edge, exactly as the 16:9 layout looks.
public class EncounterLayout : MonoBehaviour
{
    public EncounterLayout(IntPtr pointer) : base(pointer) { }
    const float NativeWidth = 1920f;
    // Cut points in sprite pixels, in the middle of the two transparent gaps.
    const float LeftCut = 183f, RightCut = 1865f;
    float _next;
    RectTransform _rack;
    Image _image;
    Sprite _nativeSprite;
    (Vector2 AnchorMin, Vector2 AnchorMax, Vector2 Pivot, Vector2 Size, Vector2 Position) _original;
    Sprite _left, _center, _right;
    Image _leftEnd, _rightEnd;

    void LateUpdate() { long started = Profile.Start(); try { LateUpdateCore(); } finally { Profile.Stop(started, Profile.Hook.Rack); } }
    void LateUpdateCore()
    {
        bool enabled = Plugin.Enabled.Value && Screen.height > 0 &&
            (float)Screen.width / Screen.height > 16f / 9f + .01f;
        if (!enabled) { Restore(); return; }
        if (Time.unscaledTime < _next) return;
        // Search slowly until an encounter exists; then only check the cached rack.
        _next = Time.unscaledTime + (_rack == null ? 1f : .25f);
        if (_rack == null)
        {
            // The previous encounter (and our end pieces with it) is gone.
            foreach (var sprite in new[] { _left, _center, _right })
                if (sprite != null) UnityEngine.Object.Destroy(sprite);
            _left = _center = _right = null;
            _image = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<EncounterView>()))
            {
                var view = candidate.TryCast<EncounterView>();
                var canvas = view?.GridView?.GetComponentInParent<Canvas>();
                _rack = canvas?.transform.Find("Rack/RackImage")?.TryCast<RectTransform>();
                if (_rack != null) break;
            }
            if (_rack == null) return;
        }
        if (_image == null) Capture();
        if (_image == null || _center == null) return;
        Apply();
    }

    void Capture()
    {
        _image = _rack.GetComponent<Image>();
        var sprite = _image?.sprite;
        if (sprite == null || sprite.texture == null) { _image = null; return; }
        _nativeSprite = sprite;
        _original = (_rack.anchorMin, _rack.anchorMax, _rack.pivot, _rack.sizeDelta, _rack.anchoredPosition);
        var rect = sprite.rect;
        _left = Slice(sprite, rect.x, rect.x + LeftCut);
        _center = Slice(sprite, rect.x + LeftCut, rect.x + RightCut);
        _right = Slice(sprite, rect.x + RightCut, rect.xMax);
        _leftEnd = EndPiece("UltrawideRackLeft", _left);
        _rightEnd = EndPiece("UltrawideRackRight", _right);
    }

    static Sprite Slice(Sprite sprite, float xMin, float xMax)
    {
        var rect = new Rect(xMin, sprite.rect.y, xMax - xMin, sprite.rect.height);
        var slice = Sprite.Create(sprite.texture, rect, new Vector2(.5f, .5f), sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        slice.name = sprite.name + "_ultrawide";
        return slice;
    }

    Image EndPiece(string name, Sprite sprite)
    {
        var obj = new GameObject(name);
        obj.layer = _rack.gameObject.layer;
        var rect = obj.AddComponent<RectTransform>();
        rect.SetParent(_rack.parent, false);
        rect.SetSiblingIndex(_rack.GetSiblingIndex() + 1);
        var image = obj.AddComponent<Image>();
        image.sprite = sprite;
        image.color = _image.color;
        image.material = _image.material;
        image.raycastTarget = _image.raycastTarget;
        return image;
    }

    // Native image size is NativeWidth x height for the full sprite width, so
    // one sprite pixel is NativeWidth / sprite width canvas units across.
    void Apply()
    {
        float unitsPerPixel = NativeWidth / _nativeSprite.rect.width;
        float height = _original.Size.y, y = _original.Position.y, anchorY = _original.AnchorMin.y;
        Place(_rack, new Vector2(.5f, anchorY), new Vector2(.5f, 0f),
            new Vector2((RightCut - LeftCut) * unitsPerPixel, height),
            new Vector2(((LeftCut + RightCut) * .5f - _nativeSprite.rect.width * .5f) * unitsPerPixel, y));
        if (_image.sprite != _center) _image.sprite = _center;
        Place(_leftEnd?.rectTransform, new Vector2(0f, anchorY), new Vector2(0f, 0f), new Vector2(LeftCut * unitsPerPixel, height), new Vector2(0f, y));
        Place(_rightEnd?.rectTransform, new Vector2(1f, anchorY), new Vector2(1f, 0f),
            new Vector2((_nativeSprite.rect.width - RightCut) * unitsPerPixel, height), new Vector2(0f, y));
        foreach (var end in new[] { _leftEnd, _rightEnd })
            if (end != null && end.gameObject.activeSelf != _rack.gameObject.activeInHierarchy) end.gameObject.SetActive(_rack.gameObject.activeInHierarchy);
    }

    static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
    {
        if (rect == null) return;
        if (rect.anchorMin != anchor) rect.anchorMin = anchor;
        if (rect.anchorMax != anchor) rect.anchorMax = anchor;
        if (rect.pivot != pivot) rect.pivot = pivot;
        if (rect.sizeDelta != size) rect.sizeDelta = size;
        if (rect.anchoredPosition != position) rect.anchoredPosition = position;
    }

    void Restore()
    {
        if (_image != null && _rack != null)
        {
            _image.sprite = _nativeSprite;
            _rack.anchorMin = _original.AnchorMin;
            _rack.anchorMax = _original.AnchorMax;
            _rack.pivot = _original.Pivot;
            _rack.sizeDelta = _original.Size;
            _rack.anchoredPosition = _original.Position;
        }
        foreach (var end in new[] { _leftEnd, _rightEnd })
            if (end != null) UnityEngine.Object.Destroy(end.gameObject);
        foreach (var sprite in new[] { _left, _center, _right })
            if (sprite != null) UnityEngine.Object.Destroy(sprite);
        _leftEnd = _rightEnd = null;
        _left = _center = _right = null;
        _image = null;
        _rack = null;
    }

    void OnDestroy() => Restore();
}

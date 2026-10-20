using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using StarVaders;
using UnityEngine;

namespace StarVadersUltrawide;

// Trim the rack's overscan without moving the edge-anchored deck counters.
public class EncounterLayout : MonoBehaviour
{
    public EncounterLayout(IntPtr pointer) : base(pointer) { }
    readonly Dictionary<int, (RectTransform Rect, Vector2 Size)> _original = new();
    float _next;
    RectTransform _rack;

    void LateUpdate()
    {
        bool enabled = Plugin.Enabled.Value && Screen.height > 0 &&
            (float)Screen.width / Screen.height > 16f / 9f + .01f;
        if (!enabled) { Restore(); return; }
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + .25f;
        if (_rack == null)
        {
          foreach (var candidate in UnityEngine.Object.FindObjectsOfType(Il2CppType.Of<EncounterView>()))
          {
            var view = candidate.TryCast<EncounterView>();
            var canvas = view?.GridView?.GetComponentInParent<Canvas>();
            _rack = canvas?.transform.Find("Rack/RackImage")?.TryCast<RectTransform>();
            if (_rack != null) break;
          }
        }
        var rack = _rack;
        if (rack != null)
        {
            int id = rack.GetInstanceID();
            if (!_original.TryGetValue(id, out var saved) || saved.Rect != rack)
                _original[id] = saved = (rack, rack.sizeDelta);
            // Native rack is 300 high with its bottom 150 below the viewport.
            // Leave a 70-unit backing strip instead of a 150-unit black panel.
            var size = new Vector2(saved.Size.x, Mathf.Min(saved.Size.y, 220f));
            if (rack.sizeDelta != size) rack.sizeDelta = size;
        }
    }

    void Restore()
    {
        foreach (var saved in _original.Values)
            if (saved.Rect != null) saved.Rect.sizeDelta = saved.Size;
        _original.Clear();
    }

    void OnDestroy() => Restore();
}

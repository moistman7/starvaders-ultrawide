using System;
using System.Collections.Generic;
using HarmonyLib;
using StarVaders;
using UnityEngine;

namespace StarVadersUltrawide;

// Panels, transition ghosts and pilot details share a 1920-wide coordinate frame.
public class SelectionLayout : MonoBehaviour
{
    public SelectionLayout(IntPtr pointer) : base(pointer) { }
    static readonly Dictionary<int, RectTransform> Frames = new();
    float _nextSearch;
    MechSelectionController _selection;

    void Update()
    {
        if (_selection == null && Time.unscaledTime >= _nextSearch)
        {
            _nextSearch = Time.unscaledTime + .25f;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType(Il2CppInterop.Runtime.Il2CppType.Of<MechSelectionController>()))
            {
                _selection = candidate.TryCast<MechSelectionController>();
                if (_selection != null) break;
            }
        }
        Apply(_selection);
    }

    internal static void Apply(MechSelectionController selection)
    {
        if (selection == null || selection.Panel1 == null) return;
        var canvas = selection.Panel1.GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null) return;
        var root = canvas.transform;
        int id = root.GetInstanceID();
        bool enabled = Plugin.Enabled.Value && Screen.height > 0 &&
            (float)Screen.width / Screen.height > 16f / 9f + .01f;
        Frames.TryGetValue(id, out var frame);
        if (!enabled)
        {
            if (frame != null)
            {
                while (frame.childCount > 0) frame.GetChild(0).SetParent(root, false);
                UnityEngine.Object.Destroy(frame.gameObject);
            }
            Frames.Remove(id);
            return;
        }
        if (frame == null)
        {
            var holder = new GameObject("UltrawideSelectionFrame");
            frame = holder.AddComponent<RectTransform>();
            frame.SetParent(root, false);
            frame.anchorMin = new Vector2(.5f, 0f);
            frame.anchorMax = new Vector2(.5f, 1f);
            frame.pivot = new Vector2(.5f, .5f);
            frame.sizeDelta = new Vector2(1920f, 0f);
            frame.anchoredPosition = Vector2.zero;
            Frames[id] = frame;
        }
        // Include siblings such as transition ghosts and the input/UI canvas.
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            var child = root.GetChild(i);
            if (child != frame) { child.SetParent(frame, false); child.SetAsFirstSibling(); }
        }
    }
}

[HarmonyPatch(typeof(MechSelectionController), nameof(MechSelectionController.Start))]
internal static class SelectionLayoutStart
{
    static void Prefix(MechSelectionController __instance) => SelectionLayout.Apply(__instance);
}

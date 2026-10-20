using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using StarVaders;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarVadersUltrawide
{
    // Keep rendered transforms in place during input processing.
    public class HudSizing : MonoBehaviour
    {
        public HudSizing(IntPtr pointer) : base(pointer) { }
        internal static ConfigEntry<float> _hudScale;
        internal static ConfigEntry<float> _boardScale;
        internal const float DefaultScale = 1.23f;
        float _nextDiscovery;
        readonly List<Transform> _targets = new();
        Transform _board;
        EncounterView _view;
        readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> _corners = new(4);
        List<(Transform Target, Vector3 Scale, Vector3 Position, Vector3 AppliedScale, Vector3 AppliedPosition)> _applied = new();
        List<(Transform Target, Vector3 Scale, Vector3 Position, Vector3 AppliedScale, Vector3 AppliedPosition)> _pending = new();

        void Start()
        {
            _hudScale = Plugin.Instance.Config.Bind("Interface", "HudScale", DefaultScale,
                new ConfigDescription("Encounter panel size multiplier. F6 decreases and F7 increases both HUD and board size.", new AcceptableValueRange<float>(1f, 1.35f)));
            _boardScale = Plugin.Instance.Config.Bind("Interface", "BoardScale", DefaultScale,
                new ConfigDescription("Encounter board size multiplier.", new AcceptableValueRange<float>(1f, 1.35f)));
        }

        void Restore()
        {
            foreach (var entry in _applied)
            {
                if (entry.Target == null) continue;
                // Respect animation writes made by the game since the last render.
                if (entry.Target.localScale == entry.AppliedScale) entry.Target.localScale = entry.Scale;
                if (entry.Target.localPosition == entry.AppliedPosition) entry.Target.localPosition = entry.Position;
            }
            _applied.Clear();
        }

        void OnDestroy() => Restore();

        void Update()
        {
            if (_hudScale == null) return;
            float step = Hotkeys.Pressed(Hotkeys.Increase) ? 0.02f :
                Hotkeys.Pressed(Hotkeys.Decrease) ? -0.02f : 0f;
            if (step != 0f)
            {
                _hudScale.Value = Mathf.Clamp(_hudScale.Value + step, 1f, 1.35f);
                _boardScale.Value = Mathf.Clamp(_boardScale.Value + step, 1f, 1.35f);
                Plugin.Instance.Log.LogInfo($"HUD size: {_hudScale.Value:P0}; board: {_boardScale.Value:P0}");
            }
            if (Time.unscaledTime < _nextDiscovery) return;
            _nextDiscovery = Time.unscaledTime + 0.25f;
            _targets.Clear();
            _board = null;
            // A live encounter owns these targets for its whole lifetime.
            // Only search the scene again after that view has been destroyed.
            if (_view == null)
                foreach (var candidate in UnityEngine.Object.FindObjectsOfType<EncounterView>())
                { _view = candidate; break; }
            if (_view != null)
            {
                var view = _view;
                Add(view._artifactViewParent?.transform);
                Add(view._heatView?.transform);
                Add(view._powerView?.transform);
                Add(view._manaView?.transform);
                Add(view._doomView?.transform);
                Add(view._encounterPortraitView?.transform);
                Add(view._endTurnView?.transform);
                Add(view._chronoButtonView?.transform);
                Add(view.GridView?.transform);
                _board = view.GridView?.transform;
            }
        }

        void Add(Transform target)
        {
            if (target == null || _targets.Contains(target)) return;
            // Never scale both an ancestor and its descendant.
            foreach (var other in _targets)
                if (target.IsChildOf(other)) return;
            for (int i = _targets.Count - 1; i >= 0; i--)
                if (_targets[i].IsChildOf(target)) _targets.RemoveAt(i);
            _targets.Add(target);
        }

        void LateUpdate()
        {
            if (_hudScale == null || !Plugin.Enabled.Value || Screen.height <= 0 ||
                (float)Screen.width / Screen.height <= 16f / 9f + 0.01f)
            { Restore(); return; }
            _pending.Clear();
            foreach (var target in _targets)
            {
                if (target == null || !target.gameObject.activeInHierarchy) continue;
                float scale = target == _board ? _boardScale.Value : _hudScale.Value;
                if (scale <= 1f) continue;
                var originalScale = target.localScale;
                var originalPosition = target.localPosition;
                // Reuse the native baseline while our last result is still present.
                // Animation writes become the new baseline, without a restore/write
                // pair that dirties the canvas on every otherwise idle frame.
                foreach (var previous in _applied)
                {
                    if (previous.Target != target) continue;
                    if (originalScale == previous.AppliedScale) originalScale = previous.Scale;
                    if (originalPosition == previous.AppliedPosition) originalPosition = previous.Position;
                    break;
                }
                var desiredScale = originalScale * scale;
                if (target.localScale != desiredScale)
                {
                    target.localScale = desiredScale;
                    if (target.localPosition != originalPosition) target.localPosition = originalPosition;
                }
                KeepOnScreen(target);
                _pending.Add((target, originalScale, originalPosition, target.localScale, target.localPosition));
            }
            // Restore targets dropped by a scene transition or a size change to 100%.
            foreach (var previous in _applied)
            {
                if (previous.Target == null) continue;
                bool retained = false;
                foreach (var current in _pending)
                    if (current.Target == previous.Target) { retained = true; break; }
                if (retained) continue;
                if (previous.Target.localScale == previous.AppliedScale) previous.Target.localScale = previous.Scale;
                if (previous.Target.localPosition == previous.AppliedPosition) previous.Target.localPosition = previous.Position;
            }
            var reusable = _applied;
            _applied = _pending;
            _pending = reusable;
        }

        void KeepOnScreen(Transform target)
        {
            var rect = target.TryCast<RectTransform>();
            if (rect == null) return;
            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            rect.GetWorldCorners(_corners);
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                var point = RectTransformUtility.WorldToScreenPoint(camera, _corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            // Full-screen containers are not useful bounds for their artwork.
            if (max.x - min.x > Screen.width || max.y - min.y > Screen.height) return;
            const float margin = 8f;
            float x = min.x < margin ? margin - min.x : max.x > Screen.width - margin ? Screen.width - margin - max.x : 0f;
            float y = min.y < margin ? margin - min.y : max.y > Screen.height - margin ? Screen.height - margin - max.y : 0f;
            if (x == 0f && y == 0f) return;
            var parent = target.parent?.TryCast<RectTransform>();
            if (parent == null) return;
            Vector2 current = RectTransformUtility.WorldToScreenPoint(camera, target.position);
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, current + new Vector2(x, y), camera, out Vector3 world))
                target.position = world;
        }
    }
}

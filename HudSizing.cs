using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using StarVaders;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarVadersUltrawide
{
    // Keep rendered transforms in place during input processing.
    public partial class HudSizing : MonoBehaviour
    {
        public HudSizing(IntPtr pointer) : base(pointer) { }
        internal static ConfigEntry<float> _hudScale;
        internal static ConfigEntry<float> _boardScale;
        internal static ConfigEntry<bool> _centerHud;
        internal const float DefaultHudScale = 1f;
        internal const float DefaultBoardScale = 1f;
        internal static HudSizing Active;
        float _nextDiscovery;
        readonly List<Transform> _targets = new();
        Transform _board;
        EncounterView _view;
        readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> _corners = new(4);
        List<(Transform Target, Vector3 Scale, Vector3 Position, Vector3 AppliedScale, Vector3 AppliedPosition)> _applied = new();
        List<(Transform Target, Vector3 Scale, Vector3 Position, Vector3 AppliedScale, Vector3 AppliedPosition)> _pending = new();

        void Start()
        {
            Active = this;
            if (_checkHudFrame) Application.runInBackground = true;
            _centerHud = Plugin.Instance.Config.Bind("Interface", "CenterHud16By9", false,
                "Keep encounter HUD panels inside a centered 16:9 area on ultrawide. Board size is independent.");
            _hudScale = Plugin.Instance.Config.Bind("Interface", "HudScale", DefaultHudScale,
                new ConfigDescription("Encounter panel size multiplier. F6 decreases and F7 increases both HUD and board size.", new AcceptableValueRange<float>(1f, 1.35f)));
            _boardScale = Plugin.Instance.Config.Bind("Interface", "BoardScale", DefaultBoardScale,
                new ConfigDescription("Encounter board size multiplier.", new AcceptableValueRange<float>(1f, 1.35f)));
            // Profiling can force a size for this session only; nothing is saved.
            if (Profile.On && float.TryParse(Environment.GetEnvironmentVariable("STARVADERS_UW_PROFILE_SIZE"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float size))
            {
                Plugin.Instance.Config.SaveOnConfigSet = false;
                _hudScale.Value = _boardScale.Value = size;
                _centerHud.Value = true;
                Plugin.Instance.Log.LogInfo($"PROFILE: session-only size {size:P0}, centered; config saving disabled.");
            }
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
            RestoreHudFrame();
        }

        void OnDestroy()
        {
            Restore();
            if (Active == this) Active = null;
        }

        void Update() { long started = Profile.Start(); try { UpdateCore(); } finally { Profile.Stop(started, Profile.Hook.HudUpdate); } }
        void UpdateCore()
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
            _nextDiscovery = Time.unscaledTime + (_view == null ? 0.25f : 1f);
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
                Add(view._encounterPortraitView?.transform.parent);
                Add(view._endTurnView?.transform);
                Add(view._chronoButtonView?.transform);
                Add(view.GridView?.transform);
                _board = view.GridView?.transform;
                Add(view._artifactView14Parent?.transform);
                Add(view._artifactView27Parent?.transform);
                Add(view._bombLimitView?.transform);
                Add(view._puppetLimitView?.transform);
                Add(view._starbuxView?.transform);
                Add(view._screenView?.transform);
                Add(view._starBucksText?.transform);
                var root = view.GridView?.GetComponentInParent<Canvas>()?.rootCanvas.transform;
                Add(root?.Find("GuideButton"));
                var rack = root?.Find("Rack");
                if (rack != null) foreach (var rect in rack.GetComponentsInChildren<RectTransform>(true))
                    if (rect.name.EndsWith("PileView")) Add(rect);
                foreach (var pause in UnityEngine.Object.FindObjectsOfType<PauseButtonView>())
                    Add(pause.transform);
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

        void LateUpdate() { long started = Profile.Start(); try { LateUpdateCore(); } finally { Profile.Stop(started, Profile.Hook.HudLateUpdate); Profile.Frame(); } }
        void LateUpdateCore()
        {
            ApplySizing();
            CheckHudFrame();
        }

        // Refresh after native animation/layout writes and before hit testing.
        // The tracked baseline prevents nested input calls from scaling twice.
        internal void ApplySizing()
        {
            if (_hudScale == null || !Plugin.Enabled.Value || Screen.height <= 0 ||
                (float)Screen.width / Screen.height <= 16f / 9f + 0.01f)
            { Restore(); return; }
            UpdateHudFrame();
            _pending.Clear();
            foreach (var target in _targets)
            {
                if (target == null || !target.gameObject.activeInHierarchy) continue;
                bool centered = target != _board && _centerHud.Value;
                float scale = target == _board ? _boardScale.Value : _hudScale.Value;
                bool bottomButton = target == _view?._endTurnView?.transform || target == _view?._chronoButtonView?.transform;
                if (scale <= 1f && !centered && !bottomButton) continue;
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
                if (target != _board && ScaleWithinGroup(target, originalPosition, scale, out var grouped))
                {
                    if (target.localPosition != grouped) target.localPosition = grouped;
                }
                else if (target == _board || !centered) KeepOnScreen(target, originalPosition, false);
                if (bottomButton) { target.localPosition = originalPosition; KeepClearOfHand(target, originalPosition); }
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
            if (_view != null) FitGroupsAboveRack();
        }

        void KeepOnScreen(Transform target, Vector3 originalPosition, bool centered)
        {
            var rect = target.TryCast<RectTransform>();
            if (rect == null || target.name == "EncounterPortraitArea") return;
            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var parent = target.parent?.TryCast<RectTransform>();
            if (parent == null) return;
            Vector2 baseline = RectTransformUtility.WorldToScreenPoint(camera, parent.TransformPoint(originalPosition));
            Vector2 current = RectTransformUtility.WorldToScreenPoint(camera, target.position);
            Vector2 previousOffset = current - baseline;
            rect.GetWorldCorners(_corners);
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                var point = RectTransformUtility.WorldToScreenPoint(camera, _corners[i]) - previousOffset;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            // Hanging artwork may extend vertically beyond the viewport while
            // still providing useful horizontal bounds for the HUD frame.
            if (max.x - min.x > Screen.width) return;
            const float margin = 8f;
            float inset = centered ? Mathf.Max(0f, (Screen.width - Screen.height * (16f / 9f)) * .5f) : 0f;
            float left = inset + margin, right = Screen.width - inset - margin;
            float x = max.x - min.x > right - left ? (left + right - min.x - max.x) * .5f :
                min.x < left ? left - min.x : max.x > right ? right - max.x : 0f;
            float y = max.y - min.y > Screen.height ? 0f :
                min.y < margin ? margin - min.y : max.y > Screen.height - margin ? Screen.height - margin - max.y : 0f;
            if (x == 0f && y == 0f && previousOffset == Vector2.zero) return;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, baseline + new Vector2(x, y), camera, out Vector3 world) && target.position != world)
                target.position = world;
        }
    }
}

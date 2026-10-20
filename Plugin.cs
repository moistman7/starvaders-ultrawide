using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using HarmonyLib;

namespace StarVadersUltrawide
{
    [BepInPlugin("local.starvaders.ultrawide", "StarVaders Ultrawide", "0.1.0")]
    public class Plugin : BasePlugin
    {
        internal static Plugin Instance;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> MatchValue;
        internal static ConfigEntry<bool> ForceResolution;
        internal static ConfigEntry<int> ResWidth;
        internal static ConfigEntry<int> ResHeight;
        internal static ConfigEntry<string> ScreenMode;
        internal static ConfigEntry<float> ForceDelay;
        internal static ConfigEntry<float> ForceWindow;

        public override void Load()
        {
            Instance = this;
            Hotkeys.Bind(Config);
            Enabled = Config.Bind("General", "Enabled", true,
                "Apply the ultrawide fix. F8 toggles it in game.");
            MatchValue = Config.Bind("General", "CanvasMatch", 1f,
                new ConfigDescription("CanvasScaler match value used on ultrawide. 0 = match width (zoomed), 1 = match height (no zoom).", new AcceptableValueRange<float>(0f, 1f)));

            ForceResolution = Config.Bind("Resolution", "ForceResolution", true,
                "Set the resolution at startup and when re-enabling the fix. Toggle off then on to restore the configured resolution.");
            ResWidth = Config.Bind("Resolution", "Width", 0, "Target width in pixels. 0 = use the monitor's native width.");
            ResHeight = Config.Bind("Resolution", "Height", 0, "Target height in pixels. 0 = use the monitor's native height.");
            ScreenMode = Config.Bind("Resolution", "ScreenMode", "FullScreenWindow",
                "FullScreenWindow (borderless), ExclusiveFullScreen, MaximizedWindow or Windowed.");
            ForceDelay = Config.Bind("Resolution", "DelaySeconds", 2f,
                new ConfigDescription("Wait this long after launch before forcing the resolution, so the game's own setup runs first.", new AcceptableValueRange<float>(0f, 120f)));
            ForceWindow = Config.Bind("Resolution", "RetrySeconds", 15f,
                new ConfigDescription("Keep re-applying for this many seconds if the game changes it back. After that the in-game settings are left alone.", new AcceptableValueRange<float>(0f, 120f)));

            Enabled.SettingChanged += (_, _) => UwBehaviour.RequestResolutionRecovery();
            ForceResolution.SettingChanged += (_, _) => UwBehaviour.RequestResolutionRecovery();

            var helpers = typeof(ClassInjector).Assembly.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers");
            var hook = helpers.GetField("GenericMethodGetMethodHook", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            hook.GetType().BaseType.GetField("_isApplied", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(hook, true);
            ClassInjector.RegisterTypeInIl2Cpp<UwBehaviour>();
            AddComponent<UwBehaviour>();
            AddComponent<HudSizing>();
            AddComponent<EncounterLayout>();
            AddComponent<MainMenuLayout>();
            AddComponent<SelectionLayout>();
            ClassInjector.RegisterTypeInIl2Cpp<MenuGraphic>();
            AddComponent<ModMenu>();
            new Harmony("local.starvaders.ultrawide.scaling").PatchAll(typeof(CanvasScalingPatch));
            new Harmony("local.starvaders.ultrawide.selection").PatchAll(typeof(SelectionLayoutStart));
            new Harmony("local.starvaders.ultrawide.menus").PatchAll(typeof(SettingsMenuLifecycle));
            new Harmony("local.starvaders.ultrawide.menus").PatchAll(typeof(SettingsMenuExitLifecycle));
            new Harmony("local.starvaders.ultrawide.menus").PatchAll(typeof(SettingsMenuDisableLifecycle));
            Log.LogInfo($"StarVaders Ultrawide by NorthernDale loaded. Toggle = {Hotkeys.ToggleFix.Value}; diagnostics = {Hotkeys.Diagnostics.Value}; menu = {Hotkeys.Menu.Value}.");
        }
    }

    public class UwBehaviour : MonoBehaviour
    {
        public UwBehaviour(IntPtr ptr) : base(ptr) { }

        static float _next;
        static float _nextForce;
        static float _nextError;
        static bool _recoveryRequested;
        static bool _forceWindowStarted;
        static float _forceUntil;

        internal static void RequestResolutionRecovery()
        {
            _next = 0f;
            // Both the hotkey and menu change the same config entry.
            // Defer Unity calls until Update on the game thread.
            _recoveryRequested = Plugin.Enabled.Value && Plugin.ForceResolution.Value;
        }
        static readonly Dictionary<int, (CanvasScaler Scaler, float Match)> Original = new();
        static readonly List<int> Stale = new();
        static readonly List<CanvasScaler> Refresh = new();
        static bool _scalersDiscovered;

        void OnDestroy()
        {
            foreach (var entry in Original.Values)
                if (entry.Scaler != null) entry.Scaler.matchWidthOrHeight = entry.Match;
            Original.Clear();
        }

        void Update()
        {
            try
            {
                var kb = Keyboard.current;
                if (kb != null)
                {
                    if (Hotkeys.Pressed(Hotkeys.ToggleFix))
                    {
                        Plugin.Enabled.Value = !Plugin.Enabled.Value;
                        Plugin.Instance.Log.LogInfo("Fix enabled = " + Plugin.Enabled.Value);
                        _next = 0f;
                    }
                    if (Hotkeys.Pressed(Hotkeys.Diagnostics)) Dump();
                }

                ForceRes();

                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.25f;
                Apply();
            }
            catch (Exception e)
            {
                if (Time.unscaledTime >= _nextError)
                {
                    _nextError = Time.unscaledTime + 5f;
                    Plugin.Instance.Log.LogError(e.ToString());
                }
            }
        }

        static void ForceRes()
        {
            if (!Plugin.Enabled.Value || !Plugin.ForceResolution.Value) return;
            float t = Time.unscaledTime;
            if (_recoveryRequested)
            {
                _recoveryRequested = false;
                _forceWindowStarted = true;
                _forceUntil = t + Plugin.ForceWindow.Value;
                _nextForce = t;
                Plugin.Instance.Log.LogInfo("Resolution recovery requested by re-enabling the fix.");
            }
            if (!_forceWindowStarted)
            {
                if (t < Plugin.ForceDelay.Value) return;
                _forceWindowStarted = true;
                _forceUntil = t + Plugin.ForceWindow.Value;
            }
            if (t > _forceUntil) return;
            if (t < _nextForce) return;
            _nextForce = t + 1f;

            int w = Plugin.ResWidth.Value, h = Plugin.ResHeight.Value;
            if (w <= 0) w = Display.main.systemWidth;
            if (h <= 0) h = Display.main.systemHeight;
            if (w <= 0 || h <= 0) return;


            FullScreenMode mode;
            if (!Enum.TryParse(Plugin.ScreenMode.Value, true, out mode) || !Enum.IsDefined(typeof(FullScreenMode), mode)) mode = FullScreenMode.FullScreenWindow;
            if (Screen.width == w && Screen.height == h && Screen.fullScreenMode == mode) return;
            Plugin.Instance.Log.LogInfo($"Forcing resolution {w}x{h} {mode} (was {Screen.width}x{Screen.height})");
            Screen.SetResolution(w, h, mode);
        }

        static void Apply()
        {
            Stale.Clear();
            foreach (var entry in Original)
                if (entry.Value.Scaler == null) Stale.Add(entry.Key);
            foreach (int id in Stale) Original.Remove(id);
            if (Screen.height <= 0) return;
            // Bootstrap canvases that existed before the Harmony hook was installed.
            // New canvases are discovered by CanvasScalingPatch as Unity updates them.
            if (!_scalersDiscovered)
            {
                _scalersDiscovered = true;
                foreach (var s in UnityEngine.Object.FindObjectsOfType<CanvasScaler>()) ApplyScaler(s);
            }
            Refresh.Clear();
            foreach (var entry in Original.Values) Refresh.Add(entry.Scaler);
            foreach (var s in Refresh) ApplyScaler(s);
        }

        internal static void ApplyScaler(CanvasScaler s)
        {
                if (s == null || Screen.height <= 0 || Plugin.Enabled == null) return;
                if (s.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
                if (s.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight) return;
                float aspect = (float)Screen.width / Screen.height;

                int id = s.GetInstanceID();
                var rr = s.referenceResolution;
                float refAspect = rr.y > 0 ? rr.x / rr.y : 16f / 9f;
                bool want = Plugin.Enabled.Value && aspect > refAspect + 0.01f;

                if (want)
                {
                    if (!Original.TryGetValue(id, out var saved) || saved.Scaler != s)
                        Original[id] = (s, s.matchWidthOrHeight);
                    if (Math.Abs(s.matchWidthOrHeight - Mathf.Clamp01(Plugin.MatchValue.Value)) > 0.001f)
                        s.matchWidthOrHeight = Mathf.Clamp01(Plugin.MatchValue.Value);
                }
                else if (Original.TryGetValue(id, out var orig))
                {
                    if (orig.Scaler == s) s.matchWidthOrHeight = orig.Match;
                    Original.Remove(id);
                }
        }

        static void Dump()
        {
            var log = Plugin.Instance.Log;
            var sb = new StringBuilder();
            sb.AppendLine($"=== DUMP === screen {Screen.width}x{Screen.height} aspect {(float)Screen.width / Screen.height:F3} fullscreen={Screen.fullScreenMode}");

            foreach (var s in UnityEngine.Object.FindObjectsOfType<CanvasScaler>())
            {
                if (s == null) continue;
                sb.AppendLine($"CanvasScaler '{s.gameObject.name}' mode={s.uiScaleMode} ref={s.referenceResolution} " +
                              $"match={s.matchWidthOrHeight} matchMode={s.screenMatchMode} scaleFactor={s.scaleFactor}");
            }

            foreach (var c in Camera.allCameras)
            {
                if (c == null) continue;
                sb.AppendLine($"Camera '{c.name}' ortho={c.orthographic} orthoSize={c.orthographicSize} fov={c.fieldOfView} " +
                              $"aspect={c.aspect:F3} rect={c.rect} pixelRect={c.pixelRect} depth={c.depth}");
                foreach (var comp in c.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    sb.AppendLine("    component: " + comp.GetIl2CppType().Name);
                }
            }
            log.LogInfo(sb.ToString());
        }
    }

    [HarmonyPatch(typeof(CanvasScaler), nameof(CanvasScaler.HandleScaleWithScreenSize))]
    internal static class CanvasScalingPatch
    {
        static void Prefix(CanvasScaler __instance) => UwBehaviour.ApplyScaler(__instance);
    }
}

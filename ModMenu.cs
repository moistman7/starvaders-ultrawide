using System;
using System.Collections.Generic;
using StarVaders;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

using Il2CppInterop.Runtime.Attributes;
using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace StarVadersUltrawide
{
    // Reuse the settings screen's actual slider artwork and font.
    // Clones get fresh events and disabled game scripts before activation.
    public class ModMenu : MonoBehaviour
    {
        public ModMenu(IntPtr pointer) : base(pointer) { }
        static ModMenu _live;

        void Awake() => _live = this;

        // StarVaders creates its pause screen on demand; take it as soon as it
        // starts instead of waiting for the once-per-second scene search.
        internal static void PauseCreated(PauseMenuController pause)
        {
            if (_live != null && pause != null) _live._pause = pause;
        }

        internal static void SettingsOpened(SettingsSceneController settings)
        {
            if (_live == null) return;
            _live._closingSettings = null;
            _live._settings = settings;
            _live.ShowEntryNow(true);
        }

        internal static void SettingsClosing(SettingsSceneController settings)
        {
            if (_live == null) return;
            _live._closingSettings = settings;
            if (_live._entry != null) _live._entry.SetActive(false);
            if (_live._panel != null && _live._panel.activeSelf) _live.SetOpen(false);
        }


        void ShowEntryNow(bool settings)
        {
            _nextSearch = Time.unscaledTime + 1f;
            if (_root == null && HudSizing._hudScale != null) Build(_settings);
            if (_entry == null) return;
            PositionEntry(settings, !settings);
            _entry.SetActive(true);
            Plugin.Instance.Log.LogDebug(settings ? "Settings entry shown by OnEnable hook." : "Pause entry prepared by OnPause hook.");
        }
        GameObject _root;
        GameObject _panel;
        RectTransform _close;
        bool _closePressed, _closedWithMouseDown;
        // Set when the menu opens on a mouse press: the panel takes no mouse
        // input until that press is released, so its release cannot click
        // whatever button opened under the cursor (often CLOSE).
        bool _awaitingRelease;
        float _closedTime = -10f;
        SettingsSceneController _settings;
        SettingsSceneController _closingSettings;

        Font _labelFont;
        readonly MenuFonts _fonts = new();
        Font _settingsFont, _settingsTitleFont, _pauseFont;
        int _pauseVisibleFrame = -1;
        Text _titleLabel;
        readonly Dictionary<int, int> _baseTextSizes = new();
        readonly Dictionary<int, Vector2> _baseTextRects = new();
        readonly Dictionary<int, float> _fontSizeScales = new();


        Slider _hud;
        Slider _board;
        Text _hudLabel;
        Text _boardLabel;
        Text _enabledLabel;
        Text _hudLayoutLabel;
        bool? _syncedCenterHud;
        float _nextSearch;
        bool _syncing;

        GameObject _entry;
        readonly List<MonoBehaviour> _suspendedInput = new();
        float _previousTimeScale;
        bool _previousCursorVisible;
        CursorLockMode _previousCursorLock;
        int _scene;
        PauseMenuController _pause;
        GameObject _eventRoot;
        readonly List<EventSystem> _suspendedEvents = new();

        bool _smokeOpened;
        float _smokeCloseAt;
        bool _captureStarted;
        bool _buttonTestDone;
        bool _fontTestDone;
        readonly bool _smokeTest = Environment.GetEnvironmentVariable("STARVADERS_UW_MENU_TEST") == "1";
        RectTransform _entryRect;
        float _syncedHud = float.NaN, _syncedBoard = float.NaN;
        bool? _syncedEnabled;
        Text _entryLabel;
        Font _entryFont;
        bool? _entrySettings;
        MenuGraphic _entryArtwork;
        GameObject _entryBorder;



        void Update() { long started = Profile.Start(); try { UpdateCore(); } finally { Profile.Stop(started, Profile.Hook.Menu); } }
        void UpdateCore()
        {
            int scene = SceneManager.GetActiveScene().handle;
            if (_scene != scene)
            {
                if (_panel != null && _panel.activeSelf) SetOpen(false);
                _scene = scene;
                _settings = null;
                _pause = null;
                _nextSearch = 0f;
            }
            var keyboard = Keyboard.current;
            bool smoke = _smokeTest;
            bool toggle = Hotkeys.Pressed(Hotkeys.Menu) || (smoke && !_smokeOpened && Time.unscaledTime > 5f && Time.unscaledTime >= _nextSearch);
            if (Time.unscaledTime >= _nextSearch || toggle)
            {
                _nextSearch = Time.unscaledTime + 1f;
                _fonts.CaptureAvailable();
                if (_pause == null)
                foreach (var pause in UnityEngine.Object.FindObjectsOfType<PauseMenuController>(true))
                {
                    if (!pause.gameObject.scene.IsValid()) continue;
                    if (pause.isActiveAndEnabled) { _pause = pause; break; }
                    if (_pause == null) _pause = pause;
                }
                if (_settings == null)
                foreach (var settings in UnityEngine.Object.FindObjectsOfType<SettingsSceneController>(true))
                {
                    if (!settings.gameObject.scene.IsValid()) continue;
                    if (settings.isActiveAndEnabled) { _settings = settings; break; }
                    if (_settings == null) _settings = settings;
                }
                if (_root == null && HudSizing._hudScale != null)
                {
                    try { Build(_settings); }
                    catch (Exception error)
                    {
                        Plugin.Instance.Log.LogError("Mod menu creation failed: " + error);
                        if (_root != null) UnityEngine.Object.Destroy(_root);
                        _root = null;
                        _nextSearch = Time.unscaledTime + 10f;
                    }
                }
            }
            if (_root == null) return;
            bool exitingOptions = _closingSettings != null && _closingSettings.isActiveAndEnabled;
            bool inSettings = !exitingOptions && _settings != null && _settings.isActiveAndEnabled;
            bool inPause = !exitingOptions && !inSettings && PauseVisible();
            bool showEntry = inSettings || inPause;
            if (_entry.activeSelf != showEntry) _entry.SetActive(showEntry);
            if (!exitingOptions && !inSettings && PauseShown()) UpdatePauseRow(); else HidePauseRow();
            if (showEntry) PositionEntry(inSettings, inPause);
            if (_panel.activeSelf && !inSettings && _pauseFont != null && _hudLabel.font != _pauseFont) ApplyPanelFonts();

            var mouse = Mouse.current;
            // A mouse held through the close no longer blocks once it is released.
            if (_closedWithMouseDown && (mouse == null || !mouse.leftButton.isPressed)) _closedWithMouseDown = false;
            if (!_panel.activeSelf && _entry.activeSelf && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                RectTransformUtility.RectangleContainsScreenPoint(_entryRect, mouse.position.ReadValue(), null))
                OpenFromEntry("entry press");
            if (toggle)
            {
                if (_panel.activeSelf) SetOpen(false, "hotkey");
                else OpenFromEntry("hotkey");
            }
            if (_panel.activeSelf)
            {
                if (smoke && !_smokeOpened)
                {
                    _smokeOpened = true;
                    _smokeCloseAt = Time.unscaledTime + 2f;
                }
                Sync();
                if (smoke && !_fontTestDone && Environment.GetEnvironmentVariable("STARVADERS_UW_FONT_TEST") == "1")
                {
                    _fontTestDone = true;
                    StartCoroutine(TestFontsAfterUnload().WrapToIl2Cpp());
                }
                if (smoke && !_buttonTestDone && Environment.GetEnvironmentVariable("STARVADERS_UW_BUTTON_TEST") == "1")
                {
                    _buttonTestDone = true;
                    StartCoroutine(TestButtonsAfterLayout().WrapToIl2Cpp());
                }
                string capture = Environment.GetEnvironmentVariable("STARVADERS_UW_MENU_CAPTURE");
                if (smoke && !_captureStarted && !string.IsNullOrEmpty(capture) && Time.unscaledTime >= _smokeCloseAt - 1f)
                {
                    _captureStarted = true;
                    StartCoroutine(CaptureAfterRender(capture).WrapToIl2Cpp());
                }
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetOpen(false, "escape");
                // The UI module can miss a click on the frame its input is enabled.
                // Closing is idempotent, so also read the mouse directly.
                if (_awaitingRelease)
                {
                    // Enable UI input the frame after the opening press is released.
                    if (mouse == null || (!mouse.leftButton.isPressed && !mouse.leftButton.wasReleasedThisFrame))
                    {
                        _awaitingRelease = false;
                        _eventRoot.SetActive(true);
                    }
                }
                else if (mouse != null && _close != null)
                {
                    bool over = RectTransformUtility.RectangleContainsScreenPoint(_close, mouse.position.ReadValue(), null);
                    if (mouse.leftButton.wasPressedThisFrame) _closePressed = over;
                    if (mouse.leftButton.wasReleasedThisFrame && _closePressed && over) SetOpen(false, "close mouse");
                }
                if (smoke && _smokeOpened && Time.unscaledTime >= _smokeCloseAt)
                {
                    SetOpen(false);
                    _smokeCloseAt = float.PositiveInfinity;
                    Plugin.Instance.Log.LogInfo($"Menu smoke test closed: restored timeScale={Time.timeScale}; previous EventSystems restored.");
                }
            }
        }

        [HideFromIl2Cpp]
        System.Collections.IEnumerator TestFontsAfterUnload()
        {
            var copies = new List<Font>();
            foreach (var label in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                var copy = _fonts.Copy(label);
                if (copy != null && !copies.Contains(copy)) copies.Add(copy);
                var pauseCopy = _fonts.CopyPause(label);
                if (pauseCopy != null && !copies.Contains(pauseCopy)) copies.Add(pauseCopy);
            }
            yield return Resources.UnloadUnusedAssets();
            int alive = 0;
            foreach (var font in copies)
                if (font != null && font.HasCharacter('U')) alive++;
            if (copies.Count > 0 && alive == copies.Count)
                Plugin.Instance.Log.LogInfo($"FONT TEST PASS: {alive} copied native fonts retained their glyphs after unused-asset unloading.");
            else Plugin.Instance.Log.LogError($"FONT TEST FAIL: {alive}/{copies.Count} copied native fonts available after unloading.");
        }

        [HideFromIl2Cpp]
        System.Collections.IEnumerator TestButtonsAfterLayout()
        {
            yield return null;
            yield return null;
            TestButtons();
        }

        void TestButtons()
        {
            bool enabled = Plugin.Enabled.Value;
            bool centered = HudSizing._centerHud.Value;
            float hud = HudSizing._hudScale.Value, board = HudSizing._boardScale.Value;
            try
            {
                ClickTestButton("EnableFix");
                if (Plugin.Enabled.Value == enabled) throw new InvalidOperationException("Toggle button callback did not run.");
                ClickTestButton("HUD FRAME");
                if (HudSizing._centerHud.Value == centered) throw new InvalidOperationException("HUD frame toggle callback did not run.");
                ClickTestButton("HUD FRAME");
                if (HudSizing._centerHud.Value != centered) throw new InvalidOperationException("HUD frame toggle did not restore.");
                HudSizing._hudScale.Value = 1.01f;
                HudSizing._boardScale.Value = 1.02f;
                ClickTestButton("RESET TO 100%");
                if (Math.Abs(HudSizing._hudScale.Value - HudSizing.DefaultHudScale) > 0.001f ||
                    Math.Abs(HudSizing._boardScale.Value - HudSizing.DefaultBoardScale) > 0.001f)
                    throw new InvalidOperationException("Reset button callback did not run.");
                ClickTestButton("CLOSE");
                if (_panel.activeSelf) throw new InvalidOperationException("Close button callback did not run.");
                Plugin.Instance.Log.LogInfo("BUTTON TEST PASS: Toggle, HUD frame, Reset, Close are the top raycast targets and their native pointer-click callbacks work.");
            }
            catch (Exception error) { Plugin.Instance.Log.LogError("BUTTON TEST FAIL: " + error); }
            finally
            {
                HudSizing._centerHud.Value = centered;
                Plugin.Enabled.Value = enabled;
                HudSizing._hudScale.Value = hud;
                HudSizing._boardScale.Value = board;
                if (_panel.activeSelf) SetOpen(false, "test");
                _smokeCloseAt = float.PositiveInfinity;
            }
        }

        void ClickTestButton(string name)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var button in _panel.GetComponentsInChildren<Button>())
            {
                if (button.gameObject.name != name) continue;
                var rect = button.GetComponent<RectTransform>();
                var data = new PointerEventData(_eventRoot.GetComponent<EventSystem>());
                data.position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
                data.button = PointerEventData.InputButton.Left;
                var results = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
                _eventRoot.GetComponent<EventSystem>().RaycastAll(data, results);
                if (results.Count == 0 || results[0].gameObject.GetComponentInParent<Button>() != button)
                    throw new InvalidOperationException($"Button is not the top raycast target: {name}; hits={results.Count}; first={(results.Count > 0 ? results[0].gameObject.name : "none")}; depth={button.GetComponent<Image>().depth}; culled={button.GetComponent<Image>().canvasRenderer.cull}");
                button.OnPointerClick(data);
                return;
            }
            throw new InvalidOperationException("Button not found: " + name);
        }

        [HideFromIl2Cpp]
        System.Collections.IEnumerator CaptureAfterRender(string path)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                if (texture != null)
                {
                    System.IO.File.WriteAllBytes(path, ImageConversion.EncodeToPNG(texture));
                    UnityEngine.Object.Destroy(texture);
                    Plugin.Instance.Log.LogInfo("End-of-frame menu capture saved.");
                }
            }
            catch (Exception error) { Plugin.Instance.Log.LogWarning("Menu capture unavailable: " + error.Message); }
        }

        // A click that closes the panel can also land on the entry beneath it.
        // Ignore entry opens until the mouse is released after a close.
        void OpenFromEntry(string source)
        {
            var mouse = Mouse.current;
            // The stray click arrives within a frame or two of the close.
            if (source != "hotkey" && (Time.unscaledTime - _closedTime < .2f || (_closedWithMouseDown && mouse != null && mouse.leftButton.isPressed)))
            {
                Plugin.Instance.Log.LogInfo($"Menu reopen ignored ({source}) right after close.");
                return;
            }
            _closedWithMouseDown = false;
            Plugin.Instance.Log.LogInfo($"Menu open requested ({source}).");
            SetOpen(true);
        }

        // StarVaders has no paused flag; the pause screen fades its canvas
        // group in and only accepts clicks while it is fully shown.
        bool PauseVisible()
        {
            if (_pause == null || !_pause.isActiveAndEnabled || _pause.IsEndingScene) return false;
            var group = _pause._canvasGroup;
            return group != null && group.alpha > .9f && group.blocksRaycasts && group.interactable;
        }

        void PositionEntry(bool inSettings, bool inPause)
        {
            CaptureFonts(inSettings, inPause);
            Font font = inSettings ? _settingsFont : _pauseFont;
            if (_entryLabel == null) _entryLabel = _entry.GetComponentInChildren<Text>();
            if (_entrySettings != inSettings || _entryFont != font)
            {
                _entryBorder.SetActive(inSettings);
                _entryArtwork.Bottom = inSettings ? .10f : .22f;
                _entryArtwork.Top = inSettings ? .90f : .78f;
                // Pause: the size of the native Show Guide button on its dock.
                _entryArtwork.rectTransform.sizeDelta = new Vector2(inSettings ? 352 : 304, inSettings ? 58 : 66);
                _entry.GetComponent<RectTransform>().sizeDelta = new Vector2(inSettings ? 360 : 304, 66);
                // Pause: the teal of the native OPTIONS button.
                _entryArtwork.color = inSettings ? new Color(.08f, .24f, .58f, 1f) : new Color(.07f, .56f, .62f, 1f);
                _entryArtwork.SetVerticesDirty();
                _entryLabel.font = font != null ? font : _labelFont;
                _entryLabel.color = Color.white;
                _entryLabel.fontStyle = font != null ? FontStyle.Normal : FontStyle.Bold;
                SizeNativeLabel(_entryLabel, font, 28, inSettings);
                _entrySettings = inSettings;
                _entryFont = font;
            }
            if (_entryRect == null) _entryRect = _entry.GetComponent<RectTransform>();
            var position = inSettings ? new Vector2(0, -470) : _pauseEntryPosition;
            if (_entryRect.anchoredPosition != position) _entryRect.anchoredPosition = position;
            var size = inSettings ? new Vector2(360, 66) : _pauseEntrySize;
            if (_entryRect.sizeDelta != size) _entryRect.sizeDelta = size;
            // In pause the copied native button is the visual; the entry is
            // only its click area.
            if (_entryArtwork.enabled == inPause) _entryArtwork.enabled = !inPause;
            if (_entryLabel.enabled == inPause) _entryLabel.enabled = !inPause;
        }

        // The pause screen's bottom row: Show Guide and Abandon Run each sit on a
        // black dock (GuidePanel / AbandonPanel, bottom-center anchored). The mod
        // entry gets a copy of that dock, centered, and the two native docks
        // move out symmetrically while the pause screen is shown.
        const float PauseDockSpacing = 410f;
        GameObject _pauseDock;
        RectTransform _guideDock, _abandonDock;
        // Native X only: the game slides the docks up on its own, so their
        // height is never touched.
        float _guideNativeX, _abandonNativeX;

        Vector2 _pauseEntryPosition = new(0, -461), _pauseEntrySize = new(304, 96);
        CanvasGroup _pauseDockGroup;
        RectTransform _pauseDockButton;

        // Shown from the first frame of the pause fade-in, so the copied button
        // slides and fades in together with the native row.
        bool PauseShown()
        {
            if (_pause == null || !_pause.isActiveAndEnabled || _pause.IsEndingScene) return false;
            var group = _pause._canvasGroup;
            return group != null && group.alpha > .01f;
        }

        void UpdatePauseRow()
        {
            var canvas = _pause?.MainPanel?.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) return;
            var group = canvas.transform.Find("CanvasGroup");
            var guide = group?.Find("GuidePanel")?.TryCast<RectTransform>();
            var abandon = group?.Find("AbandonPanel")?.TryCast<RectTransform>();
            if (guide == null || abandon == null) return;
            if (_guideDock != guide)
            {
                _guideDock = guide;
                _guideNativeX = guide.anchoredPosition.x;
            }
            if (_abandonDock != abandon)
            {
                _abandonDock = abandon;
                _abandonNativeX = abandon.anchoredPosition.x;
            }
            SetDockX(guide, -PauseDockSpacing);
            SetDockX(abandon, PauseDockSpacing);
            if (_pauseDock == null) BuildPauseDock(guide);
            if (_pauseDock == null) return;
            if (!_pauseDock.activeSelf) _pauseDock.SetActive(true);
            // Follow the native fade and slide-in. Both canvases are 1080 units
            // tall and centered, so the native dock's local height maps directly.
            float alpha = _pause._canvasGroup.alpha;
            if (_pauseDockGroup.alpha != alpha) _pauseDockGroup.alpha = alpha;
            var dockRect = _pauseDock.GetComponent<RectTransform>();
            var dockPosition = new Vector2(0f, guide.localPosition.y);
            if (dockRect.anchoredPosition != dockPosition) dockRect.anchoredPosition = dockPosition;
            if (_pauseDockButton != null)
            {
                _pauseEntryPosition = dockPosition + (Vector2)_pauseDockButton.localPosition;
                _pauseEntrySize = _pauseDockButton.rect.size;
            }
        }

        // A visual-only copy of the native Show Guide dock and button, relabeled.
        // It is cloned under an inactive holder so none of the game's scripts
        // wake up on the copy, then everything but the visuals is removed.
        void BuildPauseDock(RectTransform guide)
        {
            var holder = new GameObject("UltrawidePauseDockHolder");
            holder.SetActive(false);
            holder.transform.SetParent(_root.transform, false);
            var dock = UnityEngine.Object.Instantiate(guide.gameObject, holder.transform, false);
            dock.name = "UltrawidePauseDock";
            foreach (var behaviour in dock.GetComponentsInChildren<Behaviour>(true))
                if (!IsVisual(behaviour)) UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (var graphic in dock.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var text in dock.GetComponentsInChildren<TextMeshProUGUI>(true)) text.text = "Ultrawide Mod";
            _pauseDockButton = dock.transform.Find("Guide")?.TryCast<RectTransform>();
            _pauseDockGroup = dock.GetComponent<CanvasGroup>() ?? dock.AddComponent<CanvasGroup>();
            _pauseDockGroup.interactable = _pauseDockGroup.blocksRaycasts = false;
            var rect = dock.GetComponent<RectTransform>();
            rect.SetParent(_root.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.localScale = Vector3.one;
            rect.SetSiblingIndex(_entry.transform.GetSiblingIndex());
            UnityEngine.Object.Destroy(holder);
            _pauseDock = dock;
        }

        static bool IsVisual(Behaviour behaviour) =>
            behaviour.TryCast<Graphic>() != null || behaviour.TryCast<CanvasGroup>() != null ||
            behaviour.TryCast<Mask>() != null || behaviour.TryCast<RectMask2D>() != null ||
            behaviour.TryCast<LayoutGroup>() != null || behaviour.TryCast<ContentSizeFitter>() != null ||
            behaviour.TryCast<BaseMeshEffect>() != null;

        static void SetDockX(RectTransform dock, float x)
        {
            if (dock == null || Mathf.Abs(dock.anchoredPosition.x - x) < .01f) return;
            dock.anchoredPosition = new Vector2(x, dock.anchoredPosition.y);
        }

        void HidePauseRow()
        {
            if (_pauseDock != null && _pauseDock.activeSelf) _pauseDock.SetActive(false);
            SetDockX(_guideDock, _guideNativeX);
            SetDockX(_abandonDock, _abandonNativeX);
        }

        void OnDestroy()
        {
            HidePauseRow();
            if (_live == this) _live = null;
            if (_panel != null && _panel.activeSelf) SetOpen(false);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            if (_labelFont != null) UnityEngine.Object.Destroy(_labelFont);
            _fonts.Clear();
        }

        void CaptureFonts(bool settings, bool pause)
        {
            if (settings && _settings != null)
            {
                if (_settingsFont == null && _settings.MainPanel != null)
                    _settingsFont = _fonts.Copy(_settings.MainPanel.GetComponentInChildren<TextMeshProUGUI>(true));
                if (_settingsFont == null)
                    foreach (var label in _settings.GetComponentsInChildren<TextMeshProUGUI>(true))
                        if (label.text != null && (label.text.ToLowerInvariant().Contains("apply") || label.text.ToLowerInvariant().Contains("master volume")))
                        {
                            _settingsFont = _fonts.Copy(label);
                            if (_settingsFont != null) break;
                        }
                if (_settingsTitleFont == null)
                    foreach (var label in _settings.GetComponentsInChildren<TextMeshProUGUI>(true))
                        if (label.text != null && label.text.ToUpperInvariant().Contains("OPTIONS"))
                        { _settingsTitleFont = _fonts.Copy(label); break; }
                // These are the native English button and heading families, also
                // loaded by the main menu before stripped Settings assets appear.
                if (_settingsFont == null) _settingsFont = _fonts.Find("StarvadersGun");
                if (_settingsTitleFont == null) _settingsTitleFont = _fonts.Find("Blockvaders");
            }
            if (pause && _pause != null && _pauseFont == null && _pause.MainPanel != null)
            {
                var label = _pause.MainPanel.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label == null || !label.isActiveAndEnabled) { _pauseVisibleFrame = -1; return; }
                if (_pauseVisibleFrame < 0) _pauseVisibleFrame = Time.frameCount;
                if (Time.frameCount > _pauseVisibleFrame) _pauseFont = _fonts.CopyPause(label);
                // StarVaders strips the pause TMP font's source file; its buttons
                // use the Blockvaders family, which the main menu already loaded.
                if (Time.frameCount > _pauseVisibleFrame && _pauseFont == null) _pauseFont = _fonts.Find("Blockvaders");
            }
        }

        void ApplyPanelFonts()
        {
            bool settings = _settings != null && _settings.isActiveAndEnabled;
            CaptureFonts(settings, _pause != null);
            Font body = settings ? _settingsFont : (_pause != null ? _pauseFont : _fonts.Find("StarvadersGun"));
            Font title = settings ? _settingsTitleFont : (_pause != null ? _pauseFont : _fonts.Find("Blockvaders"));
            foreach (var label in _panel.GetComponentsInChildren<Text>(true))
            {
                var font = label == _titleLabel ? title : body;
                label.font = font != null ? font : _labelFont;
                label.fontStyle = font != null ? FontStyle.Normal : FontStyle.Bold;
                if (_baseTextSizes.TryGetValue(label.GetInstanceID(), out int size))
                    SizeNativeLabel(label, font, size, settings);
            }
        }

        void SizeNativeLabel(Text label, Font font, int baseSize, bool settings)
        {
            _baseTextRects.TryGetValue(label.GetInstanceID(), out Vector2 rect);
            if (font != null && font.name.Contains("Blockvaders"))
            {
                // Rasterize more densely, then downscale. Grow the layout rect by
                // the same amount so the visible size and alignment do not change.
                int resolution = font.dynamic && !settings ? 3 : 1;
                label.fontSize = baseSize * resolution;
                if (rect != Vector2.zero) label.rectTransform.sizeDelta = rect * resolution;
                label.rectTransform.localScale = Vector3.one * ((settings ? 1.6f : 1.7f) / resolution);
            }
            else
            {
                if (rect != Vector2.zero) label.rectTransform.sizeDelta = rect;
                label.rectTransform.localScale = Vector3.one;
                label.fontSize = Mathf.RoundToInt(baseSize * FontSizeScale(font) * (settings ? 1f : 1.15f));
            }
        }

        float FontSizeScale(Font font)
        {
            if (font == null || font == _labelFont) return 1f;
            if (font.name.Contains("Blockvaders")) return 1f;
            int id = font.GetInstanceID();
            if (_fontSizeScales.TryGetValue(id, out float cached)) return cached;
            if (font.dynamic) font.RequestCharactersInTexture("H", 32, FontStyle.Normal);
            _labelFont.RequestCharactersInTexture("H", 32, FontStyle.Bold);
            if (font.GetCharacterInfo('H', out CharacterInfo native, 32, FontStyle.Normal) &&
                _labelFont.GetCharacterInfo('H', out CharacterInfo baseline, 32, FontStyle.Bold))
            {
                int height = native.maxY - native.minY;
                if (height > 0)
                {
                    float scale = Mathf.Clamp((float)(baseline.maxY - baseline.minY) / height, 0.75f, 2.5f);
                    _fontSizeScales[id] = scale;
                    return scale;
                }
            }
            float fallback = font.name.Contains("Blockvaders") ? 1.6f : 1f;
            _fontSizeScales[id] = fallback;
            return fallback;
        }

        void EnsureFont()
        {
            if (_labelFont != null) return;
            _labelFont = Font.CreateDynamicFontFromOSFont("Arial", 32);
            _labelFont.hideFlags = HideFlags.HideAndDontSave;
            _labelFont.RequestCharactersInTexture("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789%:/.- ", 32, FontStyle.Bold);
            if (_root != null)
                foreach (var label in _root.GetComponentsInChildren<Text>(true)) label.font = _labelFont;
        }

        void SetOpen(bool open, string reason = "other")
        {
            if (_panel == null || _panel.activeSelf == open) return;
            _closePressed = false;
            _awaitingRelease = false;
            if (!open)
            {
                _closedTime = Time.unscaledTime;
                _closedWithMouseDown = Mouse.current != null && Mouse.current.leftButton.isPressed;
                Plugin.Instance.Log.LogInfo($"Menu closed ({reason}).");
            }
            if (open)
            {
                EnsureFont();
                ApplyPanelFonts();
                _previousTimeScale = Time.timeScale;
                _previousCursorVisible = Cursor.visible;
                _previousCursorLock = Cursor.lockState;
                _suspendedInput.Clear();
                foreach (var behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                {
                    var type = behaviour.GetIl2CppType();
                    if (behaviour.enabled && type.Namespace != null && type.Namespace.StartsWith("StarVaders") &&
                        (type.Name.Contains("InputHandler") || type.Name.Contains("KeyInputController")))
                    {
                        behaviour.enabled = false;
                        _suspendedInput.Add(behaviour);
                    }
                }
                Time.timeScale = 0f;
                _suspendedEvents.Clear();
                foreach (var events in UnityEngine.Object.FindObjectsOfType<EventSystem>())
                    if (events.enabled) { events.enabled = false; _suspendedEvents.Add(events); }
                _awaitingRelease = Mouse.current != null && Mouse.current.leftButton.isPressed;
                _eventRoot.SetActive(!_awaitingRelease);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                Sync();
            }
            else
            {
                Time.timeScale = _previousTimeScale;
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
                foreach (var behaviour in _suspendedInput)
                    if (behaviour != null) behaviour.enabled = true;
                _suspendedInput.Clear();
                _eventRoot.SetActive(false);
                foreach (var events in _suspendedEvents)
                    if (events != null) events.enabled = true;
                _suspendedEvents.Clear();
            }
            _panel.SetActive(open);
            if (open)
            {
                Canvas.ForceUpdateCanvases();
                _hudLabel.SetAllDirty();
                _boardLabel.SetAllDirty();
                Canvas.ForceUpdateCanvases();
                int vertices = _hudLabel.cachedTextGenerator.vertexCount;
                Plugin.Instance.Log.LogInfo($"Menu opened: font={(_hudLabel.font != null ? _hudLabel.font.name : "missing")}, HUD text vertices={vertices}, size={_hudLabel.rectTransform.rect.size}");
                if (vertices <= 4)
                    Plugin.Instance.Log.LogError("Mod menu text generated no visible geometry. Please include this log when reporting UI problems.");
            }
        }

        static RectTransform Rect(GameObject obj, Transform parent, Vector2 size, Vector2 position)
        {
            var rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var anchored = rect.anchoredPosition3D;
            rect.anchoredPosition3D = new Vector3(anchored.x, anchored.y, 0f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            return rect;
        }

        void Build(SettingsSceneController settings)
        {
            _syncedHud = _syncedBoard = float.NaN;
            _syncedEnabled = null;
            _syncedCenterHud = null;
            _entryLabel = null;
            _entryRect = null;
            _entrySettings = null;
            EnsureFont();
            _root = new GameObject("UltrawideModMenu");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2 |
                AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            _root.AddComponent<GraphicRaycaster>();
            _eventRoot = new GameObject("ModMenuInput");
            _eventRoot.SetActive(false);
            _eventRoot.transform.SetParent(_root.transform, false);
            _eventRoot.AddComponent<EventSystem>();
            _eventRoot.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            _entry = Button(_root.transform, "ULTRAWIDE MOD", new Vector2(0, -470), new Vector2(360, 66), () =>
            {
                OpenFromEntry("entry click");
            });
            _entryArtwork = _entry.transform.Find("Artwork").GetComponent<MenuGraphic>();
            _entryBorder = new GameObject("OptionsButtonBorder");
            Rect(_entryBorder, _entry.transform, new Vector2(360, 66), Vector2.zero);
            _entryBorder.transform.SetAsFirstSibling();
            var border = AddGraphic(_entryBorder);
            border.Bottom = .10f;
            border.Top = .90f;
            border.color = new Color(.32f, .85f, 1f, 1f);
            border.raycastTarget = false;
            _entryBorder.SetActive(false);
            // A fixed lower-center entry avoids changing native menu navigation.
            _panel = new GameObject("UltrawideSettings");
            var panelRect = Rect(_panel, _root.transform, Vector2.zero, Vector2.zero);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            var dim = _panel.AddComponent<Image>();
            dim.color = new Color(0.015f, 0.025f, 0.05f, 0.96f);
            dim.raycastTarget = true;
            var frame = new GameObject("Panel");
            Rect(frame, _panel.transform, new Vector2(900, 800), Vector2.zero);
            var background = AddGraphic(frame);
            background.Bottom = 0.04f;
            background.Top = 0.82f;
            background.Slant = 20f;
            background.color = new Color(0.075f, 0.18f, 0.21f, 1f);
            _titleLabel = Label(frame.transform, "ULTRAWIDE MOD", new Vector2(0, 270), new Vector2(800, 70), 42);
            _enabledLabel = Button(frame.transform, "", new Vector2(0, 170), new Vector2(620, 65), () =>
            {
                Plugin.Enabled.Value = !Plugin.Enabled.Value;
                Sync();
            }).GetComponentInChildren<Text>();
            _hudLabel = Label(frame.transform, "HUD SIZE", new Vector2(0, 90), new Vector2(760, 50), 30);
            _hud = MakeSlider(null, frame.transform, new Vector2(0, 35), value =>
            {
                if (!_syncing) HudSizing._hudScale.Value = Mathf.Round(value) / 100f;
            });
            _boardLabel = Label(frame.transform, "BOARD SIZE", new Vector2(0, -40), new Vector2(760, 50), 30);
            _board = MakeSlider(null, frame.transform, new Vector2(0, -95), value =>
            {
                if (!_syncing) HudSizing._boardScale.Value = Mathf.Round(value) / 100f;
            });
            var hudLayout = Button(frame.transform, "HUD FRAME", new Vector2(0, -175), new Vector2(620, 65), () =>
            {
                HudSizing._centerHud.Value = !HudSizing._centerHud.Value;
                Sync();
            });
            _hudLayoutLabel = hudLayout.GetComponentInChildren<Text>();
            Button(frame.transform, "RESET TO 100%", new Vector2(-190, -250), new Vector2(340, 70), () =>
            {
                HudSizing._hudScale.Value = HudSizing.DefaultHudScale;
                HudSizing._boardScale.Value = HudSizing.DefaultBoardScale;
                Sync();
            });
            _close = Button(frame.transform, "CLOSE", new Vector2(200, -250), new Vector2(300, 70), () => SetOpen(false, "close button")).GetComponent<RectTransform>();
            Label(frame.transform, $"Changes save automatically   /   Menu key: {Hotkeys.Menu.Value}", new Vector2(0, -330), new Vector2(850, 45), 22);
            Sync();
            _panel.SetActive(false);
            Plugin.Instance.Log.LogInfo($"Mod menu ready. {Hotkeys.Menu.Value} opens it anywhere; Settings also has an Ultrawide Mod entry.");
        }

        [HideFromIl2Cpp]
        Slider MakeSlider(Slider source, Transform parent, Vector2 position, Action<float> changed)
        {
            return MakeFallbackSlider(parent, position, changed);
        }

        [HideFromIl2Cpp]
        Slider MakeFallbackSlider(Transform parent, Vector2 position, Action<float> changed)
        {
            var obj = new GameObject("SizeSlider");
            Rect(obj, parent, new Vector2(620, 50), position);
            var hit = obj.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.canvasRenderer.cullTransparentMesh = false;
            var track = new GameObject("Track");
            Rect(track, obj.transform, new Vector2(620, 22), Vector2.zero);
            var trackImage = AddGraphic(track);

            trackImage.color = new Color(0.18f, 0.33f, 0.39f, 1f);
            var fill = new GameObject("Fill");
            var fillRect = Rect(fill, track.transform, Vector2.zero, Vector2.zero);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            var fillImage = AddGraphic(fill);

            fillImage.color = new Color(0.42f, 0.72f, 0.80f, 1f);
            var handle = new GameObject("Handle");
            var handleRect = Rect(handle, track.transform, new Vector2(20, 30), Vector2.zero);
            Image handleImage = null;
            for (int i = 0; i < 2; i++)
            {
                var slash = new GameObject("Slash");
                var slashRect = Rect(slash, handle.transform, new Vector2(3, 29), new Vector2(i * 7 - 3, 0));
                slashRect.localRotation = Quaternion.Euler(0, 0, -42);
                var slashImage = slash.AddComponent<Image>();
                slashImage.color = new Color(0.55f, 0.85f, 0.90f, 1f);
                slashImage.raycastTarget = false;
                if (i == 0) handleImage = slashImage;
            }
            var slider = obj.AddComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.minValue = 100;
            slider.maxValue = 135;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener((UnityAction<float>)(value => changed(value)));
            return slider;
        }
        static MenuGraphic AddGraphic(GameObject obj)
        {
            if (obj.GetComponent<CanvasRenderer>() == null) obj.AddComponent<CanvasRenderer>();
            return obj.AddComponent<MenuGraphic>();
        }

        Text Label(Transform parent, string value, Vector2 position, Vector2 size, float fontSize)
        {
            var obj = new GameObject("ModLabel");
            Rect(obj, parent, size, position);
            var label = obj.AddComponent<Text>();
            label.font = _labelFont;
            label.text = value;
            label.fontSize = (int)fontSize;
            _baseTextSizes[label.GetInstanceID()] = (int)fontSize;
            _baseTextRects[label.GetInstanceID()] = size;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.74f, 0.96f, 0.89f, 1);
            label.raycastTarget = false;
            label.maskable = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.canvasRenderer.SetAlpha(1f);
            return label;
        }
        [HideFromIl2Cpp]
        GameObject Button(Transform parent, string value, Vector2 position, Vector2 size, Action clicked)
        {
            var obj = new GameObject(value.Length == 0 ? "EnableFix" : value);
            Rect(obj, parent, size, position);
            // Use a standard Image for GraphicRaycaster registration. The custom
            // graphic is visual-only; it must not be the button's mouse target.
            var hit = obj.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.canvasRenderer.cullTransparentMesh = false;
            hit.raycastTarget = true;
            var artwork = new GameObject("Artwork");
            Rect(artwork, obj.transform, size, Vector2.zero);
            var image = AddGraphic(artwork);
            image.raycastTarget = false;
            image.Bottom = 0.22f;
            image.Top = 0.78f;
            image.color = new Color(0.12f, 0.34f, 0.32f, 1);
            var button = obj.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            button.onClick.AddListener((UnityAction)(() => clicked()));
            Label(obj.transform, value, Vector2.zero, size - new Vector2(20, 10), 28);
            return obj;
        }

        void Sync()
        {
            float hud = HudSizing._hudScale.Value, board = HudSizing._boardScale.Value;
            bool enabled = Plugin.Enabled.Value;
            bool centered = HudSizing._centerHud.Value;
            if (_syncedHud == hud && _syncedBoard == board && _syncedEnabled == enabled && _syncedCenterHud == centered) return;
            _syncing = true;
            _hud.SetValueWithoutNotify(HudSizing._hudScale.Value * 100);
            _board.SetValueWithoutNotify(HudSizing._boardScale.Value * 100);
            _hudLabel.text = $"HUD SIZE   {HudSizing._hudScale.Value:P0}";
            _boardLabel.text = $"BOARD SIZE   {HudSizing._boardScale.Value:P0}";
            _enabledLabel.text = "ULTRAWIDE FIX: " + (Plugin.Enabled.Value ? "ON" : "OFF");
            _hudLayoutLabel.text = centered ? "HUD FRAME: CENTERED 16:9" : "HUD FRAME: FULL WIDTH";
            _syncedCenterHud = centered;
            _syncedHud = hud;
            _syncedBoard = board;
            _syncedEnabled = enabled;
            _syncing = false;
        }
    }
}

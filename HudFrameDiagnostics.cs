using System;
using System.Collections.Generic;
using UnityEngine;
namespace StarVadersUltrawide;
public partial class HudSizing
{
    readonly bool _checkHudFrame = Environment.GetEnvironmentVariable("STARVADERS_UW_HUD_FRAME_TEST") == "1";
    float _checkFrameAfter, _menuReadyAt;
    bool _checkedFrame, _pressedFrame, _resumedFrame;
    void CheckHudFrame()
    {
        if (_checkHudFrame && _checkedFrame) WatchPauseLayout();
        if (!_checkHudFrame || _checkedFrame || !Plugin.Enabled.Value) return;
        if (_targets.Count == 0)
        {
            if (Time.unscaledTime < 10f) return;
            var boot = UnityEngine.Object.FindObjectOfType<StarVaders.BootupController>();
            if (boot != null && boot.IsFinishedLoadingGame && !_pressedFrame)
            { _pressedFrame = true; _menuReadyAt = Time.unscaledTime + 5f; boot.OnPressAnything(); return; }
            if (!_resumedFrame && Time.unscaledTime > _menuReadyAt)
            {
                var menu = UnityEngine.Object.FindObjectOfType<StarVaders.MainMenuSceneController>();
                if (menu != null) { _resumedFrame = true; menu.OnResumeGamePressed(); }
            }
            return;
        }
        if (_checkFrameAfter == 0) _checkFrameAfter = Time.unscaledTime + 7f;
        if (Time.unscaledTime < _checkFrameAfter) return;
        _checkedFrame = true;
        Plugin.Instance.Log.LogInfo($"RACK TREE screen={Screen.width}x{Screen.height}");
        var rackRoot = _view?.GridView?.GetComponentInParent<Canvas>()?.rootCanvas.transform.Find("Rack");
        DumpRack(rackRoot, 0);
        try { ExportSprite(rackRoot?.Find("RackImage")?.GetComponent<UnityEngine.UI.Image>()?.sprite, System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "rack-sprite.png")); }
        catch (Exception error) { Plugin.Instance.Log.LogError("SPRITE EXPORT FAIL: " + error); }
        bool savedFrame = _centerHud.Value;
        float savedSize = _hudScale.Value;
        try
        {
            foreach (float size in new[] {1f, 1.23f, 1.35f})
            {
                Restore(); _hudScale.Value=size; _centerHud.Value=false; ApplySizing();
                var full = Snapshot();
                Restore(); _centerHud.Value=true; ApplySizing();
                if (_frameWrappers.Count < 2) throw new Exception("HUD frame roots missing.");
                if (_board != null && full.TryGetValue(_board,out var board) &&
                    ((board.Scale-_board.localScale).sqrMagnitude>.0001f || (board.Position-_board.localPosition).sqrMagnitude>.0001f))
                    throw new Exception("HUD frame changed board transform.");
                CheckSwayRegression();
                var centered=Snapshot();
                for(int repeat=0;repeat<10;repeat++) ApplySizing();
                Compare(centered,"Repeated sizing drift");
                Restore(); _centerHud.Value=false; ApplySizing();
                Compare(full,"Full-width restoration");
                Plugin.Instance.Log.LogInfo($"HUD FRAME CHECK PASS: {size:P0}; native group anchors; board unchanged; repeated sizing stable; full width restored.");
            }
            Plugin.Instance.Log.LogInfo("HUD FRAME TEST PASS");
        }
        catch(Exception error) { Plugin.Instance.Log.LogError("HUD FRAME TEST FAIL: "+error); }
        finally { Restore(); _hudScale.Value=savedSize; _centerHud.Value=savedFrame; ApplySizing(); }
    }
    Dictionary<Transform,(Vector3 Scale,Vector3 Position)> Snapshot()
    {
        var state=new Dictionary<Transform,(Vector3,Vector3)>();
        foreach(var target in _targets) if(target!=null && target.gameObject.activeInHierarchy) state[target]=(target.localScale,target.localPosition);
        return state;
    }
    void Compare(Dictionary<Transform,(Vector3 Scale,Vector3 Position)> expected,string label)
    {
        foreach(var entry in expected)
            if ((entry.Key.localScale-entry.Value.Scale).sqrMagnitude>.0001f || (entry.Key.localPosition-entry.Value.Position).sqrMagnitude>.0001f)
                throw new Exception(label+": "+entry.Key.name+" expected "+entry.Value.Position+" actual "+entry.Key.localPosition);
    }
    // The game animates its side panels (X included). Centering must never
    // write those panels, only their wrappers, so animation can't capture or
    // accumulate a centering offset, and full width must be an exact zero.
    void CheckSwayRegression()
    {
        var wrappers=new List<RectTransform>(_frameWrappers.Values);
        var expected=new Dictionary<int,Vector2>();
        foreach(var wrapper in wrappers) expected[wrapper.GetInstanceID()]=wrapper.anchoredPosition;
        foreach(var wrapper in wrappers)
        {
            var panel=wrapper.GetChild(0);
            var native=panel.localPosition;
            try
            {
                for(int repeat=1;repeat<=20;repeat++)
                {
                    var animated=native+new Vector3(.5f*repeat,.125f*repeat,.01f*repeat);
                    panel.localPosition=animated;
                    ApplySizing();
                    if((panel.localPosition-animated).sqrMagnitude>.0001f) throw new Exception("Centering wrote an animated panel: "+panel.name+" expected "+animated+" actual "+panel.localPosition);
                    if((wrapper.anchoredPosition-expected[wrapper.GetInstanceID()]).sqrMagnitude>.0001f) throw new Exception("Centering offset drifted during animation: "+panel.name);
                }
            }
            finally { panel.localPosition=native; }
        }
        _centerHud.Value=false; ApplySizing();
        foreach(var wrapper in wrappers)
        {
            var parent=wrapper.parent.TryCast<RectTransform>();
            if(wrapper.localPosition.sqrMagnitude>.0001f || (wrapper.rect.size-parent.rect.size).sqrMagnitude>.0001f) throw new Exception("Full width wrapper is not an exact match for its parent: "+wrapper.name);
        }
        _centerHud.Value=true; ApplySizing();
        Plugin.Instance.Log.LogInfo("HUD SWAY REGRESSION PASS: animated panel X/Y/Z untouched; centering offset stable; full-width wrappers exactly match their parents.");
    }
    // Rack layout, sprite slicing and on-screen bounds for each visible part.
    void DumpRack(Transform t, int depth, int maxDepth = 3)
    {
        if (t == null || depth > maxDepth) return;
        var r = t.TryCast<RectTransform>();
        string info = "";
        if (r != null && ScreenExtent(r, out var screen))
            info = $" screen=({screen.xMin:0},{screen.yMin:0})..({screen.xMax:0},{screen.yMax:0})";
        var image = t.GetComponent<UnityEngine.UI.Image>();
        if (image != null)
            info += $" image sprite={image.sprite?.name} type={image.type} border={image.sprite?.border} ppuMul={image.pixelsPerUnitMultiplier} preserve={image.preserveAspect} color={image.color} enabled={image.enabled}";
        Plugin.Instance.Log.LogInfo($"RACK TREE {new string(' ', depth * 2)}{t.name} active={t.gameObject.activeSelf} pos={t.localPosition} scale={t.localScale} size={r?.sizeDelta} rect={r?.rect.size} anchors={r?.anchorMin}/{r?.anchorMax} pivot={r?.pivot}{info}");
        for (int i = 0; i < t.childCount; i++) DumpRack(t.GetChild(i), depth + 1, maxDepth);
    }
    // Save a sprite's pixels (textures are not CPU-readable, so copy via the GPU).
    static void ExportSprite(Sprite sprite, string path)
    {
        if (sprite == null) return;
        var source = sprite.texture;
        var rect = sprite.textureRect;
        var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, target);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var copy = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(rect, 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        var png = ImageConversion.EncodeToPNG(copy);
        var bytes = new byte[png.Length];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = png[i];
        System.IO.File.WriteAllBytes(path, bytes);
        UnityEngine.Object.Destroy(copy);
        Plugin.Instance.Log.LogInfo($"SPRITE EXPORT {sprite.name}: texture={source.width}x{source.height} rect={sprite.rect} textureRect={rect} pivot={sprite.pivot} ppu={sprite.pixelsPerUnit} -> {path}");
    }
    // The pause screen is created on demand; dump it once it has faded in.
    float _pauseSeenAt = -1f, _nextPauseCheck;
    bool _pauseDumped;
    void WatchPauseLayout()
    {
        if (_pauseDumped || Time.unscaledTime < _nextPauseCheck) return;
        _nextPauseCheck = Time.unscaledTime + .25f;
        foreach (var pause in UnityEngine.Object.FindObjectsOfType<StarVaders.PauseMenuController>())
        {
            var group = pause._canvasGroup;
            if (group == null || group.alpha < .9f) continue;
            if (_pauseSeenAt < 0f) { _pauseSeenAt = Time.unscaledTime; return; }
            if (Time.unscaledTime < _pauseSeenAt + 1f) return;
            _pauseDumped = true;
            DumpPauseLayout(pause);
            return;
        }
    }

    // Also reachable from F9 in normal play while the pause menu is open.
    internal void DumpPauseLayout(StarVaders.PauseMenuController pause)
    {
        var canvas = pause.MainPanel?.GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas != null) foreach (var label in canvas.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
        {
            string text = (label.text ?? "").ToUpperInvariant();
            if (!text.Contains("GUIDE") && !text.Contains("ABANDON") && !text.Contains("OPTIONS")) continue;
            string path = label.name;
            for (var p = label.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
            Plugin.Instance.Log.LogInfo($"PAUSE LABEL '{label.text}' path={path}");
        }
        Plugin.Instance.Log.LogInfo($"PAUSE TREE root={pause.name} canvas={canvas?.name} scaleFactor={canvas?.scaleFactor} sorting={canvas?.sortingOrder}");
        DumpRack(canvas != null ? canvas.transform : pause.transform, 0, 6);
    }
}

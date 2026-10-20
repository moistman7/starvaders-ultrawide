using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace StarVadersUltrawide
{
    internal sealed class MenuFonts
    {
        readonly Dictionary<string, Font> _copies = new();


        static string Key(string name) => name.ToLowerInvariant().Replace(" sdf", "").Replace("-regular", "").Replace(" updated", "").Replace(" ", "");

        internal void CaptureAvailable()
        {
            if (Find("StarvadersGun") != null) return;
            foreach (var font in Resources.FindObjectsOfTypeAll<Font>())
                if (font != null && !font.name.StartsWith("Ultrawide-") && font.name.ToLowerInvariant().Contains("starvaders")) CopySource(font);
        }

        internal Font Find(string family)
        {
            string key = Key(family);
            foreach (var pair in _copies)
                if (pair.Value != null && pair.Key.Contains(key)) return pair.Value;
            return null;
        }

        internal Font Copy(TextMeshProUGUI label)
        {
            if (label == null || label.font == null) return null;
            var source = label.font.sourceFontFile;
            if (source != null) return CopySource(source);
            // Some baked Settings TMP assets strip sourceFontFile in the build.
            // Keep sources while they are loaded and resolve the same named family.
            CaptureAvailable();
            string target = Key(label.font.name);
            foreach (var pair in _copies)
                if (pair.Value != null && (target.Contains(pair.Key) || pair.Key.Contains(target))) return pair.Value;
            return null;
        }

        internal Font CopyPause(TextMeshProUGUI label)
        {
            if (label == null || !label.isActiveAndEnabled || label.font == null || label.font.sourceFontFile == null) return null;
            var copy = CopySource(label.font.sourceFontFile, "pause-");
            // Do not reuse a scene/default font material on the pause-only copy.
            // A fresh UI material removes inherited clipping/tint/shader state.
            var shader = Shader.Find("UI/Default");
            if (shader != null && copy.material != null && !copy.material.name.StartsWith("UltrawidePause-"))
            {
                var material = new Material(shader);
                material.name = "UltrawidePause-" + copy.name;
                material.hideFlags = HideFlags.HideAndDontSave;
                material.mainTexture = copy.material.mainTexture;
                copy.material = material;
                Plugin.Instance.Log.LogInfo("Pause font now uses an owned UI/Default material.");
            }
            return copy.material != null && copy.material.mainTexture != null ? copy : null;
        }
        Font CopySource(Font source, string context = "")
        {
            string key = context + Key(source.name);
            if (_copies.TryGetValue(key, out var cached) && cached != null) return cached;
            // Own the Font object rather than borrowing the source object that
            // disappeared when the game unloaded a previous menu scene.
            var owned = UnityEngine.Object.Instantiate(source);
            owned.name = "Ultrawide-" + source.name;
            owned.hideFlags = HideFlags.HideAndDontSave;
            if (owned.dynamic) owned.RequestCharactersInTexture("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789%:/.- ", 42, FontStyle.Normal);
            if (owned.material != null)
            {
                owned.material.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                if (owned.material.mainTexture != null) owned.material.mainTexture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }
            _copies[key] = owned;
            Plugin.Instance.Log.LogInfo($"Copied native menu font: {context}{source.name}; dynamic={owned.dynamic}; atlas={(owned.material != null && owned.material.mainTexture != null ? owned.material.mainTexture.width + "x" + owned.material.mainTexture.height : "missing")}");
            return owned;
        }

        internal void Clear()
        {
            foreach (var font in _copies.Values) if (font != null) UnityEngine.Object.Destroy(font);
            _copies.Clear();

        }
    }
}

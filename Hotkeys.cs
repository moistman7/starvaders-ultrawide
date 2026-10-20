using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace StarVadersUltrawide
{
    internal static class Hotkeys
    {
        static readonly HashSet<Key> ValidKeys = new(System.Enum.GetValues<Key>());
        internal static ConfigEntry<Key> Decrease, Increase, ToggleFix, Diagnostics, Menu;

        internal static void Bind(ConfigFile config)
        {
            const string help = "Input System key name. Use None to disable. Edit with the game closed; restart to apply. Avoid assigning the same key to multiple actions.";
            Decrease = config.Bind("Hotkeys", "DecreaseSize", Key.F6, "Decrease HUD and board size by 2 percentage points. " + help);
            Increase = config.Bind("Hotkeys", "IncreaseSize", Key.F7, "Increase HUD and board size by 2 percentage points. " + help);
            ToggleFix = config.Bind("Hotkeys", "ToggleFix", Key.F8, "Toggle ultrawide scaling and enlargement. " + help);
            Diagnostics = config.Bind("Hotkeys", "Diagnostics", Key.F9, "Write canvas/camera diagnostics to the log. " + help);
            Menu = config.Bind("Hotkeys", "Menu", Key.F10, "Open or close the mod menu. " + help);
        }

        internal static bool Pressed(ConfigEntry<Key> binding)
        {
            var keyboard = Keyboard.current;
            return binding != null && keyboard != null && binding.Value != Key.None &&
                ValidKeys.Contains(binding.Value) && keyboard[binding.Value].wasPressedThisFrame;
        }
    }
}

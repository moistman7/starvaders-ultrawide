using HarmonyLib;
using StarVaders;


namespace StarVadersUltrawide
{
    [HarmonyPatch(typeof(SettingsSceneController), nameof(SettingsSceneController.OnEnable))]
    internal static class SettingsMenuLifecycle
    {
        static void Postfix(SettingsSceneController __instance) => ModMenu.SettingsOpened(__instance);
    }

    [HarmonyPatch(typeof(SettingsSceneController), nameof(SettingsSceneController.SaveAndExit))]
    internal static class SettingsMenuExitLifecycle
    {
        static void Prefix(SettingsSceneController __instance) => ModMenu.SettingsClosing(__instance);
    }

    [HarmonyPatch(typeof(SettingsSceneController), nameof(SettingsSceneController.OnDisable))]
    internal static class SettingsMenuDisableLifecycle
    {
        static void Prefix(SettingsSceneController __instance) => ModMenu.SettingsClosing(__instance);
    }

    [HarmonyPatch(typeof(PauseMenuController), nameof(PauseMenuController.Awake))]
    internal static class PauseMenuLifecycle
    {
        static void Postfix(PauseMenuController __instance) => ModMenu.PauseCreated(__instance);
    }

}

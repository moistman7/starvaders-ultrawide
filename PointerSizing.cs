using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using StarVaders;

namespace StarVadersUltrawide
{
    [HarmonyPatch]
    internal static class PointerSizing
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GenericMouseInputController), nameof(GenericMouseInputController.RayCastPointerHover));
            yield return AccessTools.Method(typeof(GenericMouseInputController), nameof(GenericMouseInputController.OnMouseClick));
            yield return AccessTools.Method(typeof(GenericMouseInputController), nameof(GenericMouseInputController.OnMouseClickReleased));
            yield return AccessTools.Method(typeof(GenericMouseInputController), nameof(GenericMouseInputController.OnMouseCancel));
        }

        static void Prefix()
        {
            long started = Profile.Start();
            var sizing = HudSizing.Active;
            if (sizing != null) sizing.ApplySizing();
            Profile.Stop(started, Profile.Hook.Pointer);
        }
    }
}

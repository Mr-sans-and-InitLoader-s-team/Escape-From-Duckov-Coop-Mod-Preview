using HarmonyLib;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(AICharacterController))]
internal static class AIThreatPatch
{
    [HarmonyPostfix]
    [HarmonyPatch("OnHurt")]
    private static void CaptureThreat(AICharacterController __instance, DamageInfo dmgInfo)
    {
        AIThreatManager.NoteHit(__instance, dmgInfo);
    }

    [HarmonyPostfix]
    [HarmonyPatch("Update")]
    private static void RetargetIfNeeded(AICharacterController __instance)
    {
        AIThreatManager.Evaluate(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnDestroy")]
    private static void ClearThreat(AICharacterController __instance)
    {
        AIThreatManager.Clear(__instance);
    }
}

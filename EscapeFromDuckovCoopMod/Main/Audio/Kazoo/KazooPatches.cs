using Duckov;
using FMOD.Studio;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch]
internal static class KazooPatches
{
    [HarmonyPatch(typeof(AudioManager), "SetRTPC", typeof(string), typeof(float), typeof(GameObject))]
    [HarmonyPostfix]
    private static void CaptureParameter(string key, float value, GameObject gameObject)
    {
        if (gameObject == null || (key != "Kazoo/Pitch" && key != "Kazoo/Intensity")) return;
        var agent = gameObject.GetComponent<ItemAgent_Kazoo>();
        if (agent != null) KazooSync.CaptureParameter(agent, key, value);
    }

    [HarmonyPatch(typeof(ItemAgent_Kazoo), "Update")]
    [HarmonyPostfix]
    private static void AfterUpdate(ItemAgent_Kazoo __instance, bool ___currentMakingSound, EventInstance? ___currentEvent)
    {
        KazooSync.Observe(__instance, ___currentMakingSound && ___currentEvent.HasValue);
    }

    [HarmonyPatch(typeof(ItemAgent_Kazoo), "OnDisable")]
    [HarmonyPrefix]
    private static void OnDisable(ItemAgent_Kazoo __instance) => KazooSync.StopLocal(__instance);

    [HarmonyPatch(typeof(ItemAgent_Kazoo), "OnDestroy")]
    [HarmonyPrefix]
    private static void OnDestroy(ItemAgent_Kazoo __instance) => KazooSync.StopLocal(__instance);
}

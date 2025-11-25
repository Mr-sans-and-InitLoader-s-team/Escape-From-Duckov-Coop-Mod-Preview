using Duckov;
using FMOD.Studio;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(AudioManager))]
internal static class CoopAudioPatches
{
    [HarmonyPatch("MPost", typeof(string), typeof(GameObject))]
    [HarmonyPostfix]
    private static void PostEventInstance(EventInstance? __result, string eventName, GameObject gameObject)
    {
        if (__result == null)
            return;

        CoopAudioSync.NotifyLocalPost(eventName, gameObject, string.Empty, string.Empty);
    }

    [HarmonyPatch("MPost", typeof(string), typeof(Vector3))]
    [HarmonyPostfix]
    private static void PostAtPosition(EventInstance? __result, string eventName, Vector3 position)
    {
        if (__result == null)
            return;

        CoopAudioSync.NotifyLocalPost(eventName, position);
    }

    [HarmonyPatch("MPostCustomSFX", typeof(string), typeof(GameObject), typeof(bool))]
    [HarmonyPostfix]
    private static void PostCustomSfx(EventInstance? __result, string filePath, GameObject gameObject, bool loop)
    {
        if (__result == null)
            return;

        CoopAudioSync.NotifyLocalPost(filePath, gameObject, string.Empty, string.Empty);
    }
}

using Duckov;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(AudioManager), "UpdateListener")]
internal static class SpectatorAudioPatch
{
    private static void Postfix(AudioManager __instance)
    {
        if (NetService.Instance?.networkStarted != true || Spectator.Instance?._spectatorActive != true) return;
        var target = GameCamera.Instance ? GameCamera.Instance.target : null;
        if (target && LocalPlayerManager.Instance.IsAlive(target))
            __instance.transform.position = target.transform.position + Vector3.up * 2f;
    }
}

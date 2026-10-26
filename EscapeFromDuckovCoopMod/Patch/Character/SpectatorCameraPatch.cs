namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdatePosition))]
internal static class SpectatorCameraPatch
{
    private static void Prefix(GameCamera __instance)
    {
        if (Spectator.Instance?._spectatorActive != true || !__instance.target) return;
        // The native camera must sample the seat pose after remote interpolation.
        __instance.target.GetComponentInParent<MountedRiderLock>()?.SyncPose();
    }
}

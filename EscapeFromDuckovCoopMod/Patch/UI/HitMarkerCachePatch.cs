namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(HitMarker), "Awake")]
internal static class HitMarkerCacheAwakePatch
{
    private static void Postfix(HitMarker __instance) => LocalHitKillFx.RegisterHitMarker(__instance);
}

[HarmonyPatch(typeof(HitMarker), "OnDestroy")]
internal static class HitMarkerCacheDestroyPatch
{
    private static void Postfix(HitMarker __instance) => LocalHitKillFx.ForgetHitMarker(__instance);
}

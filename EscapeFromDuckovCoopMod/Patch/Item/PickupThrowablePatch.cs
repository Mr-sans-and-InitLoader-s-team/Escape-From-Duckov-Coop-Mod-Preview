namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Skill_Grenade), nameof(Skill_Grenade.OnRelease))]
internal static class PickupThrowablePatch
{
    private static void Prefix(Skill_Grenade __instance, out PickupThrowableSync.Capture __state) =>
        __state = PickupThrowableSync.Begin(__instance);

    private static Exception Finalizer(Exception __exception, PickupThrowableSync.Capture __state)
    {
        PickupThrowableSync.End(__state, __exception == null);
        return __exception;
    }
}

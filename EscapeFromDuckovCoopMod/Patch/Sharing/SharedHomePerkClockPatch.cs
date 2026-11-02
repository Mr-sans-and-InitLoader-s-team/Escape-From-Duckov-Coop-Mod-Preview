using Duckov.PerkTrees;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Perk), nameof(Perk.GetRemainingTime))]
internal static class SharedHomePerkClockPatch
{
    private static bool Prefix(Perk __instance, ref TimeSpan __result)
    {
        if (!SharedHomePerkClock.TryRemaining(__instance, out var remaining)) return true;
        __result = remaining;
        return false;
    }
}

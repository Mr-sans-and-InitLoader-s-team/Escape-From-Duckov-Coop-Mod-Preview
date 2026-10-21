using Duckov.UI;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(WeightBarComplex), "OnDisable")]
internal static class WeightBarLifetimePatch
{
    private static void Prefix(WeightBarComplex __instance)
    {
        if (!SharedHome.Active && !SharedHome.Reloading) return;
        // Cancel the native continuation before scene unloading destroys its bars.
        var token = AccessTools.Field(typeof(WeightBarComplex), "currentToken");
        token.SetValue(__instance, unchecked((int)token.GetValue(__instance) + 1));
    }
}

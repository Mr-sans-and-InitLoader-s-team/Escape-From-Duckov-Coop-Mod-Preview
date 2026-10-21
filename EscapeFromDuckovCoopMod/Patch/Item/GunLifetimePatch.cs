namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(ItemAgent_Gun), "Update")]
internal static class GunLifetimePatch
{
    // Replica creation and teardown can leave an agent alive without its backing item.
    private static bool Prefix(ItemAgent_Gun __instance) => __instance.Item;
}

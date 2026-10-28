using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Inventory), nameof(Inventory.GetFirstEmptyPosition))]
internal static class PlayerStorageCapacityPatch
{
    private static void Postfix(Inventory __instance, ref int __result)
    {
        if (!ReferenceEquals(__instance, PlayerStorage.Inventory) || __result < __instance.Capacity) return;
        // Older inventories can retain items and holes beyond their current capacity.
        __result = -1;
        for (var i = 0; i < __instance.Capacity; i++)
            if (!__instance.GetItemAt(i)) { __result = i; break; }
    }
}

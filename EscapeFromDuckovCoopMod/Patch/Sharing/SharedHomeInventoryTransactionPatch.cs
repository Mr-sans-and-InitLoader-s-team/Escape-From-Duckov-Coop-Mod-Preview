using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(ItemUtilities), nameof(ItemUtilities.FindAllBelongsToPlayer))]
internal static class SharedHomeInventoryCostScopePatch
{
    private static bool Prefix(Predicate<Item> predicate, ref List<Item> __result)
    {
        if (!SharedHomeInventoryTransaction.TryFindItems(predicate, out var items)) return true;
        __result = items;
        return false;
    }
}

[HarmonyPatch(typeof(ItemUtilities), nameof(ItemUtilities.SendToPlayer))]
internal static class SharedHomeInventoryDetachedItemPatch
{
    private static bool Prefix(Item item, bool dontMerge) => !SharedHomeInventoryTransaction.ReturnDetachedItem(item, dontMerge);
}

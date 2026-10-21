using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Item), nameof(Item.Combine))]
internal static class SharedHomeStorageCombinePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Item __instance, Item incomingItem)
    {
        if (!__instance || !incomingItem || __instance == incomingItem) return true;
        if (SharedHomeStorage.Hold(__instance.InInventory) && !SharedHomeStorage.IsShared(incomingItem.InInventory))
        {
            SharedHomeStorageOperations.DepositPart(incomingItem, __instance).Forget();
            return false;
        }
        if (!SharedHomeStorage.Hold(incomingItem.InInventory)) return true;
        if (!SharedHomeStorage.IsShared(__instance.InInventory))
        {
            var count = Math.Min(incomingItem.StackCount, __instance.MaxStackCount - __instance.StackCount);
            if (__instance.Stackable && __instance.TypeID == incomingItem.TypeID && count > 0)
                SharedHomeStorage.RequestTake(incomingItem, merge: __instance, count: count);
            return false;
        }
        SharedHomeStorage.RequestEdit(incomingItem, new
        {
            operation = "merge", target = __instance.InInventory.GetIndex(__instance), expected = SharedHomeStorageOperations.Key(__instance)
        });
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem))]
internal static class SharedHomeStorageAddPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Inventory __instance, Item item, ref bool __result)
    {
        if (!SharedHomeStorageOperations.Put(item, __instance)) return true;
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(ItemSetting_Gun), nameof(ItemSetting_Gun.TakeOutAllBullets))]
internal static class SharedHomeStorageUnloadPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ItemSetting_Gun __instance) =>
        !SharedHomeStorage.RequestEdit(__instance.Item, new { operation = "unload" });
}

[HarmonyPatch(typeof(CharacterMainControl), nameof(CharacterMainControl.UseItem))]
internal static class SharedHomeStorageUsePatch
{
    private static bool Prefix(CharacterMainControl __instance, Item item) => __instance != CharacterMainControl.Main ||
        !item || !SharedHomeStorage.Hold(item.InInventory) || !item.IsUsable(__instance) ||
        !SharedHomeStorage.RequestTake(item, count: 1, use: true);
}

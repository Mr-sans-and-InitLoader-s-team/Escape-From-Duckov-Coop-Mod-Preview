using Duckov.UI;
using ItemStatsSystem;
using System.Reflection;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(LootView), "OnStoreAllButtonClicked")]
internal static class SharedHomeStorageStoreAllPatch
{
    private static bool Prefix(LootView __instance)
    {
        if (!SharedHome.Guest || SharedHome.Applying || !SharedHomeStorage.IsShared(__instance.TargetInventory)) return true;
        SharedHomeStorageStoreAll.Request().Forget();
        return false;
    }
}

[HarmonyPatch]
internal static class SharedHomeStorageStoreAllInputPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(InventoryEntry), nameof(InventoryEntry.OnDrop)),
        AccessTools.Method(typeof(InventoryEntry), nameof(InventoryEntry.OnPointerClick))
    };

    private static bool Prefix() => !SharedHome.Guest || SharedHome.Applying || !SharedHomeStorageStoreAll.Pending;
}

[HarmonyPatch]
internal static class SharedHomeStorageStoreAllSendPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(ItemUtilities), nameof(ItemUtilities.SendToPlayerStorage)),
        AccessTools.Method(typeof(PlayerStorage), nameof(PlayerStorage.Push))
    };

    [HarmonyPriority(Priority.First)]
    private static bool Prefix() => !SharedHome.Guest || SharedHome.Applying || !SharedHomeStorageStoreAll.Pending;
}

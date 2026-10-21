using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(Inventory), nameof(Inventory.Sort), new Type[0])]
internal static class SharedHomeStorageSortPatch
{
    private static bool Prefix(Inventory __instance, ref bool __state)
    {
        if (!SharedHomeStorage.IsShared(__instance) || !SharedHomeStorage.CanTransfer) return true;
        if (SharedHome.Guest)
        {
            SharedHome.Request("storageSort", new { });
            return false;
        }
        __state = true;
        SharedHomeStorage.BeginSort();
        return true;
    }

    private static Exception Finalizer(Exception __exception, bool __state)
    {
        if (__state) SharedHomeStorage.EndSort();
        return __exception;
    }
}

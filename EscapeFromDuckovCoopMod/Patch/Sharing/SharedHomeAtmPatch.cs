using ItemStatsSystem;
using Duckov.Economy;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(ATMPanel), nameof(ATMPanel.Save))]
internal static class SharedHomeAtmSavePatch
{
    private static bool Prefix(long amount, ref bool __result)
    {
        if (!SharedHome.Guest) return true;
        __result = SharedHomeLoadouts.Ready && !SharedHomeAtm.Busy && amount > 0 && ItemUtilities.GetItemCount(EconomyManager.CashItemID) >= amount;
        if (__result) SharedHomeAtm.Request(amount, false).Forget();
        return false;
    }
}

[HarmonyPatch(typeof(ATMPanel), nameof(ATMPanel.Draw))]
internal static class SharedHomeAtmDrawPatch
{
    private static bool Prefix(long amount, ref UniTask<bool> __result)
    {
        if (!SharedHome.Guest) return true;
        __result = SharedHomeAtm.Request(amount, true);
        return false;
    }
}

[HarmonyPatch(typeof(ItemUtilities), nameof(ItemUtilities.FindAllBelongsToPlayer))]
internal static class SharedHomeAtmCashScopePatch
{
    private static bool Prefix(Predicate<Item> predicate, ref List<Item> __result)
    {
        if (!SharedHomeAtm.TryFindCash(predicate, out var items)) return true;
        __result = items;
        return false;
    }
}

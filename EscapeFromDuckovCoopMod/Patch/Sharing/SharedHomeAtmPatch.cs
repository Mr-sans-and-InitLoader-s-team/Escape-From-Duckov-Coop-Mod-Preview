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

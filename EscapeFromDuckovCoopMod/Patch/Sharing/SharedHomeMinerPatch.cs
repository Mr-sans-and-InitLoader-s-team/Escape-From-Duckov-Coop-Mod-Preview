using Duckov.Bitcoins;
using Duckov.UI;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(BitcoinMiner), "UpdateWork")]
internal static class SharedHomeMinerChangedPatch
{
    private static void Postfix(BitcoinMiner __instance) => SharedHomeMiner.Track(__instance);
}

[HarmonyPatch(typeof(BitcoinMiner), "Save")]
internal static class SharedHomeMinerSavePatch
{
    private static bool Prefix() => !SharedHomeSave.OverlayActive && !SharedHome.Reloading;
}

[HarmonyPatch(typeof(BitcoinMiner), "CreateCoin")]
internal static class SharedHomeMinerProductionPatch
{
    private static bool Prefix(ref UniTask __result)
    {
        if (!SharedHome.Guest) return true;
        __result = UniTask.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(SlotDisplay), "get_Editable")]
internal static class SharedHomeMinerSlotEditPatch
{
    private static void Postfix(SlotDisplay __instance, ref bool __result)
    {
        if (SharedHome.Guest && BitcoinMiner.Instance && __instance.Target?.Master == BitcoinMiner.Instance.Item) __result = false;
    }
}

[HarmonyPatch(typeof(InventoryDisplay), "get_Editable")]
internal static class SharedHomeMinerInventoryEditPatch
{
    private static void Postfix(InventoryDisplay __instance, ref bool __result)
    {
        if (SharedHome.Guest && BitcoinMiner.Instance && __instance.Target == BitcoinMiner.Instance.Item?.Inventory) __result = false;
    }
}

[HarmonyPatch]
internal static class SharedHomeMinerTransferPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (var name in new[] { "OnMinerSlotEntryDoubleClicked", "OnMinerInventoryEntryDoubleClicked", "OnPlayerItemsDoubleClicked" })
            yield return AccessTools.Method(typeof(BitcoinMinerView), name);
    }
    private static bool Prefix() => !SharedHome.Guest;
}

using HarmonyLib;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

[HarmonyPatch(typeof(InteractableLootbox), "OnUpdate")]
internal static class LootSearchInitializationPatch
{
    private static bool Prefix(InteractableLootbox __instance, InteractableLootbox.LootBoxStates ___lootState)
    {
        if (___lootState != InteractableLootbox.LootBoxStates.looting) return true;
        return COOPManager.LootNet?.Client_WaitingForLootState(__instance.Inventory) != true;
    }
}

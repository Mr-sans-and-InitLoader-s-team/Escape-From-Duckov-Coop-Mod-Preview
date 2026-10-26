namespace EscapeFromDuckovCoopMod.Patch.Loot;

[HarmonyPatch(typeof(InteractableLootbox), "IsInteractable")]
internal static class PersonalStarterLootPatch
{
    private static bool Prefix(InteractableLootbox __instance, ref bool __result)
    {
        if (!LootboxLifecycleGuard.IsLevelInventoryReady() || PersonalStarterLoot.IsReady(__instance.Inventory)) return true;
        __result = false;
        return false;
    }
}

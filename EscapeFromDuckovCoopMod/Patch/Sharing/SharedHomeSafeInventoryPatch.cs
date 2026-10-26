namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(PetProxy), "Start")]
internal static class SharedHomeSafeOwnerPatch
{
    private static void Prefix(PetProxy __instance) => SharedHomeSafeInventory.Track(__instance);
}

[HarmonyPatch(typeof(PetProxy), "OnCollectSaveData")]
internal static class SharedHomeSafeSavePatch
{
    // The outgoing shared scene can still collect saves while returning to the menu.
    private static bool Prefix(PetProxy __instance) => SharedHomeSafeInventory.CanSave(__instance);
}

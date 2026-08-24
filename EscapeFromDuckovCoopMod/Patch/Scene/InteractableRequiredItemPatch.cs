namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(InteractableBase), "UseRequiredItem")]
internal static class InteractableRequiredItemUsePatch
{
    private static void Postfix(InteractableBase __instance, bool __result)
    {
        if (__result)
            LevelDataBoolNet.OnInteractableRequiredItemUsed(__instance);
    }
}

[HarmonyPatch(typeof(InteractableBase), nameof(InteractableBase.UpdateInteract))]
internal static class InteractableRequiredItemTimeoutPatch
{
    private static void Prefix(InteractableBase __instance, out bool __state)
    {
        __state = LevelDataBoolNet.IsInteractableRequiredItemSatisfied(__instance);
    }

    private static void Postfix(InteractableBase __instance, bool __state)
    {
        if (!__state && LevelDataBoolNet.IsInteractableRequiredItemSatisfied(__instance))
            LevelDataBoolNet.OnInteractableRequiredItemUsed(__instance);
    }
}

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(InteractableBase), nameof(InteractableBase.StartInteract))]
internal static class AISpawnerTriggerStartInteractContextPatch
{
    private static void Prefix(InteractableBase __instance, CharacterMainControl _interactCharacter, out bool __state)
    {
        __state = AISpawnerTriggerNet.BeginClientInteractContext(__instance, _interactCharacter);
    }

    private static Exception Finalizer(bool __state, Exception __exception)
    {
        if (__state)
            AISpawnerTriggerNet.EndClientInteractContext();

        return __exception;
    }
}

[HarmonyPatch(typeof(InteractableBase), nameof(InteractableBase.UpdateInteract))]
internal static class AISpawnerTriggerUpdateInteractContextPatch
{
    private static bool Prefix(InteractableBase __instance, CharacterMainControl _interactCharacter, float deltaTime, out bool __state)
    {
        __state = AISpawnerTriggerNet.BeginClientInteractContext(__instance, _interactCharacter);

        if (InteractableEventNet.Client_TryRequestHostTimeout(__instance, _interactCharacter, deltaTime))
        {
            if (__state)
            {
                AISpawnerTriggerNet.EndClientInteractContext();
                __state = false;
            }

            return false;
        }

        return true;
    }

    private static Exception Finalizer(bool __state, Exception __exception)
    {
        if (__state)
            AISpawnerTriggerNet.EndClientInteractContext();

        return __exception;
    }
}

[HarmonyPatch(typeof(InteractableBase), nameof(InteractableBase.FinishInteract))]
internal static class AISpawnerTriggerFinishInteractContextPatch
{
    private static bool Prefix(InteractableBase __instance, CharacterMainControl _interactCharacter, out bool __state)
    {
        __state = AISpawnerTriggerNet.BeginClientInteractContext(__instance, _interactCharacter);

        if (InteractableEventNet.Client_TryRequestHostFinish(__instance, _interactCharacter))
        {
            if (__state)
            {
                AISpawnerTriggerNet.EndClientInteractContext();
                __state = false;
            }

            return false;
        }

        return true;
    }

    private static Exception Finalizer(InteractableBase __instance, bool __state, Exception __exception)
    {
        if (__state)
        {
            AISpawnerTriggerNet.Client_RequestInteractableEventSpawners(__instance);
            AISpawnerTriggerNet.EndClientInteractContext();
        }

        return __exception;
    }
}

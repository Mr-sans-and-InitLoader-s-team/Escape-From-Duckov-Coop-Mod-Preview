using HarmonyLib;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch]
internal static class VehicleLocalControlPatch
{
    private static System.Reflection.MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("InteractableVehicle");
        return type == null ? null : AccessTools.Method(type, "OnInteractFinished");
    }

    [HarmonyPostfix]
    private static void Postfix(object __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || mod.IsServer)
            return;

        var level = LevelManager.Instance;
        var main = CharacterMainControl.Main;
        if (level == null || main == null)
            return;

        var targetCharacter = Traverse.Create(__instance).Field<CharacterMainControl>("targetCharacter").Value;
        if (targetCharacter == null || !targetCharacter.IsMainCharacter)
            return;

        var special = Traverse.Create(__instance).Field<AISpecialAttachmentBase>("specialAttachment").Value;
        var mounted = special != null && special.aiCharacterController != null
            ? special.aiCharacterController.CharacterMainControl
            : null;
        if (mounted == null)
            return;

        if (level.ControllingCharacter == mounted)
            return;

        try
        {
            targetCharacter.CurrentAction?.StopAction();
        }
        catch
        {
        }

        // Force the same local handoff flow as vanilla vehicle interaction,
        // so movement direction, animation state, and hoof SFX are driven by the mount controller.
        targetCharacter.ControlOtherCharacter(mounted, -1f);
    }
}


namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(SetInLevelDataBoolProxy), nameof(SetInLevelDataBoolProxy.SetToTarget))]
internal static class Patch_SetInLevelDataBoolProxy_SetToTarget
{
    private static bool Prefix(SetInLevelDataBoolProxy __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (mod.IsServer) return true;

        LevelDataBoolNet.OnLocalSet(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(SetInLevelDataBoolProxy), nameof(SetInLevelDataBoolProxy.SetTo), typeof(bool))]
internal static class Patch_SetInLevelDataBoolProxy_SetTo
{
    private static bool Prefix(SetInLevelDataBoolProxy __instance, bool target)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;
        if (LevelDataBoolNet.IsApplyingRemoteProxy) return true;
        if (mod.IsServer) return true;

        LevelDataBoolNet.OnLocalSet(__instance, target);
        return false;
    }

    private static void Postfix(SetInLevelDataBoolProxy __instance, bool target)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted || !mod.IsServer) return;
        if (LevelDataBoolNet.IsApplyingRemoteProxy) return;

        LevelDataBoolNet.OnLocalSet(__instance, target);
    }
}

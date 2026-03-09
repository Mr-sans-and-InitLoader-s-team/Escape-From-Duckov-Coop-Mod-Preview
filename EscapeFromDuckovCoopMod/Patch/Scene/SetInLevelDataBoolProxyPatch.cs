namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(SetInLevelDataBoolProxy), nameof(SetInLevelDataBoolProxy.SetToTarget))]
internal static class Patch_SetInLevelDataBoolProxy_SetToTarget
{
    private static bool Prefix(SetInLevelDataBoolProxy __instance)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        LevelDataBoolNet.OnLocalSet(__instance);
        return false;
    }
}

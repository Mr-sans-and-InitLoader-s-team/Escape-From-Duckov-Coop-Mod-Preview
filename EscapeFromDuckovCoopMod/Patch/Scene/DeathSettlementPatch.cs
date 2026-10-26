using System.Reflection;

namespace EscapeFromDuckovCoopMod;

//观战
[HarmonyPatch]
internal static class Patch_ClosureView_ShowAndReturnTask_SpectatorGate
{
    private static MethodBase TargetMethod()
    {
        var t = AccessTools.TypeByName("Duckov.UI.ClosureView");
        if (t == null) return null;
        return AccessTools.Method(t, "ShowAndReturnTask", new[] { typeof(DamageInfo), typeof(float) });
    }

    private static bool Prefix(ref UniTask __result, DamageInfo dmgInfo, float duration)
    {
        var mod = ModBehaviourF.Instance;
        if (mod == null || !mod.networkStarted) return true;

        if (Spectator.Instance._skipSpectatorForNextClosure)
        {
            Spectator.Instance._skipSpectatorForNextClosure = false;
            __result = UniTask.CompletedTask;
            return true;
        }

        __result = Spectator.Instance.BeginDeathClosure(dmgInfo, duration);
        return false;
    }
}

[HarmonyPatch(typeof(Duckov.UI.ClosureView), nameof(Duckov.UI.ClosureView.ShowAndReturnTask), typeof(float))]
internal static class SpectatorTravelClosurePatch
{
    private static bool Prefix(ref UniTask __result)
    {
        if (!SpectatorTravel.Travelling) return true;
        __result = UniTask.CompletedTask;
        return false;
    }
}

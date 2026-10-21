using System.Reflection;
using Duckov.Endowment;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch]
internal static class SharedHomeUnlockPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => SharedHomeUnlocks.Methods.Values.Where(m => m.ReturnType == typeof(void));
    private static bool Prefix(MethodBase __originalMethod, object[] __args) => SharedHomeUnlocks.Forward(__originalMethod, __args);
    private static void Postfix() => SharedHome.MarkDirty();
}

[HarmonyPatch(typeof(EndowmentManager), nameof(EndowmentManager.UnlockEndowment))]
internal static class SharedHomeEndowmentUnlockPatch
{
    private static bool Prefix(MethodBase __originalMethod, object[] __args, ref bool __result)
    {
        if (SharedHomeUnlocks.Forward(__originalMethod, __args)) return true;
        __result = true;
        return false;
    }
    private static void Postfix() => SharedHome.MarkDirty();
}

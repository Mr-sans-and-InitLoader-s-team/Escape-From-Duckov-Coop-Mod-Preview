using System;
using Duckov.Economy;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(CostTaker), "OnInteractFinished")]
internal static class CostTakerPaidFinishPatch
{
    private static void Prefix(CostTaker __instance)
    {
        LevelDataBoolNet.BeginCostTakerPayment(__instance);
    }

    private static Exception Finalizer(CostTaker __instance, Exception __exception)
    {
        LevelDataBoolNet.EndCostTakerPayment(__instance);
        return __exception;
    }
}

[HarmonyPatch(typeof(Cost), nameof(Cost.Pay))]
internal static class CostTakerCostPayPatch
{
    private static void Postfix(bool __result)
    {
        if (__result)
            LevelDataBoolNet.RecordCostTakerPayment();
    }
}

[HarmonyPatch(typeof(CostTaker), "OnEnable")]
internal static class CostTakerPaidEnablePatch
{
    private static void Postfix(CostTaker __instance)
    {
        LotteryBoxNet.RememberPaymentGateState(__instance);
        LevelDataBoolNet.TryApplyCachedCostTakerPaid(__instance);
    }
}

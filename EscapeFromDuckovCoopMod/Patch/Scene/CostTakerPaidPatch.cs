using System;
using Duckov.Economy;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(CostTaker), "OnInteractFinished")]
internal static class CostTakerPaidFinishPatch
{
    private static bool Prefix(CostTaker __instance)
    {
        if (SharedHomeCostTakers.TryRequest(__instance)) return false;
        LevelDataBoolNet.BeginCostTakerPayment(__instance);
        return true;
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

[HarmonyPatch(typeof(CostTaker), "OnDisable")]
internal static class SharedCostTakerPendingDisablePatch
{
    private static void Postfix(CostTaker __instance) => SharedHomeCostTakers.Release(__instance);
}

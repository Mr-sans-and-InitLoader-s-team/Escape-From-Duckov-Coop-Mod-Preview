using Duckov;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromDuckovCoopMod.Patch.Loot;

[HarmonyPatch(typeof(LotteryBox), "Begin")]
internal static class LotteryBoxBeginSyncPatch
{
    private static void Prefix(LotteryBox __instance, ref bool __state)
    {
        __state = LotteryBoxNet.TryPrepareLocalBegin(__instance);
    }

    private static void Postfix(LotteryBox __instance, bool __state)
    {
        if (__state)
            LotteryBoxNet.Local_BroadcastBegin(__instance);
    }
}

[HarmonyPatch(typeof(LotteryBox), "Display")]
internal static class LotteryBoxDisplaySyncPatch
{
    private static void Postfix(LotteryBox __instance, int id)
    {
        LotteryBoxNet.Local_TryBroadcastDisplay(__instance, id);
    }
}

[HarmonyPatch(typeof(LotteryBox), "SetColor")]
internal static class LotteryBoxEndSyncPatch
{
    private static void Postfix(LotteryBox __instance, Color color)
    {
        LotteryBoxNet.Local_TryBroadcastColor(__instance, color);
    }
}

using System;
using System.Reflection;
using HarmonyLib;
using ItemStatsSystem;

namespace EscapeFromDuckovCoopMod;

[HarmonyPatch(typeof(ItemAgentUtilities), nameof(ItemAgentUtilities.ReleaseActiveAgent))]
internal static class RemoteAIAgentLifetimePatch
{
    private static readonly MethodInfo UnplugHandler = AccessTools.Method(typeof(ItemAgent), "OnUnplugedFromSlot");

    private static void Prefix(ItemAgentUtilities __instance)
    {
        var agent = __instance.ActiveAgent;
        if (!agent || __instance.Master == null || UnplugHandler == null ||
            !agent.GetComponentInParent<RemoteAIReplicaTag>(true)) return;

        // Destroy runs at frame end; the released agent must stop observing its item immediately.
        __instance.Master.onUnpluggedFromSlot -= (Action<Item>)UnplugHandler.CreateDelegate(typeof(Action<Item>), agent);
    }
}

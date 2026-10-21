using System.Collections;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CoopRuntimeTests;

internal static class RuntimeSnapshotBacklogProbe
{
    private static bool _installed;
    private static float _until;
    private static bool QueueLength(ref int __result)
    {
        if (Time.unscaledTime >= _until) return true;
        __result = 32;
        return false;
    }
    internal static object Run(Assembly mod, JObject command)
    {
        if (!_installed)
        {
            new Harmony("local.coop.snapshot-backlog").Patch(AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.LiteNetTransport"), "GetReliableQueueLength"),
                prefix: new HarmonyMethod(typeof(RuntimeSnapshotBacklogProbe), nameof(QueueLength)));
            _installed = true;
        }
        if (command["holdSeconds"] != null) _until = Time.unscaledTime + (float)command["holdSeconds"];
        var manager = AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.COOPManager"), "AI").GetValue(null);
        var queues = (IDictionary)AccessTools.Field(manager.GetType(), "_serverSnapshotChunks").GetValue(manager);
        return new { holding = Time.unscaledTime < _until, peers = queues.Count, chunks = queues.Values.Cast<IEnumerable>().Sum(q => q.Cast<object>().Count()),
            ids = queues.Values.Cast<IEnumerable>().SelectMany(q => q.Cast<object>())
                .SelectMany(c => c.GetType().GetField("Item1")?.GetValue(c) as int[] ?? Array.Empty<int>()).ToArray() };
    }
}

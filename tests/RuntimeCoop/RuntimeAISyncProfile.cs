using System.Collections;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace CoopRuntimeTests;

internal sealed class RuntimeAISyncProfile : MonoBehaviour
{
    private static object _manager;
    private static bool _installed, _active;
    private static readonly List<float> Frames = new();
    private static readonly Dictionary<string, (int Calls, long Ticks, long Max)> Methods = new();
    private static readonly HashSet<int> Seen = new();
    private static Dictionary<int, int> LastInstances = new();
    private static Dictionary<int, int> CurrentInstances = new();
    private static int _created, _removed, _replaced, _maxSpawning, _maxSpawnQueue, _maxStateQueue, _maxReplicas;
    private static long _allocated;
    private static int _collections;

    private static object Read(object target, string field) => AccessTools.Field(target.GetType(), field).GetValue(target);
    private static int Count(string field) => (int)AccessTools.Property(Read(_manager, field).GetType(), "Count").GetValue(Read(_manager, field));

    internal static object Run(Assembly mod, JObject command)
    {
        _manager = AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.COOPManager"), "AI").GetValue(null);
        if ((int?)command["frameCap"] is int cap) Application.targetFrameRate = cap;
        if ((bool?)command["begin"] == true)
        {
            if (!_installed)
            {
                var harmony = new Harmony("local.coop.ai-profile");
                foreach (var name in new[] { "Client_Update", "Client_ProcessPendingSnapshots", "Client_ProcessPendingStateUpdates",
                    "Client_ProcessReplicaSpawnQueue", "Client_HandleSpawn", "Client_HandleSnapshotChunk", "Client_SendSnapshotRequest" })
                    harmony.Patch(AccessTools.Method(_manager.GetType(), name),
                        prefix: new HarmonyMethod(typeof(RuntimeAISyncProfile), nameof(Enter)),
                        postfix: new HarmonyMethod(typeof(RuntimeAISyncProfile), nameof(Leave)));
                var tracker = new GameObject("AISyncProfile");
                DontDestroyOnLoad(tracker);
                tracker.AddComponent<RuntimeAISyncProfile>();
                _installed = true;
            }
            _active = false;
            Frames.Clear(); Methods.Clear(); Seen.Clear(); LastInstances.Clear(); CurrentInstances.Clear();
            _created = _removed = _replaced = _maxSpawning = _maxSpawnQueue = _maxStateQueue = _maxReplicas = 0;
            _allocated = GC.GetAllocatedBytesForCurrentThread(); _collections = GC.CollectionCount(0);
            _active = true;
        }
        if ((bool?)command["end"] == true) _active = false;
        var frames = Frames.OrderBy(v => v).ToArray();
        float Percentile(float p) => frames.Length == 0 ? 0 : frames[Math.Min(frames.Length - 1, (int)(frames.Length * p))];
        return new
        {
            frames = frames.Length, medianMs = Percentile(.5f), p95Ms = Percentile(.95f), p99Ms = Percentile(.99f),
            maxMs = frames.LastOrDefault(), created = _created, removed = _removed, replaced = _replaced, unique = Seen.Count,
            maxSpawning = _maxSpawning, maxSpawnQueue = _maxSpawnQueue, maxStateQueue = _maxStateQueue, maxReplicas = _maxReplicas,
            stateQueue = Count("_pendingStateUpdates"), spawnQueue = Count("_clientReplicaSpawnQueue"), spawning = Count("_clientReplicaSpawning"),
            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - _allocated, collections = GC.CollectionCount(0) - _collections,
            methods = Methods.Select(p => new { method = p.Key, calls = p.Value.Calls,
                totalMs = p.Value.Ticks * 1000d / Stopwatch.Frequency, maxMs = p.Value.Max * 1000d / Stopwatch.Frequency }).ToArray()
        };
    }

    private static void Enter(out long __state) => __state = _active ? Stopwatch.GetTimestamp() : 0;
    private static void Leave(MethodBase __originalMethod, long __state)
    {
        if (!_active || __state == 0) return;
        var ticks = Stopwatch.GetTimestamp() - __state;
        Methods.TryGetValue(__originalMethod.Name, out var old);
        Methods[__originalMethod.Name] = (old.Calls + 1, old.Ticks + ticks, Math.Max(old.Max, ticks));
    }

    private void LateUpdate()
    {
        if (!_active) return;
        Frames.Add(Time.unscaledDeltaTime * 1000f);
        _maxSpawning = Math.Max(_maxSpawning, Count("_clientReplicaSpawning"));
        _maxSpawnQueue = Math.Max(_maxSpawnQueue, Count("_clientReplicaSpawnQueue"));
        _maxStateQueue = Math.Max(_maxStateQueue, Count("_pendingStateUpdates"));
        var current = CurrentInstances;
        current.Clear();
        foreach (DictionaryEntry row in (IDictionary)Read(_manager, "_clientReplicas"))
        {
            var instance = (GameObject)Read(row.Value, "Instance");
            if (!instance) continue;
            var id = (int)row.Key; current[id] = instance.GetInstanceID(); Seen.Add(id);
            if (!LastInstances.TryGetValue(id, out var before)) _created++;
            else if (before != current[id]) _replaced++;
        }
        foreach (var id in LastInstances.Keys)
            if (!current.ContainsKey(id)) _removed++;
        CurrentInstances = LastInstances;
        LastInstances = current;
        _maxReplicas = Math.Max(_maxReplicas, current.Count);
    }
}

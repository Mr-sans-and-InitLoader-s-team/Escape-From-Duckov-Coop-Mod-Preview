using System.Collections;
using System.Reflection;
using Duckov.Utilities;
using HarmonyLib;
using UnityEngine;

namespace CoopRuntimeTests;

internal static class RuntimeAIStreamChecks
{
    internal static object Run(Assembly mod, bool server)
    {
        var manager = AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.COOPManager"), "AI").GetValue(null);
        var registry = AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.CoopSyncDatabase"), "AI").GetValue(null);
        object Get(object o, string name) => AccessTools.Field(o.GetType(), name)?.GetValue(o) ?? AccessTools.Property(o.GetType(), name).GetValue(o);
        void Set(object o, string name, object value)
        {
            var field = AccessTools.Field(o.GetType(), name);
            if (field != null) field.SetValue(o, value); else AccessTools.Property(o.GetType(), name).SetValue(o, value);
        }
        object Call(string name, params object[] args) => AccessTools.Method(manager.GetType(), name).Invoke(manager, args);
        object Message(string name, params (string Key, object Value)[] fields)
        {
            var message = Activator.CreateInstance(AccessTools.TypeByName("EscapeFromDuckovCoopMod." + name));
            foreach (var field in fields) Set(message, field.Key, field.Value);
            return message;
        }
        var statusType = AccessTools.TypeByName("EscapeFromDuckovCoopMod.AIStatus");
        var active = Enum.Parse(statusType, "Active");
        var checks = new List<object>();
        void Check(string name, bool passed, object evidence) => checks.Add(new { name, passed, evidence });

        var prefix = AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.Patch_SABPD_FixedUpdate_AllPlayersUnion"), "Prefix");
        if (!server)
        {
            const int unknownActivation = 2026000099;
            Call("Client_HandleActivationState", Message("AIActivationStateRpc", ("Id", unknownActivation), ("Activated", true)));
            Check("activation_without_metadata_does_not_create_phantom_ai",
                !((IEnumerable)Get(registry, "Entries")).Cast<object>().Any(e => (int)Get(e,"Id") == unknownActivation), null);
            Check("client_native_distance_culling", (bool)prefix.Invoke(null, new object[] { SetActiveByPlayerDistance.Instance }), null);
            var modelName = CharacterMainControl.Main.characterModel.name.Replace("(Clone)", "");
            var template = Call("ResolveModelPrefab", modelName) as CharacterModel;
            Check("replica_template_is_asset_without_live_equipment", template && !template.gameObject.scene.IsValid(), template ? template.name : null);
        }

        if (server)
        {
            var entry = ((IEnumerable)Get(registry, "Entries")).Cast<object>().First(e => Get(e, "Controller") is AICharacterController c && c && c.CharacterMainControl);
            var controller = (AICharacterController)Get(entry, "Controller");
            var fields = new[] { "LastKnownPosition", "LastKnownRotation", "LastKnownVelocity", "LastStateSentTime", "LastAnimSample" };
            var saved = fields.ToDictionary(f => f, f => Get(entry, f));
            try
            {
                Set(entry, "LastKnownPosition", controller.transform.position - Vector3.right);
                Set(entry, "LastStateSentTime", Time.unscaledTime - .5f);
                Call("RefreshEntryPose", entry);
                var velocity = (Vector3)Get(entry, "LastKnownVelocity");
                Check("velocity_uses_elapsed_sample_time", Mathf.Abs(velocity.x - 2f) < .01f && Mathf.Abs(velocity.z) < .01f, velocity);
            }
            finally { foreach (var field in saved) Set(entry, field.Key, field.Value); }
            var trackerType = mod.GetType("EscapeFromDuckovCoopMod.AISyncTracker");
            var tracker = controller.GetComponent(trackerType);
            Check("ai_present_before_host_started_has_health_tracker", tracker != null, null);
            if (tracker)
            {
                var calls = AccessTools.Field(typeof(UnityEngine.Events.UnityEventBase), "m_Calls").GetValue(controller.CharacterMainControl.Health.OnHealthChange);
                int Listeners() => ((ICollection)AccessTools.Field(calls.GetType(), "m_RuntimeCalls").GetValue(calls)).Count;
                var before = Listeners();
                for (var i = 0; i < 100; i++) AccessTools.Method(trackerType, "Initialize").Invoke(tracker, new[] { manager, controller, entry });
                Check("tracker_rebinding_does_not_duplicate_health_or_death_hooks", Listeners() == before && before > 0, new { before, after = Listeners() });
            }
            return checks;
        }

        var ids = Enumerable.Range(2026000100, 8).ToArray();
        var origin = CharacterMainControl.Main.transform.position;
        var rows = ids.Select(id => AccessTools.Method(registry.GetType(), "GetOrCreate").Invoke(registry, new object[] { id })).ToArray();
        int QueueCount() => (int)Get(Get(manager, "_pendingStateUpdates"), "Count");
        void Drain()
        {
            for (var i = 0; QueueCount() > 0 && i < 256; i++) Call("Client_ProcessPendingStateUpdates");
        }
        object State(int id, double time, Vector3 position) => Message("AIStateUpdateRpc", ("Id", id), ("Timestamp", time),
            ("Position", position), ("Rotation", Quaternion.identity), ("StatusOverride", active));
        try
        {
            foreach (var row in rows)
            {
                Set(row, "Status", active); Set(row, "Activated", true);
                Set(row, "SpawnPosition", origin); Set(row, "LastKnownPosition", origin); Set(row, "LastKnownRemoteTime", 10000d);
                Set(row, "CurrentHealth", 100f); Set(row, "MaxHealth", 100f);
            }
            Drain();
            for (var frame = 1; frame <= 1000; frame++)
                foreach (var id in ids) Call("Client_HandleState", State(id, 10000d + frame, origin + Vector3.right * (frame / 1000f)));
            var queued = QueueCount();
            Check("burst_coalesces_8000_poses_to_8_pending_ids", queued == ids.Length, new { queued, packets = 8000 });
            Drain();
            Check("burst_applies_latest_pose_for_every_ai", rows.All(e => (double)Get(e, "LastKnownRemoteTime") == 11000d), null);

            Call("Client_HandleState", State(ids[0], 12000d, origin + Vector3.right * 2));
            Call("Client_HandleState", State(ids[0], 11999d, origin + Vector3.left * 2));
            Drain();
            Check("queued_out_of_order_pose_cannot_rewind", (double)Get(rows[0], "LastKnownRemoteTime") == 12000d, Get(rows[0], "LastKnownPosition"));
            Call("Client_HandleState", State(ids[0], 11998d, origin + Vector3.left * 3));
            Drain();
            Check("applied_out_of_order_pose_cannot_rewind", (double)Get(rows[0], "LastKnownRemoteTime") == 12000d, Get(rows[0], "LastKnownPosition"));

            var snapshot = Message("AISnapshotEntry", ("Id", ids[0]), ("Status", active), ("Activated", true),
                ("LastKnownRemoteTime", 11900d), ("SpawnPosition", origin), ("LastKnownPosition", origin + Vector3.left * 4),
                ("SpawnRotation", Quaternion.identity), ("LastKnownRotation", Quaternion.identity));
            Call("ApplySnapshot", rows[0], snapshot);
            Check("old_reliable_snapshot_cannot_rewind_new_pose", (double)Get(rows[0], "LastKnownRemoteTime") == 12000d &&
                Vector3.Distance((Vector3)Get(rows[0], "LastKnownPosition"), origin + Vector3.right * 2) < .001f, Get(rows[0], "LastKnownPosition"));

            Call("Client_HandleState", State(ids[1], 13000d, origin + Vector3.right * 1000));
            Check("range_cull_preserves_host_lifecycle", Get(rows[1], "Status").ToString() == "Active" && (bool)Get(rows[1], "Activated"), Get(rows[1], "Status").ToString());
            Check("range_exit_updates_pose_without_respawning_old_position", !(bool)Call("ShouldHaveClientReplica", rows[1]) &&
                Vector3.Distance((Vector3)Get(rows[1], "LastKnownPosition"), origin + Vector3.right * 1000) < .01f, null);
            Call("Client_HandleActivationState", Message("AIActivationStateRpc", ("Id", ids[1]), ("Activated", false)));
            Check("sleep_does_not_mark_ai_despawned", Get(rows[1], "Status").ToString() == "Active" && !(bool)Get(rows[1], "Activated"), Get(rows[1], "Status").ToString());
            Call("Client_HandleState", State(ids[1], 13001d, origin));
            Drain();
            Call("Client_HandleActivationState", Message("AIActivationStateRpc", ("Id", ids[1]), ("Activated", true)));
            Check("wake_reuses_retained_spawn_metadata", (bool)Call("ShouldHaveClientReplica", rows[1]), null);
            Call("Client_HandleDespawn", Message("AIDespawnRpc", ("Id", ids[2]), ("Status", Enum.Parse(statusType, "Dead"))));
            Call("Client_HandleState", State(ids[2], 14000d, origin));
            Drain();
            Check("late_pose_cannot_resurrect_dead_ai", Get(rows[2], "Status").ToString() == "Dead", null);

            var replicas = (IDictionary)Get(manager, "_clientReplicas");
            var existing = replicas.Keys.Cast<int>().FirstOrDefault();
            if (existing != 0)
            {
                var real = AccessTools.Method(registry.GetType(), "GetOrCreate").Invoke(registry, new object[] { existing });
                var oldInstance = Get(replicas[existing], "Instance");
                Call("Client_HandleState", State(existing, (double)Get(real, "LastKnownRemoteTime") - 1d, origin + Vector3.right * 1000));
                Check("stale_out_of_range_pose_does_not_destroy_live_model", replicas.Contains(existing) && ReferenceEquals(oldInstance, Get(replicas[existing], "Instance")), null);
            }
        }
        finally
        {
            Drain();
            foreach (var id in ids) AccessTools.Method(registry.GetType(), "RemoveEntry").Invoke(registry, new object[] { id });
        }
        return checks;
    }
}

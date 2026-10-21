using System.Collections;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeAIAuditProbe
{
    private static Assembly _mod;
    private static string _deathLog;

    internal static object Run(Assembly mod, JObject command)
    {
        if (_mod == null)
        {
            _mod = mod;
            _deathLog = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", "native-death-errors.jsonl"));
            File.WriteAllText(_deathLog, "");
            var finalizer = new HarmonyMethod(typeof(RuntimeAIAuditProbe), nameof(TraceDeadError)) { priority = Priority.Last };
            new Harmony("local.coop.ai-death-trace").Patch(AccessTools.Method(typeof(CharacterMainControl), "OnDead"),
                prefix: new HarmonyMethod(typeof(RuntimeAIAuditProbe), nameof(TraceDead)), finalizer: finalizer);
        }
        object Read(object target, string name) => AccessTools.Field(target.GetType(), name)?.GetValue(target) ??
            AccessTools.Property(target.GetType(), name)?.GetValue(target);
        var manager = AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.COOPManager"), "AI").GetValue(null);
        if ((bool?)command["refresh"] == true) AccessTools.Method(manager.GetType(), "Client_RefreshSnapshot").Invoke(manager, null);
        var registry = AccessTools.Property(mod.GetType("EscapeFromDuckovCoopMod.CoopSyncDatabase"), "AI").GetValue(null);
        var rows = ((IEnumerable)Read(registry, "Entries")).Cast<object>().ToArray();
        var controllers = rows.Select(e => Read(e, "Controller")).OfType<AICharacterController>().Where(c => c).ToArray();
        var replicas = Object.FindObjectsByType(mod.GetType("EscapeFromDuckovCoopMod.RemoteAIReplicaTag"), FindObjectsInactive.Include,
            FindObjectsSortMode.None).Cast<Component>().ToArray();
        var culler = Duckov.Utilities.SetActiveByPlayerDistance.Instance;
        var managed = culler ? AccessTools.Field(culler.GetType(), "cachedListRef").GetValue(culler) as List<GameObject> : null;
        return new
        {
            activation = AccessTools.Property(manager.GetType(), "ActivationRadius").GetValue(manager),
            deactivation = AccessTools.Property(manager.GetType(), "DeactivationRadius").GetValue(manager),
            joining = AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.LateJoinScene"), "_joining").GetValue(null),
            nativeCulling = new { distance = culler ? culler.Distance : 0f, registered = managed?.Count ?? 0,
                active = managed?.Count(go => go && go.activeSelf) ?? 0,
                activeBeyondLocalRange = managed?.Count(go => go && go.activeSelf && CharacterMainControl.Main &&
                    Vector3.Distance(go.transform.position, CharacterMainControl.Main.transform.position) > culler.Distance + 1f) ?? 0 },
            unregistered = Object.FindObjectsByType<AICharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene.IsValid() && !controllers.Contains(c))
                .Select(c => new { c.name, id = c.GetInstanceID(), scene = c.gameObject.scene.name,
                    c.isActiveAndEnabled, hasCharacter = c.CharacterMainControl != null }).ToArray(),
            entries = rows.Select(e => new
            {
                id = Read(e, "Id"), status = Read(e, "Status").ToString(), activated = Read(e, "Activated"),
                model = Read(e, "ModelName"), preset = Read(e, "CharacterPresetKey"), hp = Read(e, "CurrentHealth"),
                scene = Read(e, "SceneBuildIndex"), vehicle = Read(e, "IsVehicle"),
                anchor = AccessTools.Method(manager.GetType(), "GetEntryAnchor").Invoke(null, new[] { e }),
                velocity = Read(e, "LastKnownVelocity"), remoteTime = Read(e, "LastKnownRemoteTime"), received = Read(e, "LastStateReceivedTime"),
                expectedReplica = AccessTools.Method(manager.GetType(), "ShouldHaveClientReplica").Invoke(manager, new[] { e })
            }).ToArray(),
            replicas = replicas.Select(c => new { id = Read(c, "Id"), instance = c.gameObject.GetInstanceID(), c.gameObject.activeInHierarchy }).ToArray(),
            invalidGuns = Object.FindObjectsByType<ItemAgent_Gun>(FindObjectsSortMode.None).Where(g => !g.Item)
                .Select(g => new { g.name, holder = g.Holder ? g.Holder.name : null,
                    parent = g.transform.parent ? g.transform.parent.name : null }).ToArray()
        };
    }

    private static void TraceDead(CharacterMainControl __instance, DamageInfo dmgInfo, out string __state)
    {
        try
        {
            __state = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                time = Time.realtimeSinceStartup, victim = __instance.name, id = __instance.GetInstanceID(),
                main = __instance == CharacterMainControl.Main, scene = __instance.gameObject.scene.name,
                remoteAI = __instance.GetComponent(_mod.GetType("EscapeFromDuckovCoopMod.RemoteAIReplicaTag")) != null,
                remotePlayer = __instance.GetComponent(_mod.GetType("EscapeFromDuckovCoopMod.RemoteReplicaTag")) != null,
                preset = __instance.characterPreset ? __instance.characterPreset.name : null,
                item = __instance.CharacterItem ? __instance.CharacterItem.GetInstanceID() : 0,
                model = __instance.characterModel ? __instance.characterModel.name : null,
                attacker = dmgInfo.fromCharacter ? dmgInfo.fromCharacter.name : null,
                attackerMain = dmgInfo.fromCharacter && dmgInfo.fromCharacter == CharacterMainControl.Main,
                level = LevelManager.LevelInited, loading = SceneLoader.IsSceneLoading,
                hasHealth = __instance.Health != null, position = __instance.transform.position
            });
        }
        catch (Exception error) { __state = error.ToString(); }
    }

    private static Exception TraceDeadError(Exception __exception, string __state)
    {
        if (__exception != null) File.AppendAllText(_deathLog, __state + "\n" + __exception + "\n");
        return __exception;
    }
}

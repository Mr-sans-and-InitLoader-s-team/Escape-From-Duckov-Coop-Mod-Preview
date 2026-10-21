using Duckov.MiniMaps;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov.Utilities;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal static class RuntimeRegionProbe
{
    private static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    private static object Read(object value, string name) => AccessTools.Field(value.GetType(), name)?.GetValue(value);
    private static object Events(UnityEventBase value) => Enumerable.Range(0, value.GetPersistentEventCount()).Select(i => new
    {
        method = value.GetPersistentMethodName(i),
        target = value.GetPersistentTarget(i) is Component c ? c ? Path(c.transform) : null :
            value.GetPersistentTarget(i) ? value.GetPersistentTarget(i).name : null
    }).ToArray();
    private static T[] Loaded<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(c => c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded).ToArray();

    internal static async UniTask<object> Run(JObject command)
    {
        if (command["holdPlayer"] != null) CharacterMainControl.Main.movementControl.MovementEnabled = !(bool)command["holdPlayer"];
        if (command["forceBoss"] != null) LevelManager.forceBossSpawn = (bool)command["forceBoss"];
        int spawned = 0;
        if (command["spawn"] != null)
        {
            var preset = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>().Concat(GameplayDataSettings.CharacterRandomPresetData.presets)
                .First(p => p && (p.name == (string)command["spawn"] || p.nameKey == (string)command["spawn"]));
            var point = command["x"] != null ? new Vector3((float)command["x"], (float)command["y"], (float)command["z"]) : CharacterMainControl.Main.transform.position + Vector3.right * 12;
            var character = await preset.CreateCharacterAsync(point, Vector3.forward, MultiSceneCore.ActiveSubScene?.buildIndex ?? MultiSceneCore.MainScene.Value.buildIndex, null, false);
            spawned = character.GetInstanceID();
            RuntimeCombat.PauseBrain(character, true);
            character.movementControl.MovementEnabled = false;
            character.Health.SetInvincible(true);
            await UniTask.Delay(1200);
        }
        if (command["nativeAI"] != null)
        {
            var character = Loaded<CharacterMainControl>().Single(c => c.GetInstanceID() == (int)command["nativeAI"]);
            if (command["x"] != null)
            {
                character.movementControl.MovementEnabled = false;
                character.SetPosition(new Vector3((float)command["x"], (float)command["y"], (float)command["z"]));
            }
            if ((bool?)command["kill"] == true)
            {
                character.Health.SetInvincible(false);
                character.Health.Hurt(new DamageInfo(CharacterMainControl.Main) { damageValue = 100000, ignoreArmor = true });
                await UniTask.Delay(1500);
            }
        }
        var sampleStack = VolumeManager.instance.CreateStack();
        if (GameCamera.Instance) VolumeManager.instance.Update(sampleStack, GameCamera.Instance.volumeProxy,
            GameCamera.Instance.renderCamera.GetUniversalAdditionalCameraData().volumeLayerMask);
        var color = sampleStack.GetComponent<ColorAdjustments>();
        var profileTypes = Loaded<Volume>().Where(v => v.sharedProfile &&
            (v.sharedProfile.name.Contains("Boss") || v.sharedProfile.name.Contains("Myst")))
            .SelectMany(v => v.sharedProfile.components).Select(c => c.GetType()).Distinct().ToArray();
        var result = new
        {
            spawned,
            areaRegistrations = PointsOfInterests.Points.OfType<SimplePointOfInterest>().Where(p => p && p.IsArea)
                .GroupBy(p => p.GetInstanceID()).Select(g => new { id = g.Key, path = Path(g.First().transform), count = g.Count() }).ToArray(),
            stack = profileTypes.Select(t => new { type = t.FullName, fields = t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(f => typeof(VolumeParameter).IsAssignableFrom(f.FieldType)).ToDictionary(f => f.Name,
                    f => { var parameter = f.GetValue(sampleStack.GetComponent(t)); return parameter?.GetType().GetProperty("value")?.GetValue(parameter)?.ToString(); }) }).ToArray(),
            camera = GameCamera.Instance ? new { trigger = GameCamera.Instance.volumeProxy.position, filter = color.colorFilter.value.ToString(), saturation = color.saturation.value, contrast = color.contrast.value } : null,
            markers = Loaded<SimplePointOfInterest>().Select(p => new
            {
                path = Path(p.transform), p.name, p.IsArea, p.AreaRadius, p.DisplayName, p.HideIcon,
                icon = p.Icon ? p.Icon.name : null, color = p.Color.ToString(), active = p.gameObject.activeInHierarchy,
                position = p.transform.position, scene = p.gameObject.scene.name, mapScene = p.OverrideScene,
                nativeAI = p.GetComponentInParent<CharacterMainControl>() ? p.GetComponentInParent<CharacterMainControl>().GetInstanceID() : 0
            }).ToArray(),
            volumes = Loaded<Volume>().Select(v => new
            {
                path = Path(v.transform), active = v.gameObject.activeInHierarchy, v.enabled, v.isGlobal, v.weight, v.priority,
                v.blendDistance, profile = v.sharedProfile ? v.sharedProfile.name : null, position = v.transform.position,
                colliders = v.GetComponents<Collider>().Select(c => new { type = c.GetType().Name, c.enabled, c.isTrigger, c.bounds.center, c.bounds.size,
                    distance = GameCamera.Instance && c.enabled && c.gameObject.activeInHierarchy ? Vector3.Distance(c.ClosestPoint(GameCamera.Instance.volumeProxy.position), GameCamera.Instance.volumeProxy.position) : -1 }).ToArray()
            }).ToArray(),
            roots = Loaded<CharacterSpawnerRoot>().Select(r => new
            {
                path = Path(r.transform), r.SpawnerGuid, r.spawnChance, r.needTrigger, r.enabled, active = r.gameObject.activeInHierarchy,
                created = Read(r, "created"), r.RelatedScene, position = r.transform.position,
                start = Events(r.OnStartEvent), dead = Events(r.OnAllDeadEvent),
                presets = r.GetComponentsInChildren<RandomCharacterSpawner>(true).SelectMany(s => s.randomPresetInfos)
                    .Where(i => i.randomPreset).Select(i => new { i.randomPreset.name, i.randomPreset.isBoss }).Distinct().ToArray(),
                spawners = r.GetComponentsInChildren<RandomCharacterSpawner>(true).Select(s => new { path = Path(s.transform), start = Events(s.OnStartCreateEvent) }).ToArray()
            }).ToArray(),
            selectors = Loaded<RandomActiveSelector>().Select(s => new
            {
                path = Path(s.transform), s.activeChance, index = Read(s, "activeIndex"), set = Read(s, "setted"),
                choices = s.selections.Select(t => t ? Path(t.transform) : null).ToArray()
            }).Concat(Loaded<RandomActiveSelectorWeighted>().Select(s => new
            {
                path = Path(s.transform), s.activeChance, index = Read(s, "activeIndex"), set = Read(s, "setted"),
                choices = s.selections.Select(t => t.target ? Path(t.target.transform) : null).ToArray()
            })).ToArray()
        };
        VolumeManager.instance.DestroyStack(sampleStack);
        return result;
    }
}

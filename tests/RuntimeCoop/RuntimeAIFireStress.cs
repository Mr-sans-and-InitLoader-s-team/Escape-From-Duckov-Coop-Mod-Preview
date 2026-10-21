using System.Diagnostics;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Scenes;
using Duckov;
using HarmonyLib;
using ItemStatsSystem;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

internal sealed class RuntimeAIFireStress : MonoBehaviour
{
    private sealed class Sample { internal int Calls; internal long Ticks, Max, Bytes; }
    private static readonly Dictionary<MethodBase, Sample> Samples = new();
    private static readonly List<float> Frames = new();
    private static readonly List<CharacterMainControl> Shooters = new();
    private static bool _installed, _active;
    private static long _allocated;
    private static int _collections, _peakFakes, _spawned, _hits;
    private static HashSet<Projectile> _fakes;
    private static readonly Dictionary<string, int> Audio = new();
    private static readonly HashSet<Component> Emitters = new();
    private static int _peakEmitters;
    private static int _fastPlayed, _fastFallback;

    internal static async UniTask<object> Run(Assembly mod, JObject command)
    {
        if (!_installed)
        {
            var harmony = new Harmony("local.coop.ai-fire-stress");
            foreach (var (type, names) in new[] {
                (mod.GetType("EscapeFromDuckovCoopMod.WeaponHandle"), new[] { "Client_HandleFireEvent", "SpawnVisualProjectile" }),
                (mod.GetType("EscapeFromDuckovCoopMod.FxManager"), new[] { "PlayMuzzleFxAndShell", "Client_PlayLocalShotFx", "Client_PlayWorldShotFx" }),
                (mod.GetType("EscapeFromDuckovCoopMod.LocalHitKillFx"), new[] { "ClientPlayForPlayer", "PlayHurtVisual" }),
                (typeof(Projectile), new[] { "Update", "UpdateMoveAndCheck", "UpdateFlyThroughSound" }),
                (typeof(ItemAgent_Gun), new[] { "TransToFire", "ShootOneBullet", "PostShootSound" }),
                (typeof(DamageReceiver), new[] { "Hurt" }), (typeof(BulletPool), new[] { "GetABullet" }),
                (mod.GetType("EscapeFromDuckovCoopMod.CoopAudioSync"), new[] { "HandleIncoming", "Dispatch" }),
                (mod.GetType("EscapeFromDuckovCoopMod.AIAwarenessSync"), new[] { "Client_HandleSound" }),
                (typeof(Duckov.Sounds.SoundVisualization), new[] { "Update", "Trigger" }),
                (mod.GetType("EscapeFromDuckovCoopMod.CoopAudioEmitter"), new[] { "Spawn", "Update", "Play" }) })
            foreach (var name in names)
                harmony.Patch(AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(RuntimeAIFireStress), nameof(Enter)),
                    postfix: new HarmonyMethod(typeof(RuntimeAIFireStress), nameof(Leave)));
            harmony.Patch(AccessTools.Method(typeof(Projectile), nameof(Projectile.Init), new[] { typeof(ProjectileContext) }),
                postfix: new HarmonyMethod(typeof(RuntimeAIFireStress), nameof(Spawned)));
            harmony.Patch(AccessTools.Method(typeof(Health), nameof(Health.Hurt)),
                postfix: new HarmonyMethod(typeof(RuntimeAIFireStress), nameof(Hit)));
            foreach (var method in typeof(AudioManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "MPost" && m.GetParameters().Length == 2))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(RuntimeAIFireStress), nameof(NativeAudio)));
            foreach (var name in new[] { "Dispatch", "HandleIncoming" })
                harmony.Patch(AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.CoopAudioSync"), name),
                    prefix: new HarmonyMethod(typeof(RuntimeAIFireStress), nameof(NetworkAudio)));
            harmony.Patch(AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.CoopAudioEmitter"), "Spawn"),
                postfix: new HarmonyMethod(typeof(RuntimeAIFireStress), nameof(EmitterSpawned)));
            harmony.Patch(AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.CoopAudioEmitter"), "OnDestroy"),
                postfix: new HarmonyMethod(typeof(RuntimeAIFireStress), nameof(EmitterDestroyed)));
            if (mod.GetType("EscapeFromDuckovCoopMod.CoopCombatAudio") is Type combatAudio)
            {
                harmony.Patch(AccessTools.Method(combatAudio, "TryPlayOneShot"),
                    postfix: new HarmonyMethod(typeof(RuntimeAIFireStress), nameof(FastPlayed)));
            }
            _fakes = (HashSet<Projectile>)AccessTools.Field(mod.GetType("EscapeFromDuckovCoopMod.FakeProjectileRegistry"), "_fakes").GetValue(null);
            var go = new GameObject("AIFireStress"); DontDestroyOnLoad(go); go.AddComponent<RuntimeAIFireStress>();
            _installed = true;
        }
        if ((bool?)command["hearing"] is bool hearing) RuntimeHearingProbe.SetEnabled(hearing);
        if ((bool?)command["begin"] == true)
        {
            _active = false; Samples.Clear(); Frames.Clear(); Audio.Clear(); _peakFakes = _spawned = _hits = _fastPlayed = _fastFallback = 0; _peakEmitters = Emitters.Count;
            _allocated = GC.GetAllocatedBytesForCurrentThread(); _collections = GC.CollectionCount(0); _active = true;
            RuntimeHearingProbe.Begin();
        }
        if ((bool?)command["verify"] == true)
        {
            var checks = new Dictionary<string, bool>();
            var combat = mod.GetType("EscapeFromDuckovCoopMod.CoopCombatAudio", true);
            var payloadType = mod.GetType("EscapeFromDuckovCoopMod.CoopAudioEventPayload", true);
            var payload = Activator.CreateInstance(payloadType);
            AccessTools.Field(payloadType, "Kind").SetValue(payload, Enum.Parse(AccessTools.Field(payloadType, "Kind").FieldType, "ThreeD"));
            AccessTools.Field(payloadType, "EventName").SetValue(payload, "SFX/Combat/Gun/Shoot/ak47");
            AccessTools.Field(payloadType, "Position").SetValue(payload, CharacterMainControl.Main.transform.position);
            var play = AccessTools.Method(combat, "TryPlayGunshot");
            checks["native_gunshot_starts"] = (bool)play.Invoke(null, new[] { payload });
            var transient = AccessTools.Method(combat, "IsTransientGunshot");
            checks["oneshot_gunshot_unreliable"] = (bool)transient.Invoke(null, new[] { payload });
            foreach (var field in new[] { "HasSwitch", "HasSoundKey", "HasKazooPitch" })
            {
                AccessTools.Field(payloadType, field).SetValue(payload, true);
                checks["preserve_parameterized_" + field] = !(bool)play.Invoke(null, new[] { payload });
                checks["parameterized_reliable_" + field] = !(bool)transient.Invoke(null, new[] { payload });
                AccessTools.Field(payloadType, field).SetValue(payload, false);
            }
            AccessTools.Field(payloadType, "EventName").SetValue(payload, "SFX/Combat/Gun/Reload/single_shotgun_start");
            checks["preserve_other_audio"] = !(bool)play.Invoke(null, new[] { payload });
            checks["other_audio_reliable"] = !(bool)transient.Invoke(null, new[] { payload });
            var blocked = AccessTools.Method(mod.GetType("EscapeFromDuckovCoopMod.CoopAudioSync"), "ShouldBlockUi");
            checks["flyby_not_broadcast"] = (bool)blocked.Invoke(null, new object[] { "SFX/Combat/Bullet/flyby", true });
            checks["gunshot_still_synchronized"] = !(bool)blocked.Invoke(null, new object[] { "SFX/Combat/Gun/Shoot/ak47", true });
            var sources = GameObject.Find("Sound Sources"); var before = sources ? sources.transform.childCount : 0;
            var flyby = (Action<AudioManager, Vector3>)Delegate.CreateDelegate(typeof(Action<AudioManager, Vector3>), AccessTools.Method(typeof(AudioManager), "OnBulletFlyby"));
            for (var i = 0; i < 100; i++) flyby(AudioManager.Instance, CharacterMainControl.Main.transform.position);
            sources = GameObject.Find("Sound Sources");
            checks["flyby_no_unused_objects"] = (sources ? sources.transform.childCount : 0) == before;
            if (Shooters.Count >= 2)
            {
                var scope = mod.GetType("EscapeFromDuckovCoopMod.ProjectileShotFx", true);
                var begin = AccessTools.Method(scope, "Begin"); var end = AccessTools.Method(scope, "End"); var take = AccessTools.Method(scope, "Take");
                var a = Shooters[0].GetGun(); var b = Shooters[1].GetGun();
                bool Take(ItemAgent_Gun gun) => (bool)take.Invoke(null, new object[] { gun });
                var outer = begin.Invoke(null, new object[] { a });
                checks["first_pellet_fx"] = Take(a); checks["remaining_pellets_no_duplicate_fx"] = !Take(a);
                var takeSound=AccessTools.Method(scope,"TakeSound");var hearingSound=new AISound{fromObject=a.gameObject};
                checks["one_hearing_event_per_shot"]=(bool)takeSound.Invoke(null,new object[]{hearingSound}) && !(bool)takeSound.Invoke(null,new object[]{hearingSound});
                var inner = begin.Invoke(null, new object[] { b });
                checks["nested_shot_fx"] = Take(b) && !Take(b);
                end.Invoke(null, new[] { inner }); checks["restore_outer_shot"] = !Take(a);
                end.Invoke(null, new[] { outer }); checks["outside_scope_unchanged"] = Take(a);
            }
            else foreach(var pair in RuntimeHearingProbe.Verify(mod)) checks[pair.Key]=pair.Value;
            return checks;
        }
        if ((bool?)command["end"] == true)
        {
            _active = false;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - _allocated;
            var frames = Frames.OrderBy(v => v).ToArray();
            float P(float p) => frames.Length == 0 ? 0 : frames[Math.Min(frames.Length - 1, (int)(frames.Length * p))];
            return new { frames = frames.Length, seconds = frames.Sum(v => (double)v) / 1000, medianMs = P(.5f), p95Ms = P(.95f), p99Ms = P(.99f), maxMs = frames.LastOrDefault(),
                allocatedBytes = bytes > 0 ? (long?)bytes : null, collections = GC.CollectionCount(0) - _collections, peakFakes = _peakFakes, remainingFakes = _fakes.Count, spawned = _spawned, hits = _hits,
                peakEmitters = _peakEmitters, remainingEmitters = Emitters.Count, audio = Audio,
                fastPlayed = _fastPlayed, fastFallback = _fastFallback,
                hearing = RuntimeHearingProbe.Snapshot(),
                methods = Samples.Select(p => new { method = p.Key.DeclaringType.Name + "." + p.Key.Name, calls = p.Value.Calls,
                    totalMs = p.Value.Ticks * 1000d / Stopwatch.Frequency, maxMs = p.Value.Max * 1000d / Stopwatch.Frequency, allocatedBytes = p.Value.Bytes }).ToArray() };
        }
        if ((int?)command["prepare"] is int prepareCount)
        {
            foreach (var character in Shooters) if (character) Object.Destroy(character.gameObject);
            Shooters.Clear();
            var preset = Object.FindObjectsByType<AICharacterController>(FindObjectsSortMode.None).Select(a => a.CharacterMainControl)
                .First(c => c && c.characterPreset && c.characterPreset.nameKey == "Cname_Scav").characterPreset;
            var center = CharacterMainControl.Main.transform.position;
            for (var i = 0; i < prepareCount; i++)
            {
                var point = center + new Vector3((i % 4 - 1.5f) * 2.5f, 0, 12 + i / 4 * 2.5f);
                if (!RuntimePlacement.Ground(point, out var ground)) throw new InvalidOperationException("No supported AI firing point: " + point);
                var character = await preset.CreateCharacterAsync(ground.point + Vector3.up * .05f, Vector3.back,
                    MultiSceneCore.ActiveSubScene?.buildIndex ?? MultiSceneCore.MainScene.Value.buildIndex, null, false);
                RuntimeCombat.PauseBrain(character, true); character.movementControl.MovementEnabled = false; character.Health.SetInvincible(true);
                character.PrimWeaponSlot().Content?.Detach();
                var weapon = await ItemAssetsCollection.InstantiateAsync((int?)command["weapon"] ?? 240);
                if (!character.CharacterItem.TryPlug(weapon)) throw new InvalidOperationException("Cannot equip native AI fixture gun");
                character.ChangeHoldItem(weapon);
                await UniTask.NextFrame();
                var ammoType = (int?)command["ammo"] ?? 140;
                var ammo = await ItemAssetsCollection.InstantiateAsync(ammoType); ammo.StackCount = 30;
                character.CharacterItem.Inventory.AddAndMerge(ammo);
                var setting = weapon.GetComponent<ItemSetting_Gun>();
                if (!setting.IsValidBullet(ammo)) throw new InvalidOperationException("Wrong native fixture ammunition");
                setting.SetTargetBulletType(ammoType);
                setting.LoadBulletsFromInventory(character.CharacterItem.Inventory);
                await UniTask.WaitUntil(() => setting.BulletCount > 0).Timeout(TimeSpan.FromSeconds(10));
                if (!character.GetGun() || character.GetGun().BulletCount <= 0) throw new InvalidOperationException("Native fixture has no loaded gun");
                Shooters.Add(character);
            }
            await UniTask.Delay(2500);
        }
        if ((float?)command["seconds"] is float seconds)
        {
            var count = Math.Min((int?)command["count"] ?? Shooters.Count, Shooters.Count);
            var rate = (float?)command["rate"] ?? 10;
            var victim = (bool?)command["remote"] == false ? CharacterMainControl.Main : RuntimeCombat.RemotePlayer(
                mod.GetType("EscapeFromDuckovCoopMod.NetService").GetField("Instance").GetValue(null));
            var guns = Shooters.Take(count).Select(c => c.GetGun()).ToArray();
            var next = new float[count]; var shots = new int[count];
            var fire = (Action<ItemAgent_Gun, bool>)Delegate.CreateDelegate(typeof(Action<ItemAgent_Gun, bool>), AccessTools.Method(typeof(ItemAgent_Gun), "TransToFire"));
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                var elapsed = Time.realtimeSinceStartup - start;
                var collider = victim.mainDamageReceiver.GetComponent<Collider>();
                var aim = collider.bounds.center + Vector3.up * ((float?)command["missHeight"] ?? 0);
                for (var i = 0; i < guns.Length; i++)
                {
                    var gun = guns[i]; if (elapsed < next[i]) continue;
                    next[i] += 1 / rate;
                    var bullet = gun.GunItemSetting.GetCurrentLoadedBullet();
                    if (!bullet) throw new InvalidOperationException("Fixture ammunition depleted");
                    if (bullet.StackCount < 3) bullet.StackCount = 30;
                    AccessTools.Field(typeof(ItemSetting_Gun), "_bulletCountCache").SetValue(gun.GunItemSetting, gun.GunItemSetting.GetBulletCount());
                    Shooters[i].SetAimPoint(aim); gun.OverrideShootPoint(aim);
                    var before = gun.GunItemSetting.BulletCount; fire(gun, true);
                    if (gun.GunItemSetting.BulletCount < before) shots[i]++;
                }
                await UniTask.NextFrame();
            }
            return new { count, rate, seconds = Time.realtimeSinceStartup - start, shots, target = victim.GetInstanceID() };
        }
        return new { shooters = Shooters.Where(c => c).Select(c => new { id = c.GetInstanceID(), preset = c.characterPreset.name,
            gun = c.GetGun()?.Item.TypeID, pellets = c.GetGun()?.ShotCount, position = c.transform.position }).ToArray(), active = _active, fakes = _fakes.Count };
    }

    private static void Enter(out (long Ticks, long Bytes) __state) => __state = _active ? (Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;
    private static void Leave(MethodBase __originalMethod, (long Ticks, long Bytes) __state)
    {
        if (!_active || __state.Ticks == 0) return;
        var ticks = Stopwatch.GetTimestamp() - __state.Ticks;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - __state.Bytes;
        if (!Samples.TryGetValue(__originalMethod, out var sample)) Samples[__originalMethod] = sample = new Sample();
        sample.Calls++; sample.Ticks += ticks; sample.Max = Math.Max(sample.Max, ticks); sample.Bytes += Math.Max(0, bytes);
    }
    private static void Spawned() { if (_active) _spawned++; }
    private static void Hit(bool __result) { if (_active && __result) _hits++; }
    private static void NativeAudio(string eventName) { if (_active) CountAudio("local/" + eventName); }
    private static void NetworkAudio(MethodBase __originalMethod, object payload)
    {
        if (_active) CountAudio(__originalMethod.Name + "/" + AccessTools.Field(payload.GetType(), "EventName").GetValue(payload));
    }
    private static void CountAudio(string key) { Audio.TryGetValue(key, out var count); Audio[key] = count + 1; }
    private static void EmitterSpawned(Component __result) { if (__result) { Emitters.Add(__result); _peakEmitters = Math.Max(_peakEmitters, Emitters.Count); } }
    private static void EmitterDestroyed(Component __instance) => Emitters.Remove(__instance);
    private static void FastPlayed(bool __result) { if (_active) { if (__result) _fastPlayed++; else _fastFallback++; } }
    private void Update() { if (_active) { Frames.Add(Time.unscaledDeltaTime * 1000); _peakFakes = Math.Max(_peakFakes, _fakes.Count); RuntimeHearingProbe.Tick(); } }
}

using System.Collections;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Buffs;
using Duckov.Scenes;
using Duckov.Utilities;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Saves;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CoopRuntimeTests;

public static class Bootstrap
{
    private static bool _started;
    public static void Ensure()
    {
        if (_started || !Environment.GetCommandLineArgs().Any(a => a.StartsWith("--coop-test-role="))) return;
        _started = true;
        var go = new GameObject("CoopRuntimeTests");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Runner>();
    }
}

public sealed class Runner : MonoBehaviour
{
    private string _role, _root;
    private float _nextState;
    private bool _busy;
    private Assembly _mod;
    private int _fixtureSerial;
    private bool _blendTraceInstalled;
    private static int _blockedInputUpdates, _blockedCursorWarps;
    private int _localKillEvents;
    private readonly JsonSerializerSettings _json = new() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
    private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private Type MT(string name) => _mod.GetType("EscapeFromDuckovCoopMod." + name, true);
    private object Service => _mod == null ? null : MT("NetService").GetField("Instance", Flags).GetValue(null);
    private object Get(object instance, string name) => instance?.GetType().GetField(name, Flags)?.GetValue(instance) ?? instance?.GetType().GetProperty(name, Flags)?.GetValue(instance);
    private void Set(object instance, string name, object value)
    {
        var field = instance.GetType().GetField(name, Flags);
        if (field != null) field.SetValue(instance, value);
        else instance.GetType().GetProperty(name, Flags).SetValue(instance, value);
    }
    private void Awake()
    {
        _role = Environment.GetCommandLineArgs().Single(a => a.StartsWith("--coop-test-role=")).Split('=')[1];
        _root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        Directory.CreateDirectory(Path.Combine(_root, "commands"));
        Directory.CreateDirectory(Path.Combine(_root, "results"));
        Console.SetOut(new RuntimeConsole(Console.Out,Path.Combine(_root,"console-errors.jsonl")));
        Health.OnDead+=CountLocalKill;
        Debug.unityLogger.logHandler=new RuntimeLog(Debug.unityLogger.logHandler,Path.Combine(_root,"exceptions.jsonl"));
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        if (Application.isBatchMode)
        {
            if (UnityEngine.InputSystem.Keyboard.current==null) UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            if (UnityEngine.InputSystem.Mouse.current==null) UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
        }
        var harmony = new Harmony("local.coop.runtime-tests");
        UnityEngine.InputSystem.InputSystem.settings.updateMode=UnityEngine.InputSystem.InputSettings.UpdateMode.ProcessEventsManually;
        harmony.Patch(AccessTools.Method(typeof(CharacterInputControl), "Update"),
            prefix: new HarmonyMethod(typeof(Runner), nameof(BlockTestInput)));
        harmony.Patch(AccessTools.Method(typeof(CharacterInputControl), "RegisterEvents"),
            prefix: new HarmonyMethod(typeof(Runner), nameof(BlockTestInput)));
        harmony.Patch(AccessTools.Method(typeof(InputManager), "Update"),
            prefix: new HarmonyMethod(typeof(Runner), nameof(BlockTestInput)));
        harmony.Patch(AccessTools.Method(typeof(UnityEngine.InputSystem.Mouse), "WarpCursorPosition"),
            prefix: new HarmonyMethod(typeof(Runner), nameof(BlockCursorWarp)));
        harmony.Patch(AccessTools.Method(typeof(InputManager), "UpdateCursor"),
            prefix: new HarmonyMethod(typeof(Runner), nameof(ReleaseTestCursor)));
        ReleaseTestCursor();
        harmony.Patch(AccessTools.Method(typeof(Duckov.Modding.ModManager), "ShouldActivateMod"),
            prefix: new HarmonyMethod(typeof(Runner), nameof(FilterMods)));
        var achievement = AccessTools.Method(typeof(SteamManager), "OnAchievementUnlocked");
        if (achievement != null) harmony.Patch(achievement, prefix: new HarmonyMethod(typeof(Runner), nameof(SkipAchievement)));
        Debug.Log($"[RuntimeTests] role={_role} isolatedPath={Application.persistentDataPath} save={SavesSystem.CurrentFilePath}");
    }
    private static bool SkipAchievement() => false;
    private static bool TestSharedIdentity(ref string __result)
    {
        __result = "private-test-" + Environment.GetCommandLineArgs().Single(a=>a.StartsWith("--coop-test-role=")).Split('=')[1];
        return false;
    }
    private void CountLocalKill(Health health,DamageInfo info)
    {
        if(info.fromCharacter==CharacterMainControl.Main && health.TryGetCharacter()!=CharacterMainControl.Main) _localKillEvents++;
    }
    private void OnDestroy() { Health.OnDead-=CountLocalKill; }
    private static bool BlockTestInput() { _blockedInputUpdates++; return false; }
    private static bool BlockCursorWarp() { _blockedCursorWarps++; return false; }
    private static void TraceBlendInitialization(Component __instance)
    {
        var initialized=(bool)AccessTools.Field(__instance.GetType(),"_isInitialized").GetValue(__instance);
        bool Valid(string field)=>UnityEngine.Playables.PlayableExtensions.IsValid((UnityEngine.Animations.AnimationScriptPlayable)AccessTools.Field(__instance.GetType(),field).GetValue(__instance));
        var pose=Valid("_poseJobPlayable");var overlay=Valid("_overlayJobPlayable");var layer=Valid("_layeringJobPlayable");
        if (initialized && pose && overlay && layer) return;
        var character=__instance.GetComponentInParent<CharacterMainControl>();
        File.AppendAllText(Path.Combine(Path.GetDirectoryName(Application.persistentDataPath),"animation-trace.jsonl"),
            JsonConvert.SerializeObject(new { time=Time.realtimeSinceStartup,name=__instance.name,initialized,pose,overlay,layer,
                character=character?.GetInstanceID(),main=character?.IsMainCharacter })+"\n");
    }
    private static bool ReleaseTestCursor()
    {
        Cursor.lockState=CursorLockMode.None;
        Cursor.visible=true;
        return false;
    }
    private static bool FilterMods(Duckov.Modding.ModInfo info, ref bool __result)
    {
        __result = !Environment.GetCommandLineArgs().Contains("--coop-test-no-mod") && info.name == "EscapeFromDuckovCoopMod";
        return false;
    }

    private void Update()
    {
        if (!_blendTraceInstalled)
        {
            var blending=AccessTools.TypeByName("KINEMATION.MagicBlend.Runtime.MagicBlending");
            if (blending!=null)
            {
                new Harmony("local.coop.runtime-tests.animation").Patch(AccessTools.Method(blending,"UpdateMagicBlendAsset"),
                    prefix:new HarmonyMethod(typeof(Runner),nameof(TraceBlendInitialization)));
                _blendTraceInstalled=true;
                Debug.Log("[RuntimeTests] Native animation trace installed");
            }
        }
        if (SceneLoader.IsSceneLoading) SceneLoader.Instance.NotifyPointerClick(null);
        if (Time.unscaledTime >= _nextState)
        {
            _nextState = Time.unscaledTime + .2f;
            try { Write(Path.Combine(_root, "state.json"), State()); }
            catch (Exception error) { Debug.LogWarning("[RuntimeTests] state " + error.Message); }
        }
        if (_busy) return;
        var command = Directory.GetFiles(Path.Combine(_root, "commands"), "*.json").OrderBy(p => p).FirstOrDefault();
        if (command != null) Execute(command).Forget();
    }

    private object CharacterState(CharacterMainControl c) => c == null ? null : new
    {
        name = c.name, id = c.GetInstanceID(), hp = c.Health.CurrentHealth, max = c.Health.MaxHealth, dead = c.Health.IsDead,
        heldItem=c.agentHolder && c.agentHolder.CurrentHoldItemAgent && c.agentHolder.CurrentHoldItemAgent.Item
            ?c.agentHolder.CurrentHoldItemAgent.Item.TypeID:0,
        position = new[] { c.transform.position.x, c.transform.position.y, c.transform.position.z },
        buffs = c.GetBuffManager()?.Buffs.Where(b => b).Select(b => new { id = b.ID, layers = b.CurrentLayers, remaining = b.RemainingTime }).ToArray()
    };

    private CharacterMainControl Target(JObject command)
    {
        if(command["player"]!=null && command["ai"]==null)return RuntimeCombat.RemotePlayer(Service,(string)command["player"]);
        if (command["ai"] == null) return CharacterMainControl.Main;
        var id = (int)command["ai"];
        var registry = MT("CoopSyncDatabase").GetProperty("AI").GetValue(null);
        var entry = ((IEnumerable)Get(registry, "Entries")).Cast<object>().First(e => (int)Get(e, "Id") == id);
        if (Get(entry, "Controller") is AICharacterController controller && controller) return controller.CharacterMainControl;
        var manager = Get(MT("COOPManager").GetProperty("AI", Flags)?.GetValue(null), "_clientReplicas") as IDictionary;
        if (manager == null) manager = Get(MT("COOPManager").GetField("AI", Flags)?.GetValue(null), "_clientReplicas") as IDictionary;
        return Get(manager?[id], "Character") as CharacterMainControl ?? throw new InvalidOperationException("AI has no live replica");
    }

    private object ItemSummary(Item item) => item == null ? null : new
    {
        type = item.TypeID, count = item.StackCount, durability = item.Durability,
        children = ItemTreeData.FromItem(item).entries.Select(e => new { type = e.typeID, count = e.StackCount }).ToArray()
    };

    private Inventory LootInventory(int uid, int? key = null)
    {
        var registry = MT("CoopSyncDatabase").GetProperty("Loot").GetValue(null);
        var entry = ((IEnumerable)Get(registry, "Entries")).Cast<object>().First(e => key.HasValue ? (int)Get(e,"PositionKey")==key.Value : (int)Get(e, "LootUid") == uid);
        return Get(entry, "Inventory") as Inventory;
    }

    private object State()
    {
        var service = Service;
        var remotes = new List<object>();
        if (service != null)
        {
            var dictionary = Get(service, (bool)Get(service, "IsServer") ? "remoteCharacters" : "clientRemoteCharacters") as IDictionary;
            if (dictionary != null) foreach (DictionaryEntry entry in dictionary)
                if (entry.Value is GameObject go && go)
                {
                    var character=go.GetComponent<CharacterMainControl>();
                    var args=new object[]{character,null};
                    var identified=(bool)MT("NetService").GetMethod("TryGetPlayerId").Invoke(service,args);
                    remotes.Add(new { key=identified?(string)args[1]:entry.Key.ToString(),character=CharacterState(character) });
                }
        }
        return new
        {
            role = _role, time = Time.realtimeSinceStartup, scene = SceneManager.GetActiveScene().name,
            mainScene=MultiSceneCore.MainScene?.name,
            sceneIndex = SceneManager.GetActiveScene().buildIndex, initialized = LevelManager.LevelInited,
            raid = LevelManager.Instance != null && LevelManager.Instance.IsRaidMap, steam = SteamManager.Initialized,
            steamId = SteamManager.Initialized ? SteamUser.GetSteamID().ToString() : null,
            save = SavesSystem.CurrentFilePath, mod = _mod?.Location,
            network = Get(service, "networkStarted"), server = Get(service, "IsServer"), status = Get(service, "status"),
            peers = Get(Get(service, "netManager"), "ConnectedPeersCount"), local = CharacterState(CharacterMainControl.Main), remotes,
            playerId=Get(Get(service,"localPlayerStatus"),"EndPoint"),
            experience=EXPManager.EXP,
            localKillEvents=_localKillEvents,
            transport = Get(service, "TransportMode")?.ToString(), manager = Get(service, "netManager")?.GetType().Name,
            modInstances = _mod == null ? 0 : Resources.FindObjectsOfTypeAll(MT("NetService")).Length,
            coopAssemblyLoaded=AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="EscapeFromDuckovCoopMod"),
            closure = Duckov.UI.ClosureView.Instance ? new { canContinue=Get(Duckov.UI.ClosureView.Instance,"canContinue") } : null,
            loading=SceneLoader.IsSceneLoading,loadingComment=SceneLoader.LoadingComment,
            blackScreen=Get(Get(Duckov.UI.BlackScreen.Instance,"fadeGroup"),"IsShown"),
            cursor=Cursor.lockState.ToString(), cursorVisible=Cursor.visible,
            inputUpdatesBlocked=_blockedInputUpdates, cursorWarpsBlocked=_blockedCursorWarps,
            batchMode=Application.isBatchMode, graphics=SystemInfo.graphicsDeviceType.ToString(),
            fps = 1f / Time.unscaledDeltaTime, frameCap = Application.targetFrameRate, timeScale = Time.timeScale
        };
    }

    private async UniTask Execute(string path)
    {
        _busy = true;
        var id = Path.GetFileNameWithoutExtension(path);
        try
        {
            var command = JObject.Parse(File.ReadAllText(path));
            object result = null;
            var action = (string)command["action"];
            switch (action)
            {
                case "loadMod":
                    _mod = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "EscapeFromDuckovCoopMod") ??
                        Assembly.LoadFrom(Path.Combine(Application.dataPath, "Mods", "3591341282", "EscapeFromDuckovCoopMod.dll"));
                    var modDeadline = Time.unscaledTime + 20;
                    await UniTask.WaitUntil(() => Service != null || Time.unscaledTime >= modDeadline);
                    if (Service == null) new GameObject("CoopTestMod").AddComponent(MT("ModBehaviour"));
                    result = _mod.Location;
                    break;
                case "loadBase":
                    await UniTask.WaitUntil(()=>Resources.FindObjectsOfTypeAll<Title>().All(t=>!t || !t.gameObject.scene.IsValid() || !t.gameObject.activeInHierarchy ||
                        Get(Get(t,"fadeGroup"),"IsShown") is true)).Timeout(TimeSpan.FromSeconds(20));
                    await UniTask.NextFrame();
                    await SceneLoader.Instance.LoadBaseScene();
                    result = State();
                    break;
                case "host":
                    Set(Service, "TransportMode", Enum.Parse(MT("NetworkTransportMode"), (string)command["transport"]));
                    Set(Service, "port", (int?)command["port"] ?? 19050);
                    MT("NetService").GetMethod("StartNetwork").Invoke(Service, new object[] { true, false });
                    result = State();
                    break;
                case "join":
                    MT("NetService").GetMethod("StopNetwork").Invoke(Service, new object[] { true });
                    Set(Service, "TransportMode", Enum.Parse(MT("NetworkTransportMode"), (string)command["transport"]));
                    MT("NetService").GetMethod("StartNetwork").Invoke(Service, new object[] { false, false });
                    MT("NetService").GetMethod("ConnectToHost").Invoke(Service, new object[] { "127.0.0.1", (int?)command["port"] ?? 19050 });
                    result = State();
                    break;
                case "joinSteam":
                    MT("NetService").GetMethod("ConnectToSteamHost").Invoke(Service, new object[] { new CSteamID(ulong.Parse((string)command["steamId"])) });
                    result = State();
                    break;
                case "buffCatalog":
                    result = ((IEnumerable<Buff>)AccessTools.Field(typeof(GameplayDataSettings.BuffsData), "allBuffs").GetValue(GameplayDataSettings.Buffs))
                        .Where(b => b).Select(b => new { id = b.ID, name = b.name, layers = b.MaxLayers, duration = b.TotalLifeTime, timed = b.LimitedLifeTime, tag = b.ExclusiveTag.ToString() }).ToArray();
                    break;
                case "addBuff":
                    var prefab = ((IEnumerable<Buff>)AccessTools.Field(typeof(GameplayDataSettings.BuffsData), "allBuffs").GetValue(GameplayDataSettings.Buffs)).First(b => b && b.ID == (int)command["id"]);
                    Target(command).AddBuff(prefab, CharacterMainControl.Main);
                    result = CharacterState(Target(command));
                    break;
                case "removeBuff":
                    Target(command).RemoveBuff((int)command["id"], (bool?)command["one"] ?? false);
                    result = CharacterState(Target(command));
                    break;
                case "clearBuffs":
                    foreach (var buff in CharacterMainControl.Main.GetBuffManager().Buffs.ToArray()) if (buff) CharacterMainControl.Main.RemoveBuff(buff.ID, false);
                    result = CharacterState(CharacterMainControl.Main);
                    break;
                case "invincible": Target(command).Health.SetInvincible((bool)command["enabled"]); break;
                case "heal": Target(command).Health.SetHealth((float?)command["hp"] ?? 100); result = CharacterState(Target(command)); break;
                case "aiProfile": result=RuntimeAISyncProfile.Run(_mod,command); break;
                case "aiStreamChecks": result=RuntimeAIStreamChecks.Run(_mod,(bool)Get(Service,"IsServer")); break;
                case "aiAgentLifetime": result=await RuntimeAIAgentLifetimeProbe.Run(_mod); break;
                case "combatProfileBegin": RuntimeCombatProfile.Begin(_mod); break;
                case "aiFireStress": result=await RuntimeAIFireStress.Run(_mod,command); break;
                case "combatProfileEnd": result=RuntimeCombatProfile.End(); break;
                case "hitMarkerLifecycle": result=await RuntimeCombatProfile.MarkerLifecycle(_mod); break;
                case "combatHitStream": result=await RuntimeCombatProfile.HitStream(Target(command),(float)command["seconds"],(float)command["rate"]); break;
                case "combatGunStream": result=await RuntimeCombatProfile.GunStream(Target(command),(float)command["seconds"]); break;
                case "combatTargetHealth":
                    var combatTarget=Target(command);
                    combatTarget.CharacterItem.GetStat("MaxHealth".GetHashCode()).BaseValue=(float)command["hp"];
                    combatTarget.Health.SetHealth((float)command["hp"]);
                    combatTarget.Health.SetInvincible(false);
                    result=CharacterState(combatTarget);
                    break;
                case "damage":
                    var damage = new DamageInfo(CharacterMainControl.Main)
                    {
                        damageValue = (float?)command["amount"] ?? 10, ignoreArmor = (bool?)command["ignoreArmor"] ?? true,
                        ignoreDifficulty = (bool?)command["ignoreDifficulty"] ?? true, isExplosion = (bool?)command["explosion"] ?? false,
                        critRate=(float?)command["critRate"] ?? 0, critDamageFactor=(float?)command["critFactor"] ?? 1,
                        armorPiercing=(float?)command["piercing"] ?? 0, bleedChance=(float?)command["bleedChance"] ?? 0,
                        damageType = (DamageTypes)((int?)command["type"] ?? 0), damagePoint = CharacterMainControl.Main.transform.position,
                        damageNormal = Vector3.up
                    };
                    if (command["buff"] != null)
                    {
                        damage.buff=((IEnumerable<Buff>)AccessTools.Field(typeof(GameplayDataSettings.BuffsData),"allBuffs").GetValue(GameplayDataSettings.Buffs))
                            .First(b=>b && b.ID==(int)command["buff"]);
                        damage.buffChance=1;
                    }
                    if (command["element"] != null) damage.AddElementFactor((ElementTypes)(int)command["element"], 1);
                    var damageTarget = Target(command);
                    damage.toDamageReceiver = damageTarget.mainDamageReceiver;
                    damage.damagePoint = damageTarget.transform.position;
                    var before = damageTarget.Health.CurrentHealth;
                    if((bool?)command["receiver"]==true)damageTarget.mainDamageReceiver.Hurt(damage);
                    else damageTarget.Health.Hurt(damage);
                    result = new { before, after = damageTarget.Health.CurrentHealth, local = CharacterState(damageTarget) };
                    break;
                case "explosion":
                    var explosionTarget = Target(command);
                    var explosionDamage = new DamageInfo(CharacterMainControl.Main)
                    { damageValue = (float?)command["amount"] ?? 10, ignoreArmor = true, ignoreDifficulty = true, damageType = DamageTypes.normal };
                    var explosionBefore = explosionTarget.Health.CurrentHealth;
                    LevelManager.Instance.ExplosionManager.CreateExplosion(explosionTarget.transform.position, (float?)command["radius"] ?? 3,
                        explosionDamage, ExplosionFxTypes.normal, 0, true);
                    result = new { before = explosionBefore, after = explosionTarget.Health.CurrentHealth };
                    break;
                case "expireBuff":
                    var expiring = Target(command).GetBuffManager().Buffs.FirstOrDefault(b => b && b.ID == (int)command["id"]);
                    if (!expiring) { result=new { expired=false,reason="native_already_removed" }; break; }
                    AccessTools.Field(typeof(Buff), "timeWhenStarted").SetValue(expiring, Time.time - expiring.TotalLifeTime - .1f);
                    result=new { expired=true };
                    break;
                case "coldStep":
                    var coldBefore=TimeOfDayController.coldLevel;
                    try
                    {
                        TimeOfDayController.coldLevel=(float)command["level"];
                        for (var tick=0;tick<((int?)command["ticks"] ?? 1);tick++)
                        {
                            AccessTools.Field(typeof(CharacterMainControl),"coldTickTimer").SetValue(CharacterMainControl.Main,0f);
                            CharacterMainControl.Main.UpdateCold();
                        }
                        result=CharacterState(CharacterMainControl.Main);
                    }
                    finally { TimeOfDayController.coldLevel=coldBefore; }
                    break;
                case "aiSnapshot":
                    var aiRegistry = MT("CoopSyncDatabase").GetProperty("AI").GetValue(null);
                    var aiRows = new List<object>();
                    foreach (var entry in (IEnumerable)Get(aiRegistry, "Entries"))
                    {
                        CharacterMainControl aiCharacter = null;
                        try { aiCharacter = Target(new JObject { ["ai"] = (int)Get(entry, "Id") }); } catch { }
                        aiRows.Add(new { id = Get(entry, "Id"), preset = Get(entry, "CharacterPresetKey"), hp = Get(entry, "CurrentHealth"),
                            max = Get(entry, "MaxHealth"), status = Get(entry, "Status")?.ToString(), character = CharacterState(aiCharacter) });
                    }
                    result = aiRows;
                    break;
                case "aiAttack":
                    result=RuntimeCombat.AiAttack(command,Target(command),(bool?)command["remote"]==true?
                        RuntimeCombat.RemotePlayer(Service,(string)command["player"]):CharacterMainControl.Main);
                    break;
                case "aiExplosion":
                    var explosionVictim=(bool?)command["remote"]==true?RuntimeCombat.RemotePlayer(Service,(string)command["player"]):CharacterMainControl.Main;
                    var enemyExplosion=new DamageInfo(Target(command)) { damageValue=10,damageType=DamageTypes.normal,ignoreArmor=false };
                    var victimBefore=explosionVictim.Health.CurrentHealth;
                    LevelManager.Instance.ExplosionManager.CreateExplosion(explosionVictim.transform.position,3,enemyExplosion,ExplosionFxTypes.normal,0,true);
                    result=new {before=victimBefore,after=explosionVictim.Health.CurrentHealth};
                    break;
                case "pauseAI":
                    var pauseRegistry=MT("CoopSyncDatabase").GetProperty("AI").GetValue(null);
                    var paused=0;
                    foreach(var row in (IEnumerable)Get(pauseRegistry,"Entries"))
                    {
                        if(command["ai"]!=null && (int)Get(row,"Id")!=(int)command["ai"])continue;
                        if(Get(row,"Controller") is AICharacterController ai && ai)
                            paused+=RuntimeCombat.PauseBrain(ai.CharacterMainControl,(bool?)command["enabled"] ?? true);
                    }
                    result=paused;
                    break;
                case "fakeExplosion": result=RuntimeCombat.FakeExplosion(_mod); break;
                case "animationProbe": result=await RuntimeAnimation.Probe(_mod); break;
                case "exitCatalog": result=RuntimeExits.Catalog(); break;
                case "worldSnapshot": result=RuntimeWorld.Snapshot(_mod); break;
                case "worldCatalog": result=RuntimeWorldInteractions.Catalog(_mod); break;
                case "worldAction": result=RuntimeWorldInteractions.Act(_mod,command); break;
                case "blackMarket": result=RuntimeBlackMarketProbe.Run(command); break;
                case "sharedHome": result=await RuntimeSharedHomeProbe.Run(_mod,command); break;
                case "sharedEntry": result=RuntimeSharedEntryProbe.Run(_mod,command); break;
                case "sharedStorage": result=await RuntimeSharedStorageProbe.Run(_mod,command); break;
                case "sharedStorageLoading": result=await RuntimeSharedStorageLoadingProbe.Run(_mod,command); break;
                case "sharedCharacter": result=RuntimeSharedCharacterProbe.Run(_mod,command); break;
                case "faceState": result=RuntimeFaceStateProbe.Run(_mod); break;
                case "modeSelection": result=await RuntimeModeSelectionProbe.Run(_mod,command); break;
                case "localization": result=await RuntimeLocalizationProbe.Run(_mod,command); break;
                case "joinFeedback": result=await RuntimeJoinFeedbackProbe.Run(_mod,command); break;
                case "appearanceInteraction": result=await RuntimeAppearanceInteractionProbe.Run(_mod,command); break;
                case "sessionUI": result=await RuntimeSessionUIProbe.Run(_mod,command); break;
                case "aiAudit": result=RuntimeAIAuditProbe.Run(_mod,command); break;
                case "gunLifetime": result=await RuntimeGunLifetimeProbe.Run(); break;
                case "nativeScenery": result=await RuntimeNativeSceneryProbe.Run(command); break;
                case "regions": result=await RuntimeRegionProbe.Run(command); break;
                case "mapSurvey": result=await RuntimeMapSurvey.Run(command); break;
                case "steamMode": result=await RuntimeSteamModeProbe.Run(_mod,command); break;
                case "interactionCatalog": result=RuntimeInteractionProbe.Catalog(); break;
                case "lootInspection": result=RuntimeInteractionProbe.Loot(_mod,(int?)command["uid"]); break;
                case "constructionProbe": result=RuntimeInteractionProbe.Construction(command); break;
                case "nativeInteraction": result=RuntimeInteractionProbe.Act(_mod,command); break;
                case "lootPut": result=await RuntimeInteractionProbe.Put(_mod,command); break;
                case "dropItem":
                    var dropped=await ItemAssetsCollection.InstantiateAsync((int)command["id"]);
                    dropped.StackCount=(int?)command["count"] ?? 1;
                    CharacterMainControl.Main.CharacterItem.Inventory.AddItem(dropped);
                    if(dropped.InInventory!=CharacterMainControl.Main.CharacterItem.Inventory)throw new InvalidOperationException("No inventory space for native drop test");
                    dropped.Drop(CharacterMainControl.Main.transform.position+Vector3.forward*2,true,Vector3.forward,360);
                    result=ItemSummary(dropped);
                    break;
                case "pickupDrop":
                    var dropRegistry=MT("CoopSyncDatabase").GetProperty("Drops").GetValue(null);
                    var dropEntry=((IEnumerable)Get(dropRegistry,"Entries")).Cast<object>().First(e=>(uint)Get(e,"DropId")== (uint)command["id"]);
                    result=CharacterMainControl.Main.PickupItem((Item)Get(dropEntry,"Item"));
                    break;
                case "friendlyFire":
                    var friendlyFire=MT("COOPManager").GetField("FriendlyFire",Flags).GetValue(null);
                    if(command["enabled"]!=null)friendlyFire.GetType().GetMethod("UpdateState").Invoke(friendlyFire,new object[]{(bool)command["enabled"],true});
                    result=Get(friendlyFire,"FriendlyFirePlayersEnabled");
                    break;
                case "teleporterCatalog":
                    result=Object.FindObjectsByType<MultiSceneTeleporter>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                        .Select(t=>new {id=t.GetInstanceID(),name=t.name,target=t.Target.SceneID,location=t.Target.LocationName,
                            position=new[]{t.transform.position.x,t.transform.position.y,t.transform.position.z}}).ToArray();
                    break;
                case "teleport":
                    var teleporter=Object.FindObjectsByType<MultiSceneTeleporter>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                        .Single(t=>t.GetInstanceID()==(int)command["id"]);
                    teleporter.DoTeleport();
                    break;
                case "enterExit": result=RuntimeExits.Enter((int)command["id"]); break;
                case "subSceneCatalog":
                    result=MultiSceneCore.Instance.SubScenes.Select(e=>new {id=e.sceneID,index=e.Info.BuildIndex,
                        locations=e.cachedLocations.Select(l=>new {l.path,position=new[]{l.position.x,l.position.y,l.position.z}}).ToArray()}).ToArray();
                    break;
                case "subScene":
                    result=await MultiSceneCore.Instance.LoadAndTeleport((string)command["scene"],
                        new Vector3((float)command["x"],(float)command["y"],(float)command["z"]),true);
                    break;
                case "sceneVote":
                    var sceneNet=MT("SceneNet").GetField("Instance",Flags).GetValue(null);
                    result=new {active=Get(sceneNet,"sceneVoteActive"),target=Get(sceneNet,"sceneTargetId"),
                        ready=Get(sceneNet,"sceneReady"),participants=Get(sceneNet,"sceneParticipantIds")};
                    break;
                case "transportDiagnostics": result=RuntimeTransport.Snapshot(Service); break;
                case "packetProfile": result=RuntimePacketProfile.Run(_mod,(bool?)command["reset"]==true); break;
                case "snapshotBacklog": result=RuntimeSnapshotBacklogProbe.Run(_mod,command); break;
                case "ready":
                    var voting=MT("SceneNet").GetField("Instance",Flags).GetValue(null);
                    if((bool)Get(Service,"IsServer"))MT("SceneNet").GetMethod("Server_OnSceneReadySet").Invoke(voting,new object[]{null,true});
                    else MT("SceneNet").GetMethod("Client_SendReadySet").Invoke(voting,new object[]{true});
                    break;
                case "sceneCatalog": result = SceneInfoCollection.Entries.Select(e => new { id = e.ID, name = e.DisplayName, index = e.BuildIndex }).ToArray(); break;
                case "inventory": result = ItemTreeData.FromItem(CharacterMainControl.Main.CharacterItem).entries.Select(e => new { type=e.typeID,count=e.StackCount }).ToArray(); break;
                case "give":
                    var given = await ItemAssetsCollection.InstantiateAsync((int)command["id"]);
                    given.StackCount = (int?)command["count"] ?? 1;
                    if (command["durability"] != null) given.Durability = (float)command["durability"];
                    result = new { item=ItemSummary(given), picked=CharacterMainControl.Main.PickupItem(given) };
                    break;
                case "prepareGun":
                    var gunItem=await ItemAssetsCollection.InstantiateAsync((int?)command["id"] ?? 240);
                    var ammo=await ItemAssetsCollection.InstantiateAsync((int?)command["ammo"] ?? 140);
                    ammo.StackCount=90;
                    var setting=gunItem.GetComponent<ItemSetting_Gun>();
                    if (!setting.IsValidBullet(ammo)) throw new InvalidOperationException("Wrong ammunition for native gun");
                    if (!CharacterMainControl.Main.PickupItem(gunItem)) throw new InvalidOperationException("Cannot equip gun");
                    CharacterMainControl.Main.PickupItem(ammo);
                    CharacterMainControl.Main.ChangeHoldItem(gunItem);
                    await UniTask.Delay(300);
                    CharacterMainControl.Main.TryToReload(ammo);
                    await UniTask.WaitUntil(()=>setting.BulletCount>0).Timeout(TimeSpan.FromSeconds(15));
                    result=new { item=ItemSummary(gunItem), bullets=setting.BulletCount, damage=CharacterMainControl.Main.GetGun()?.Damage };
                    break;
                case "fire":
                case "aiFire":
                    var shooter=action=="aiFire"?Target(command):CharacterMainControl.Main;
                    var firingGun=shooter.GetGun();
                    if (!firingGun) throw new InvalidOperationException("No held gun");
                    var victim=action=="aiFire"?((bool?)command["remote"]==true?RuntimeCombat.RemotePlayer(Service,(string)command["player"]):CharacterMainControl.Main):Target(command);
                    var hitCollider=victim.mainDamageReceiver.GetComponent<Collider>();
                    var aim=hitCollider?hitCollider.bounds.center:victim.transform.position+Vector3.up*.5f;
                    shooter.SetAimPoint(aim);
                    firingGun.OverrideShootPoint(aim);
                    var bulletsBefore=firingGun.GunItemSetting.BulletCount;
                    if((bool?)command["probeOnly"]!=true)AccessTools.Method(typeof(ItemAgent_Gun),"TransToFire").Invoke(firingGun,new object[]{true});
                    var rayOrigin=firingGun.muzzle.position;
                    var shot=(bool?)command["probeOnly"]==true?null:(Projectile)AccessTools.Field(typeof(ItemAgent_Gun),"projInst").GetValue(firingGun);
                    result=new { before=bulletsBefore, after=firingGun.GunItemSetting.BulletCount,origin=rayOrigin,aim,target=victim.GetInstanceID(),
                        projectile=RuntimeCombat.Trace(shot,shooter,_mod),
                        hits=Physics.RaycastAll(rayOrigin,(aim-rayOrigin).normalized,Vector3.Distance(rayOrigin,aim)+1,-1,QueryTriggerInteraction.Ignore)
                            .OrderBy(h=>h.distance).Select(h=>new {name=h.collider.name,h.distance,layer=h.collider.gameObject.layer,
                                receiver=h.collider.GetComponent<DamageReceiver>()?.name,character=h.collider.GetComponentInParent<CharacterMainControl>()?.GetInstanceID(),point=h.point}).ToArray() };
                    break;
                case "combatInfo":
                    var combatant=Target(command);
                    result=new {team=combatant.Team.ToString(),invincible=combatant.Health.Invincible,gun=combatant.GetGun()?.Item.TypeID,bullets=combatant.GetGun()?.GunItemSetting.BulletCount,
                        weapons=combatant.CharacterItem.Slots.Where(s=>s.Content && s.Content.GetComponent<ItemSetting_Gun>()).Select(s=>new {s.Key,s.Content.TypeID}).ToArray(),
                        bodyArmor=combatant.Health.BodyArmor,headArmor=combatant.Health.HeadArmor,
                        colliders=combatant.GetComponentsInChildren<Collider>(true).Select(c=>new {name=c.name,c.enabled,c.isTrigger,
                            layer=c.gameObject.layer,center=c.bounds.center,size=c.bounds.size,receiver=c.GetComponent<DamageReceiver>()?.name}).ToArray()};
                    break;
                case "itemInfo":
                    var inspected = await ItemAssetsCollection.InstantiateAsync((int)command["id"]);
                    result = new { item=ItemSummary(inspected), capacity=inspected.Inventory?.Capacity,
                        slots=inspected.Slots?.Select(s=>new{key=s.Key,content=ItemSummary(s.Content)}).ToArray() };
                    Object.Destroy(inspected.gameObject);
                    break;
                case "nativeCorpses":
                    result = ((List<DeadBodyManager.DeathInfo>)AccessTools.Field(typeof(DeadBodyManager),"deaths").GetValue(DeadBodyManager.Instance))
                        .Select(i=>new { id=$"{SavesSystem.CurrentSlot}:{i.raidID}:{i.itemTreeData.rootInstanceID}",i.valid,i.touched,i.spawned,i.subSceneID }).ToArray();
                    break;
                case "nativeSpawnCorpse":
                    var nativeRecords=(List<DeadBodyManager.DeathInfo>)AccessTools.Field(typeof(DeadBodyManager),"deaths").GetValue(DeadBodyManager.Instance);
                    var nativeInfo=nativeRecords.First(i=>$"{SavesSystem.CurrentSlot}:{i.raidID}:{i.itemTreeData.rootInstanceID}"==(string)command["id"]);
                    await (UniTask)AccessTools.Method(typeof(DeadBodyManager),"SpawnDeadBody").Invoke(DeadBodyManager.Instance,new object[]{nativeInfo});
                    break;
                case "itemCatalog": result = Enumerable.Range(1,4000).Select(ItemAssetsCollection.GetMetaData).Where(m=>m.id>0)
                    .Select(m=>new{id=m.id,name=m.DisplayName}).ToArray(); break;
                case "safeMove":result=RuntimePlacement.Move(command);break;
                case "move":
                    CharacterMainControl.Main.SetPosition(new Vector3((float)command["x"], (float)command["y"], (float)command["z"]));
                    result = CharacterState(CharacterMainControl.Main);
                    break;
                case "spectator":
                    var spectator = MT("Spectator").GetField("Instance", Flags).GetValue(null);
                    result = new { active=Get(spectator,"_spectatorActive"), camera=CharacterState(GameCamera.Instance?.target),
                        candidates=((IEnumerable)Get(spectator,"_spectateList")).Cast<CharacterMainControl>().Select(c=>CharacterState(c)).ToArray(),
                        ownerActive=CharacterMainControl.Main.gameObject.activeInHierarchy,
                        controlling=CharacterState(LevelManager.Instance.ControllingCharacter), main=CharacterState(CharacterMainControl.Main) };
                    break;
                case "endSpectator":
                    var spectate = MT("Spectator").GetField("Instance", Flags).GetValue(null);
                    MT("Spectator").GetMethod("EndSpectatorAndShowClosure").Invoke(spectate, new object[] { true });
                    break;
                case "continue": AccessTools.Method(typeof(Duckov.UI.ClosureView), "OnContinueButtonClicked").Invoke(Duckov.UI.ClosureView.Instance, null); break;
                case "corpseFixture": result = await CorpseFixture(command); break;
                case "corpseInventory":
                case "worldInventories":
                    var lootRegistry = MT("CoopSyncDatabase").GetProperty("Loot").GetValue(null);
                    result = ((IEnumerable)Get(lootRegistry, "Entries")).Cast<object>().Where(e => (action=="worldInventories" || (int)Get(e,"LootUid") >= 0) && Get(e,"Inventory") is Inventory inv && inv)
                        .Select(e => new { uid=Get(e,"LootUid"),key=Get(e,"PositionKey"),scene=Get(e,"SceneIndex"),box=(Get(e,"Lootbox") as InteractableLootbox)?.name,
                            items=(Get(e,"Inventory") as Inventory).Select(i=>ItemSummary(i)).ToArray(),
                            slots=Enumerable.Range(0,(Get(e,"Inventory") as Inventory).Capacity).Where(i=>(Get(e,"Inventory") as Inventory).GetItemAt(i))
                                .Select(i=>new {slot=i,item=ItemSummary((Get(e,"Inventory") as Inventory).GetItemAt(i))}).ToArray() }).ToArray();
                    break;
                case "destroyCorpse": Object.Destroy(LootInventory((int?)command["uid"] ?? -1,(int?)command["key"]).gameObject); break;
                case "openCorpse":
                    var openInv = LootInventory((int?)command["uid"] ?? -1,(int?)command["key"]);
                    var lootNet = MT("COOPManager").GetProperty("LootNet", Flags)?.GetValue(null) ?? MT("COOPManager").GetField("LootNet",Flags).GetValue(null);
                    lootNet.GetType().GetMethod("Client_SetLootActive").Invoke(lootNet, new object[] { openInv, true });
                    lootNet.GetType().GetMethod("Client_RequestLootState").Invoke(lootNet,new object[]{openInv,false});
                    break;
                case "takeCorpse":
                    var takeInv = LootInventory((int?)command["uid"] ?? -1,(int?)command["key"]);
                    if (!takeInv.RemoveAt((int)command["slot"], out var taken)) throw new InvalidOperationException("No corpse item in slot");
                    result = new { taken=ItemSummary(taken), picked=CharacterMainControl.Main.PickupItem(taken) };
                    break;
                case "splitCorpse":
                    var splitInv = LootInventory((int?)command["uid"] ?? -1,(int?)command["key"]);
                    var splitItem = splitInv.GetItemAt((int)command["slot"]);
                    var splitCount = (int)command["count"];
                    var splitBefore = splitItem.StackCount;
                    splitItem.StackCount -= splitCount;
                    var splitOut = await ItemAssetsCollection.InstantiateAsync(splitItem.TypeID);
                    splitOut.StackCount = splitCount;
                    result = new { before=splitBefore, after=splitItem.StackCount, picked=CharacterMainControl.Main.PickupItem(splitOut) };
                    break;
                case "savedCorpses":
                    var saved = SavesSystem.Load<Dictionary<string,byte[]>>("CoopCorpseRemainders");
                    result = saved?.Select(kv=>new { id=kv.Key, entries=ES3.Deserialize<InventoryData>(kv.Value).entries.Select(e=>new
                        {slot=e.inventoryPosition,items=e.itemTreeData.entries.Select(t=>new{type=t.typeID,count=t.StackCount}).ToArray()}).ToArray() }).ToArray();
                    break;
                case "restoreSavedCorpse":
                    var restoreType = MT("CorpseRecovery");
                    var restoreRecords = (List<DeadBodyManager.DeathInfo>)AccessTools.Field(typeof(DeadBodyManager),"deaths").GetValue(DeadBodyManager.Instance);
                    var restoreInfo = restoreRecords.First(i=>(string)restoreType.GetMethod("RecordId",Flags).Invoke(null,new object[]{i})==(string)command["id"]);
                    var restoreArgs = new object[]{restoreInfo,default(UniTask)};
                    if (!(bool)restoreType.GetMethod("TryRestore",Flags).Invoke(null,restoreArgs)) throw new InvalidOperationException("No saved remainder");
                    await (UniTask)restoreArgs[1];
                    result = "restored";
                    break;
                case "invoke":
                    var target = (string)command["target"] == "health" ? (object)Target(command).Health : Target(command);
                    if (command["type"] != null)
                    {
                        var targetType = AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType((string)command["type"])).First(t=>t!=null);
                        target = targetType.GetProperty("Instance",Flags)?.GetValue(null) ?? targetType.GetField("Instance",Flags)?.GetValue(null);
                    }
                    var tokenArgs = (JArray)command["args"] ?? new JArray();
                    var method = target.GetType().GetMethods(Flags).First(m=>m.Name==(string)command["method"] && m.GetParameters().Length==tokenArgs.Count);
                    var callArgs = method.GetParameters().Select((p,i)=>tokenArgs[i].ToObject(p.ParameterType)).ToArray();
                    var called = method.Invoke(target,callArgs);
                    result = called is Object obj ? new {name=obj.name,id=obj.GetInstanceID()} : called;
                    break;
                case "loadScene":
                    await SceneLoader.Instance.LoadScene((string)command["scene"], overrideCurtainScene:null,
                        clickToConinue:false, notifyEvacuation:false, doCircleFade:false, useLocation:false, saveToFile:false);
                    result = State();
                    break;
                case "stop": MT("NetService").GetMethod("StopNetwork").Invoke(Service, new object[] { true }); break;
                case "quit": Application.Quit(); break;
                default: throw new InvalidOperationException("Unknown action " + action);
            }
            Write(Path.Combine(_root, "results", id + ".json"), new { ok = true, action, result });
        }
        catch (Exception error)
        {
            Write(Path.Combine(_root, "results", id + ".json"), new { ok = false, error = error.ToString() });
            Debug.LogError("[RuntimeTests] command " + error);
        }
        finally { File.Delete(path); _busy = false; }
    }

    private async UniTask<object> CorpseFixture(JObject command)
    {
        var copy = await ItemTreeData.InstantiateAsync(ItemTreeData.FromItem(CharacterMainControl.Main.CharacterItem));
        copy.Inventory.SetCapacity(512);
        if (command["items"] is JArray items)
            foreach (JObject row in items)
            {
                var item = await ItemAssetsCollection.InstantiateAsync((int)row["type"]);
                item.StackCount = (int?)row["count"] ?? 1;
                copy.Inventory.AddItem(item);
            }
        var info = new DeadBodyManager.DeathInfo { valid=true, raidID=(uint)(900000+(++_fixtureSerial)),
            subSceneID=MultiSceneCore.ActiveSubSceneID, worldPosition=CharacterMainControl.Main.transform.position+Vector3.right*3,
            itemTreeData=ItemTreeData.FromItem(copy) };
        var deaths = (List<DeadBodyManager.DeathInfo>)AccessTools.Field(typeof(DeadBodyManager),"deaths").GetValue(DeadBodyManager.Instance);
        deaths.Add(info);
        var recovery=MT("CorpseRecovery");
        var id=(string)recovery.GetMethod("RecordId",Flags).Invoke(null,new object[]{info});
        var box=InteractableLootbox.CreateFromItem(copy,info.worldPosition,Quaternion.identity,true,GameplayDataSettings.Prefabs.LootBoxPrefab_Tomb,true);
        if((bool)Get(Service,"IsServer")) recovery.GetMethod("RegisterServerBody",Flags).Invoke(null,new object[]{box,null,id});
        else
        {
            recovery.GetMethod("ReportStoredBody",Flags).Invoke(null,new object[]{id,info.worldPosition,ES3.Serialize(InventoryData.FromInventory(box.Inventory))});
            box.gameObject.SetActive(false); Object.Destroy(box.gameObject);
        }
        return new {id, items=box.Inventory.Select(i=>ItemSummary(i)).ToArray()};
    }

    private void Write(string path, object value)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(value, _json));
        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);
    }
}

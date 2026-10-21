using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using EscapeFromDuckovCoopMod;
using LiteNetLib.Utils;

var assertions = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    assertions++;
}
var soundWriter=new NetDataWriter();
new AISoundEventRpc {Position=new Vector3(1,2,3),Radius=40,Team=Teams.player,SoundType=SoundTypes.combatSound,SourceAiId=-123}.Serialize(soundWriter);
var soundBytes=soundWriter.CopyData();var soundReader=new NetDataReader(soundBytes);
var soundRoundTrip=new AISoundEventRpc();soundRoundTrip.Deserialize(soundReader);
Check(soundRoundTrip.SourceAiId==-123 && soundRoundTrip.Position.z==3 && soundRoundTrip.Radius==40,"AI hearing replay retains its native actor identity");
soundReader.SetSource(soundBytes.Take(soundBytes.Length-sizeof(int)).ToArray());soundRoundTrip.Deserialize(soundReader);
Check(soundRoundTrip.SourceAiId==0 && soundRoundTrip.SoundType==SoundTypes.combatSound,"legacy AI hearing packets remain readable");
soundReader.SetSource(soundBytes);
Check(soundReader.GetFloat()==1 && soundReader.GetFloat()==2 && soundReader.GetFloat()==3 && soundReader.GetFloat()==40 && soundReader.GetInt()==(int)Teams.player && soundReader.GetByte()==(byte)SoundTypes.combatSound && soundReader.AvailableBytes==sizeof(int),"AI hearing actor extension retains the old payload prefix");
var putWriter=new NetDataWriter();
var putReceipt=new LootPutOkRpc {Id=new LootIdentifier {Scene=10,PositionKey=20,InstanceId=30,LootUid=-1},Token=42,Slot=3,Stack=8,TypeId=140};
putReceipt.Serialize(putWriter);
var putBytes=putWriter.CopyData();
var putReader=new NetDataReader(putBytes);
var putRoundTrip=new LootPutOkRpc();putRoundTrip.Deserialize(putReader);
Check(putRoundTrip.Token==42 && putRoundTrip.Slot==3 && putRoundTrip.Stack==8 && putRoundTrip.TypeId==140,"placement receipt identifies every acknowledged stack");
putReader.SetSource(putBytes.Take(putBytes.Length-sizeof(int)).ToArray());
putRoundTrip=new LootPutOkRpc();putRoundTrip.Deserialize(putReader);
Check(putRoundTrip.TypeId==0 && putRoundTrip.Stack==8,"legacy placement acknowledgement remains readable");
putReader.SetSource(putBytes);
var legacyPutId=new LootIdentifier();legacyPutId.Deserialize(putReader);
Check(putReader.GetUInt()==42 && putReader.GetInt()==3 && putReader.GetInt()==8 && putReader.AvailableBytes==sizeof(int),"extended acknowledgement retains the legacy payload prefix");
object Invoke(Type type, string method, params object[] args) =>
    type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

SharedHomeManifest Manifest() => new()
{
    Game = "1.0.0", Coop = "1.4.0", Mods = new()
    {
        new() { Name = "A", Version = "1", Hash = new string('a', 64) },
        new() { Name = "B", Version = "2", Hash = string.Empty }
    }
};
var hostManifest = Manifest();
Check(new SharedHomeCharacterContext(typeof(NewCharacterContext),typeof(OldCharacterConfig)).Read(new NewCharacterContext()) == "MainCharacterItemData_Challenge",
    "new game character context uses its native property");
Check(new SharedHomeCharacterContext(typeof(PublicCharacterContext),typeof(OldCharacterConfig)).Read(new PublicCharacterContext()) == "MainCharacterItemData",
    "public game constant save key works without the newer getter");
ChallengeCharacterConfig.OverridePlayerItemSaveDataKeySuffix = "Cold";
Check(new SharedHomeCharacterContext(typeof(OldCharacterContext),typeof(ChallengeCharacterConfig)).Read(new OldCharacterContext()) == "MainCharacterItemData_Cold",
    "older context API retains an available native challenge suffix");
Check(new SharedHomeCharacterContext(typeof(OldCharacterContext),typeof(OldCharacterConfig)).Read(new OldCharacterContext()) == "MainCharacterItemData",
    "legacy game with no optional context API uses the native ordinary save key");
Check(new SharedHomeCharacterContext(typeof(NewCharacterContext),typeof(OldCharacterConfig)).Read(null) == "MainCharacterItemData",
    "main menu has no character context to dereference");
var guestManifest = Manifest();
guestManifest.Mods.Reverse();
Check(hostManifest.Compare(guestManifest).Count == 0, "enabled mod order does not cause false mismatch");
guestManifest.Game = "1.0.1";
guestManifest.Coop = "1.3.6";
Check(hostManifest.Compare(guestManifest).Select(i => i.Kind).SequenceEqual(new[] { "game", "coop" }), "both game and co-op version differences are reported");
guestManifest = Manifest();
guestManifest.Mods.RemoveAt(0);
guestManifest.Mods.Add(new() { Name = "C", Version = "3", Hash = "" });
Check(hostManifest.Compare(guestManifest).Select(i => i.Kind).SequenceEqual(new[] { "missing", "extra" }), "missing and extra enabled mods are reported together");
guestManifest = Manifest();
guestManifest.Mods[0].Version = "9";
guestManifest.Mods[0].Hash = new string('b', 64);
Check(hostManifest.Compare(guestManifest).Select(i => i.Kind).SequenceEqual(new[] { "version", "hash" }), "same mod can report version and DLL differences");
guestManifest = Manifest();
guestManifest.Mods[0].Hash = new string('A', 64);
Check(hostManifest.Compare(guestManifest).Count == 0, "hash formatting is case insensitive");
guestManifest.Mods[0].Unreadable = true;
Check(hostManifest.Compare(guestManifest).Single().Kind == "unreadable", "unreadable DLL cannot validate even with a matching cached hash");
guestManifest = Manifest();
guestManifest.Mods.Add(guestManifest.Mods[0]);
Check(hostManifest.Compare(guestManifest).Single().Kind == "invalid", "duplicate mod identities are rejected");
Check(hostManifest.Compare(null).Single().Kind == "invalid", "absent manifest is rejected");
guestManifest = Manifest();
guestManifest.Protocol = 0;
Check(hostManifest.Compare(guestManifest).Single().Kind == "protocol", "incompatible handshake protocol is rejected");
guestManifest.Game = "";
Check(hostManifest.Compare(guestManifest).Single().Kind == "invalid", "unknown game version cannot validate");

// A snapshot received first can sit in the work queue while newer reliable HP/death is applied.
var weaponOrder = new WeaponReplayOrder();
var firstGun = weaponOrder.Receive(1, 1, true);
var knife = weaponOrder.Receive(1, 2, true);
var lastGun = weaponOrder.Receive(1, 1, true);
Check(!weaponOrder.IsCurrent(1, firstGun) && !weaponOrder.IsCurrent(1, knife) && weaponOrder.IsCurrent(1, lastGun),
    "older asynchronous weapon mounts cannot override gun knife gun order");
weaponOrder.Receive(1, 2, false);
Check(weaponOrder.IsCurrent(1, lastGun), "empty unused melee snapshot does not cancel pending gun");
weaponOrder.Receive(1, 1, false);
Check(!weaponOrder.IsCurrent(1, lastGun), "clearing held socket invalidates pending item");
var otherGun = weaponOrder.Receive(2, 1, true);
weaponOrder.Receive(1, 2, true);
Check(weaponOrder.IsCurrent(2, otherGun), "weapon receipt order is independent per character");

var order = new HealthSnapshotOrder();
var constructionObject = new GameObject();
var freeSite = constructionObject.AddComponent<ConstructionSite>();
var freeTaker = constructionObject.AddComponent<CostTaker>();
Check(ConstructionSiteReplay.IsFreeConstructionCost(freeTaker), "free construction completion is eligible for replay");
Check(ConstructionSiteReplay.TryApply(freeTaker) && freeSite.Built && freeSite.BuiltCalls == 1, "free construction invokes its native completion once");
Check(ConstructionSiteReplay.TryApply(freeTaker) && freeSite.BuiltCalls == 1, "replayed construction completion is idempotent");
freeSite.SetOriginalCost(500);
freeTaker.CurrentCost = new Duckov.Economy.Cost { money = 0 };
Check(!ConstructionSiteReplay.IsFreeConstructionCost(freeTaker) && !ConstructionSiteReplay.TryApply(freeTaker) && freeSite.BuiltCalls == 1,
    "discounted paid construction keeps the existing payment path");
Check(!ConstructionSiteReplay.IsFreeConstructionCost(new GameObject().AddComponent<CostTaker>()),
    "ordinary free interactables do not become construction completions");
var queuedSpawn = order.Receive();
var confirmedHit = order.Receive();
Check(order.TryApply(7, confirmedHit), "apply confirmed hit before draining spawn queue");
Check(!order.TryApply(7, queuedSpawn), "queued spawn must not roll HP back");
Check(!order.TryApply(7, confirmedHit), "duplicate snapshot ignored");
Check(order.TryApply(8, queuedSpawn), "ordering is independent for each AI");
var death = order.Receive();
Check(order.TryApply(7, death) && !order.TryApply(7, confirmedHit), "old alive state cannot undo death");
Check(order.TryApply(7, order.Receive()), "new authoritative healing/snapshot remains legal");
order.Remove(7);
Check(order.TryApply(7, queuedSpawn), "removed ID may be reused");
order.Clear();
Check(order.Receive() == 1 && order.TryApply(7, 1), "scene reset clears receipt history");
Check(!order.TryApply(7, 0), "unsequenced state rejected");

var go = new GameObject();
var character = go.AddComponent<CharacterMainControl>();
var health = go.AddComponent<Health>();
character.IsMainCharacter = true;
Check(!RemoteHealthState.TryApply(health, 300, 280), "owner HP stays under native control");
character.IsMainCharacter = false;
Check(!RemoteHealthState.TryApply(health, 300, 280), "native AI stays under native control");
go.AddComponent<RemoteReplicaTag>();
var maxEvents = 0;
var hpEvents = 0;
health.OnMaxHealthChange += _ => maxEvents++;
health.OnHealthChange += _ => hpEvents++;
Check(RemoteHealthState.TryApply(health, 300, 280) && health.CurrentHealth == 280, "owner snapshot applied exactly");
Check(!(bool)Invoke(typeof(RemoteHealthWritePatch), "Prefix", health), "shelter regen cannot write mirror HP");
var maxArgs = new object[] { health, 0f };
Check(!(bool)Invoke(typeof(RemoteMaxHealthPatch), "Prefix", maxArgs) && (float)maxArgs[1] == 300,
    "authoritative maximum bypasses copied equipment modifiers");
RemoteHealthState.TryApply(health, 300, 280);
Check(maxEvents == 1 && hpEvents == 1, "identical snapshots do not fire duplicate HP events");
RemoteHealthState.TryApply(health, 300, 300);
Check(health.CurrentHealth == 300 && hpEvents == 2, "owner healing is retained");
RemoteHealthState.TryApply(health, 200, 300);
Check(health.CurrentHealth == 200 && RemoteHealthState.Get(health).MaxHealth == 200, "maximum can fall after gear changes");
var local = new GameObject().AddComponent<Health>();
Check((bool)Invoke(typeof(RemoteHealthWritePatch), "Prefix", local), "native health setters remain enabled");
var ai = new GameObject();
ai.AddComponent<CharacterMainControl>();
ai.AddComponent<RemoteAIReplicaTag>();
Check(RemoteHealthState.TryApply(ai.AddComponent<Health>(), 80, 40), "AI replicas also receive exact host HP");

var preset = new CharacterRandomPreset();
character.characterPreset = preset;
Check(Invoke(typeof(PlayerDamageAiCombatFactorPatch), "AiCombatPreset", character) == null,
    "remote players are excluded from native AI-vs-AI combat factors");
var npc = new GameObject().AddComponent<CharacterMainControl>();
npc.characterPreset = preset;
Check(ReferenceEquals(Invoke(typeof(PlayerDamageAiCombatFactorPatch), "AiCombatPreset", npc), preset),
    "native AI-vs-AI factors are preserved");
var field = AccessTools.Field(typeof(CharacterMainControl), nameof(CharacterMainControl.characterPreset));
var load = new CodeInstruction(OpCodes.Ldfld, field);
load.labels.Add(default);
var transformed = ((IEnumerable<CodeInstruction>)Invoke(typeof(PlayerDamageAiCombatFactorPatch), "Transpiler",
    (object)new[] { load, new CodeInstruction(OpCodes.Ret) })).ToArray();
Check(transformed[0].opcode == OpCodes.Call && transformed[0].labels.Count == 1 && transformed[1].opcode == OpCodes.Ret,
    "transpiler preserves branches and unrelated instructions");

var packet = new AIHealthBroadcastRpc { Id = 7, MaxHealth = 100, CurrentHealth = 60,
    HasDamage = true, Damage = new DamageForwardPayload { DamageValue = 50 }, ConfirmedDamage = 40, KillerCredit = true };
var writer = new NetDataWriter();
packet.Serialize(writer);
var bytes = writer.CopyData();
var decoded = new AIHealthBroadcastRpc();
decoded.Deserialize(new NetDataReader(bytes));
Check(decoded.CurrentHealth == 60 && decoded.ConfirmedDamage == 40 && decoded.Damage.DamageValue == 50 && decoded.KillerCredit,
    "confirmed damage round-trips separately from raw hit damage");
decoded.Deserialize(new NetDataReader(bytes[..^sizeof(float)]));
Check(decoded.ConfirmedDamage == 0 && decoded.KillerCredit, "legacy health packets remain readable");

writer.Reset();
new DeadLootSpawnRpc { SceneIndex=12, LootUid=7, UseTombPrefab=true, PlayerId="", OwnerId="client-b" }.Serialize(writer);
var corpseSpawn = new DeadLootSpawnRpc();
var corpseReader = new NetDataReader(writer.CopyData());
corpseSpawn.Deserialize(corpseReader);
Check(corpseSpawn.OwnerId == "client-b" && corpseSpawn.PlayerId == "" && corpseReader.AvailableBytes == 0,
    "restored corpse ownership does not report a living owner as newly dead");
writer.Reset();
new DeadLootSpawnRpc { PlayerId="client-a", OwnerId="client-a" }.Serialize(writer);
corpseSpawn.Deserialize(new NetDataReader(writer.CopyData()));
Check(corpseSpawn.OwnerId == "client-a" && corpseSpawn.PlayerId == "client-a",
    "current death retains both owner identity and death notification");
writer.Reset();
writer.Put(12); writer.Put(7); writer.PutVector3(default); writer.PutQuaternion(default);
writer.Put(true); writer.Put("legacy-client"); writer.Put("Tomb");
corpseReader = new NetDataReader(writer.CopyData());
corpseSpawn.Deserialize(corpseReader);
Check(corpseSpawn.OwnerId == "legacy-client" && corpseReader.AvailableBytes == 0,
    "legacy corpse spawn packets retain their original death identity");

var buffOrder = new BuffReplayOrder();
var dropEntries = new[] {
    new ItemDropSnapshotEntry { DropId=71, Angle=12, SceneBuildIndex=23 },
    new ItemDropSnapshotEntry { DropId=72, Angle=34, SceneBuildIndex=24 }
};
writer.Reset(); new ItemDropSnapshotChunkRpc { Version=1, Reset=true, IsLast=true, Entries=dropEntries }.Serialize(writer);
var dropChunk = new ItemDropSnapshotChunkRpc();
dropChunk.Deserialize(new NetDataReader(writer.CopyData()));
Check(dropChunk.Entries.Select(e=>e.SceneBuildIndex).SequenceEqual(new[]{23,24}) && dropChunk.Entries[1].DropId==72,
    "drop snapshot scene trailer preserves multiple item boundaries");
var legacyDropReader = new NetDataReader(writer.CopyData());
legacyDropReader.GetInt(); legacyDropReader.GetBool(); legacyDropReader.GetBool(); legacyDropReader.GetUShort();
var legacyDrop = new ItemDropSnapshotEntry(); legacyDrop.Deserialize(legacyDropReader); legacyDrop.Deserialize(legacyDropReader);
Check(legacyDrop.DropId==72 && legacyDrop.Angle==34, "legacy drop readers still read every item");
writer.Reset(); writer.Put(1); writer.Put(true); writer.Put(true); writer.Put((ushort)2);
foreach(var entry in dropEntries) entry.Serialize(writer);
dropChunk.Deserialize(new NetDataReader(writer.CopyData()));
Check(dropChunk.Entries.All(e=>e.SceneBuildIndex==-1) && dropChunk.Entries[1].DropId==72,
    "legacy drop chunks retain the native current-scene fallback");
var aiBuff = new AIBuffBroadcastRpc { Id = 42, WeaponTypeId = 240, BuffId = 1002, Layers = 2, Removed = true };
writer.Reset(); aiBuff.Serialize(writer);
var aiBuffDecoded = new AIBuffBroadcastRpc();
aiBuffDecoded.Deserialize(new NetDataReader(writer.CopyData()));
Check(aiBuffDecoded.Layers == 2 && aiBuffDecoded.Removed, "AI treatment reports remaining layers without refreshing lifetime");
writer.Reset(); writer.Put(42); writer.Put(240); writer.Put(1002);
aiBuffDecoded.Deserialize(new NetDataReader(writer.CopyData()));
Check(aiBuffDecoded.Layers == null, "legacy AI additions retain stack semantics");
var aiEntries = new[] {
    new AISnapshotEntry { Id=42, BuffWeaponTypeIds=new[]{240}, BuffIds=new[]{1002}, BuffLayers=new[]{3} },
    new AISnapshotEntry { Id=43, BuffWeaponTypeIds=new[]{0,0}, BuffIds=new[]{2101,2102}, BuffLayers=new[]{2,1} }
};
writer.Reset(); new AISnapshotChunkRpc { Entries=aiEntries }.Serialize(writer);
var aiChunk = new AISnapshotChunkRpc();
aiChunk.Deserialize(new NetDataReader(writer.CopyData()));
Check(aiChunk.Entries[0].BuffLayers.SequenceEqual(new[]{3}) && aiChunk.Entries[1].BuffLayers.SequenceEqual(new[]{2,1}),
    "AI layer trailer preserves multiple inline entry boundaries");
var legacyChunkReader=new NetDataReader(writer.CopyData());
legacyChunkReader.GetBool(); legacyChunkReader.GetUShort();
var legacyEntry=new AISnapshotEntry(); legacyEntry.Deserialize(legacyChunkReader); legacyEntry.Deserialize(legacyChunkReader);
Check(legacyEntry.Id==43 && legacyEntry.BuffIds.SequenceEqual(new[]{2101,2102}), "legacy snapshot readers still read every entry");
writer.Reset(); writer.Put(false); writer.Put((ushort)2);
foreach(var entry in aiEntries) entry.Serialize(writer);
aiChunk.Deserialize(new NetDataReader(writer.CopyData()));
Check(aiChunk.Entries.All(e=>e.BuffLayers==null), "new snapshot readers accept legacy chunks without a trailer");
writer.Reset(); new AISpawnRpc { Entry=aiEntries[0] }.Serialize(writer);
var aiSpawn=new AISpawnRpc(); aiSpawn.Deserialize(new NetDataReader(writer.CopyData()));
Check(aiSpawn.Entry.BuffLayers.Single()==3, "AI initial spawn carries current layers");
var playerHealth=new PlayerHealthReportRpc { MaxHealth=107,CurrentHealth=90,LifeId=Guid.NewGuid().ToString("N"),
    Buffs=new[]{new PlayerBuffState { BuffId=1002,Layers=2 }} };
writer.Reset(); playerHealth.Serialize(writer);
var playerHealthDecoded=new PlayerHealthReportRpc(); playerHealthDecoded.Deserialize(new NetDataReader(writer.CopyData()));
Check(playerHealthDecoded.Buffs.Single().Layers==2 && playerHealthDecoded.LifeId==playerHealth.LifeId,
    "owner health heartbeat carries the complete buff state after life metadata");
playerHealth.Buffs=null; writer.Reset(); playerHealth.Serialize(writer);
playerHealthDecoded.Deserialize(new NetDataReader(writer.CopyData()));
Check(playerHealthDecoded.Buffs==null, "legacy health reports do not mean clear every buff");
writer.Reset(); new PlayerHealthBroadcastRpc { PlayerId="player",MaxHealth=107,CurrentHealth=90,Buffs=Array.Empty<PlayerBuffState>() }.Serialize(writer);
var playerBroadcast=new PlayerHealthBroadcastRpc(); playerBroadcast.Deserialize(new NetDataReader(writer.CopyData()));
Check(playerBroadcast.Buffs is {Length:0}, "an explicit empty owner snapshot clears template buffs");
writer.Reset(); new PlayerHealthBroadcastRpc { PlayerId="player",MaxHealth=107,CurrentHealth=90 }.Serialize(writer);
playerBroadcast.Deserialize(new NetDataReader(writer.CopyData()));
Check(playerBroadcast.Buffs==null, "legacy broadcasts preserve event-based synchronization");
var pendingBleed = buffOrder.Receive("player", 1002);
buffOrder.Receive("player", 1002);
Check(buffOrder.IsCurrent("player", 1002, pendingBleed), "concurrent additions retain native buff stacking");
var removedBleed = buffOrder.Receive("player", 1002, true);
Check(!buffOrder.IsCurrent("player", 1002, pendingBleed), "late async bleed addition cannot undo treatment");
Check(buffOrder.IsCurrent("player", 1002, removedBleed), "treatment remains the latest buff state");
buffOrder.Receive("other", 1002);
buffOrder.Receive("player", 1001);
Check(buffOrder.IsCurrent("player", 1002, removedBleed), "buff ordering is independent for players and effects");
buffOrder.Clear();
buffOrder.Receive("player", 1002);
Check(!buffOrder.IsCurrent("player", 1002, removedBleed), "reset cannot reuse a pending async replay token");
var buffPacket = new PlayerBuffBroadcastRpc { PlayerId = "player", BuffId = 1002, Removed = true };
writer.Reset();
buffPacket.Serialize(writer);
bytes = writer.CopyData();
var decodedBuff = new PlayerBuffBroadcastRpc();
decodedBuff.Deserialize(new NetDataReader(bytes));
Check(decodedBuff.Removed && decodedBuff.BuffId == 1002 && !decodedBuff.ColdLayers.HasValue, "bleed removal round-trips");
decodedBuff.Deserialize(new NetDataReader(bytes[..^1]));
Check(!decodedBuff.Removed, "old buff broadcast defaults to addition");
buffPacket.ColdLayers = 26;
buffPacket.Removed = false;
writer.Reset();
buffPacket.Serialize(writer);
decodedBuff.Deserialize(new NetDataReader(writer.CopyData()));
Check(decodedBuff.ColdLayers == 26 && !decodedBuff.Removed, "cold layer synchronization remains intact");
var buffReport = new PlayerBuffReportRpc { BuffId = 1002, Removed = true };
writer.Reset();
buffReport.Serialize(writer);
var decodedReport = new PlayerBuffReportRpc();
decodedReport.Deserialize(new NetDataReader(writer.CopyData()));
Check(decodedReport.Removed && decodedReport.BuffId == 1002, "owner's treatment report round-trips");
buffReport.Layers = 2;
writer.Reset(); buffReport.Serialize(writer);
decodedReport.Deserialize(new NetDataReader(writer.CopyData()));
Check(decodedReport.Removed && decodedReport.Layers == 2, "partial treatment reports the exact remaining layer count");
buffPacket.Removed = true; buffPacket.Layers = 2;
writer.Reset(); buffPacket.Serialize(writer);
decodedBuff.Deserialize(new NetDataReader(writer.CopyData()));
Check(decodedBuff.Removed && decodedBuff.Layers == 2, "partial treatment layer count reaches the observing player");
decodedBuff.Deserialize(new NetDataReader(writer.CopyData()[..^sizeof(int)]));
Check(decodedBuff.Removed && decodedBuff.Layers == null, "older removal packets remain readable without an absolute count");

foreach (var host in new[] { false, true })
{
    NetService.Instance = new NetService { IsServer = host };
    var mainObject = new GameObject();
    CharacterMainControl.Main = mainObject.AddComponent<CharacterMainControl>();
    CharacterMainControl.Main.IsMainCharacter = true;
    mainObject.AddComponent<Health>().IsDead = true;
    var buddy = new GameObject();
    buddy.AddComponent<CharacterMainControl>();
    buddy.AddComponent<Health>().CurrentHealth = 100;
    if (host)
    {
        var peer = new CoopPeer();
        NetService.Instance.remoteCharacters[peer] = buddy;
        NetService.Instance.playerStatuses[peer] = new PlayerStatus { SceneId = "Raid" };
    }
    else
    {
        NetService.Instance.clientRemoteCharacters["buddy"] = buddy;
        NetService.Instance.clientPlayerStatuses["buddy"] = new PlayerStatus { SceneId = "Raid" };
    }
    Spectator.Instance = new GameObject().AddComponent<Spectator>();
    GameCamera.Instance = new GameCamera { target = CharacterMainControl.Main };
    LevelManager.Instance.SetControllingCharacter(CharacterMainControl.Main);
    Duckov.UI.ClosureView.NativeCalls = 0;
    var settlement = Duckov.UI.ClosureView.ShowAndReturnTask(default);
    var baseLoads = 0;
    var nativeContinuation = settlement.Task.ContinueWith(_ => baseLoads++, TaskContinuationOptions.ExecuteSynchronously);
    Check(Spectator.Instance._spectatorActive && !settlement.Task.IsCompleted && baseLoads == 0,
        "native death return waits while spectating");
    Check(Duckov.UI.ClosureView.NativeCalls == 0, "settlement UI does not run behind spectator mode");
    Check(GameCamera.Instance.target == buddy.GetComponent<CharacterMainControl>() &&
          LevelManager.Instance.ControllingCharacter == CharacterMainControl.Main,
        "spectating follows the teammate without handing local input to its replica");
    var cameraChanges = GameCamera.Instance.TargetChanges;
    var controlPatch=typeof(Spectator).Assembly.GetType("EscapeFromDuckovCoopMod.SpectatorControlPatch");
    Check(!(bool)Invoke(controlPatch,"Prefix",LevelManager.Instance,CharacterMainControl.Main),
        "native restoration of the inactive owner cannot re-fire the spectator camera event");
    Check((bool)Invoke(controlPatch,"Prefix",LevelManager.Instance,buddy.GetComponent<CharacterMainControl>()),
        "spectator control guard does not block a different control request");
    Spectator.Instance.SpectateNext();
    Spectator.Instance.SpectatePrev();
    Spectator.Instance.RefreshCameraTarget(GameCamera.Instance.target);
    Check(GameCamera.Instance.TargetChanges == cameraChanges, "one-player cycling does not repeatedly reset camera blend");
    var secondBuddy = new GameObject().AddComponent<CharacterMainControl>();
    Spectator.Instance._spectateList.Insert(0, secondBuddy);
    Spectator.Instance.RefreshCameraTarget(GameCamera.Instance.target);
    Check(GameCamera.Instance.target == buddy.GetComponent<CharacterMainControl>() && Spectator.Instance._spectateIdx == 1,
        "list changes preserve the selected teammate by identity");
    Spectator.Instance._spectateList.RemoveAt(1);
    Spectator.Instance.RefreshCameraTarget(GameCamera.Instance.target);
    Check(GameCamera.Instance.target == secondBuddy, "a removed target switches the actual camera to a surviving teammate");
    var duplicate = Duckov.UI.ClosureView.ShowAndReturnTask(default);
    Check(ReferenceEquals(duplicate.Task, settlement.Task), "duplicate death closure callers await the same flow");
    Spectator.Instance.EndSpectatorAndShowClosure();
    TestUniTask.Tick();
    Check(Duckov.UI.ClosureView.NativeCalls == 1 && !settlement.Task.IsCompleted && baseLoads == 0,
        "spectator exit opens one settlement and still waits for Continue");
    Check(GameCamera.Instance.target == CharacterMainControl.Main, "settlement restores the owner's camera");
    Spectator.Instance.EndSpectatorAndShowClosure(true);
    Spectator.Instance.EndSpectatorAndShowClosure(true);
    TestUniTask.Tick();
    Check(Duckov.UI.ClosureView.NativeCalls == 1, "duplicate forced-end packets cannot open another settlement");
    Duckov.UI.ClosureView.Continue.SetResult();
    nativeContinuation.GetAwaiter().GetResult();
    Check(settlement.Task.IsCompletedSuccessfully && baseLoads == 1, "Continue resumes native return exactly once");
    Spectator.Instance = new GameObject().AddComponent<Spectator>();
    Spectator.Instance.EndSpectatorAndShowClosure(true);
    var earlyEnd = Duckov.UI.ClosureView.ShowAndReturnTask(default);
    Check(!Spectator.Instance._spectatorActive && !earlyEnd.Task.IsCompleted,
        "forced end before native death delay uses the original settlement");
    Duckov.UI.ClosureView.Continue.SetResult();
    Spectator.Instance = new GameObject().AddComponent<Spectator>();
    var disconnected = Duckov.UI.ClosureView.ShowAndReturnTask(default);
    NetService.Instance.networkStarted = false;
    TestUniTask.Tick();
    Check(!Spectator.Instance._spectatorActive && !disconnected.Task.IsCompleted,
        "disconnect exits spectator mode and waits for the native Continue button");
    Duckov.UI.ClosureView.Continue.SetResult();
    Check(disconnected.Task.IsCompletedSuccessfully, "disconnected death settlement completes");
    Check(!Spectator.Instance._skipSpectatorForNextClosure, "disconnect cannot leave a bypass flag for the next death");
}

// Exercise the actual corpse recovery bridge against native-shaped save and interaction surfaces.
NetService.Instance = new NetService { IsServer = false };
HealthM.Instance = new HealthM();
CharacterMainControl.Main.Health.IsDead = true;
object[] spawnArgs = { null, CharacterMainControl.Main.CharacterItem, Duckov.Utilities.GameplayDataSettings.Prefabs.LootBoxPrefab_Tomb };
var spawnPatch = typeof(EscapeFromDuckovCoopMod.Patch.Loot.DeadLootSpawnPatch);
Check((bool)Invoke(spawnPatch, "Prefix", spawnArgs) && HealthM.Instance.Reports == 1,
    "client death reports inventory before native destruction while retaining native item transfer");
var temporaryBody = new InteractableLootbox();
Invoke(spawnPatch, "Postfix", temporaryBody, spawnArgs[1], spawnArgs[2]);
Check(!temporaryBody.gameObject && !temporaryBody.gameObject.activeSelf,
    "the temporary client death tomb is hidden and discarded after native item transfer");
spawnArgs[1] = new ItemStatsSystem.Item();
Check((bool)Invoke(spawnPatch, "Prefix", spawnArgs) && HealthM.Instance.Reports == 1,
    "the client's saved recovery tomb remains available to native spawning");
var savedBody = new InteractableLootbox();
Invoke(spawnPatch, "Postfix", savedBody, spawnArgs[1], spawnArgs[2]);
Check(savedBody.gameObject && savedBody.gameObject.activeSelf, "historical client tombs are not discarded");
NetService.Instance.IsServer = true;
spawnArgs[1] = CharacterMainControl.Main.CharacterItem;
Check((bool)Invoke(spawnPatch, "Prefix", spawnArgs), "the host retains the native death tomb creation path");

NetService.Instance = new NetService { IsServer = true };
Saves.SavesSystem.Writes = 0;
var ownerObject = new GameObject();
CharacterMainControl.Main = ownerObject.AddComponent<CharacterMainControl>();
ownerObject.AddComponent<Health>().IsDead = true;
Duckov.DeadBodyManager.Instance = new();
var record = new Duckov.DeadBodyManager.DeathInfo
{
    raidID = 1, itemTreeData = new() { rootInstanceID = CharacterMainControl.Main.CharacterItem.GetInstanceID() }
};
Duckov.DeadBodyManager.Instance.Add(record);
var recordId = CorpseRecovery.GetLocalRecordId(CharacterMainControl.Main);
var body = new InteractableLootbox();
body.Inventory.Items[0] = new ItemStatsSystem.Item { Tree = new() { Type = 10, Durability = 45 } };
body.Inventory.Items[2] = new ItemStatsSystem.Item { Tree = new() { Type = 20, Children = { new() { Type = 30, Count = 20 } } } };
CorpseRecovery.RegisterServerBody(body, null, recordId);
Check(CorpseRecovery.HasRemaining(record) && Saves.SavesSystem.Writes == 1, "initial dropped inventory becomes the recovery source");
body.OnInteractStartEvent.Invoke(null, body);
Check(CorpseRecovery.HasRemaining(record) && Saves.SavesSystem.Writes == 1, "opening a corpse does not consume its remaining inventory");
var initial = Saves.SavesSystem.Load<Dictionary<string, byte[]>>("CoopCorpseRemainders")[recordId];
body.Inventory.Items.Remove(0);
CorpseRecovery.QueueSnapshot(body.Inventory);
body.Inventory.Items[2].Tree.Children[0].Count = 7;
CorpseRecovery.QueueSnapshot(body.Inventory);
Check(Saves.SavesSystem.Writes == 1, "one frame of mutations is coalesced before saving");
TestUniTask.Tick();
var remainder = ES3.Deserialize<ItemStatsSystem.Data.InventoryData>(Saves.SavesSystem.Load<Dictionary<string, byte[]>>("CoopCorpseRemainders")[recordId]);
Check(Saves.SavesSystem.Writes == 2 && remainder.entries.Count == 1 && remainder.entries[0].inventoryPosition == 2 &&
      remainder.entries[0].itemTreeData.Children[0].Count == 7, "removed weapon stays removed and nested partial stacks persist exactly");
CorpseRecovery.ApplySnapshot(new CorpseInventoryRpc { RecordId = recordId, Revision = 1, Data = initial });
Check(Saves.SavesSystem.Writes == 2, "old snapshots cannot restore already looted items");
Saves.SavesSystem.CurrentSlot = 2;
CorpseRecovery.ApplySnapshot(new CorpseInventoryRpc { RecordId = recordId, Revision = 50, Data = initial });
Check(Saves.SavesSystem.Writes == 2, "snapshots cannot modify another save slot");
Saves.SavesSystem.CurrentSlot = 1;
Duckov.DeadBodyManager.Instance = new();
Duckov.DeadBodyManager.Instance.Add(record);
Check(CorpseRecovery.HasRemaining(record), "remaining loot survives death-manager recreation");
body.Inventory.Items.Clear();
CorpseRecovery.QueueSnapshot(body.Inventory);
CorpseRecovery.Flush();
Check(!CorpseRecovery.HasRemaining(record) && record.touched, "fully empty corpses do not respawn");
TestUniTask.Tick();
var remoteBody = new InteractableLootbox();
remoteBody.Inventory.Items[0] = new ItemStatsSystem.Item { Tree = new() { Type = 10, Durability = 37, Children = { new() { Type = 40 } } } };
var remoteOwner = new CoopPeer();
CoopTool.Sent.Clear();
CorpseRecovery.RegisterServerBody(remoteBody, remoteOwner, recordId);
Check(CoopTool.Sent.Count == 1 && CoopTool.Sent[0].Owner == remoteOwner, "remote-owned corpse snapshots are sent directly to their owner");
writer.Reset();
CoopTool.Sent[0].Message.Serialize(writer);
var corpsePacket = new CorpseInventoryRpc();
corpsePacket.Deserialize(new NetDataReader(writer.CopyData()));
CorpseRecovery.ApplySnapshot(corpsePacket);
var restored = ES3.Deserialize<ItemStatsSystem.Data.InventoryData>(Saves.SavesSystem.Load<Dictionary<string, byte[]>>("CoopCorpseRemainders")[recordId]);
Check(restored.entries.Single().itemTreeData.Durability == 37 && restored.entries.Single().itemTreeData.Children.Single().Type == 40,
    "reliable corpse snapshots preserve durability and attachments");
var writes = Saves.SavesSystem.Writes;
CorpseRecovery.ApplySnapshot(corpsePacket);
Check(Saves.SavesSystem.Writes == writes, "duplicate remainder packets are idempotent");
remoteBody.Inventory.Items[0].Tree.Durability = 33;
CorpseRecovery.QueueSnapshot(remoteBody.Inventory);
CorpseRecovery.ResetNetwork();
TestUniTask.Tick();
Check(CoopTool.Sent.Count == 2, "reset flushes pending changes once and cancels old scheduled work");
var trackedItem = remoteBody.Inventory.Items[0];
trackedItem.InInventory = remoteBody.Inventory;
trackedItem.Tree.Durability = 32;
CorpseRecovery.QueueItemTreeSnapshot(new ItemStatsSystem.Item { InInventory = new() });
TestUniTask.Tick();
Check(CoopTool.Sent.Count == 2, "unrelated item changes do not capture a registered corpse");
SceneLoader.IsSceneLoading = true;
CorpseRecovery.QueueItemTreeSnapshot(trackedItem);
CorpseRecovery.QueueSnapshot(remoteBody.Inventory);
TestUniTask.Tick();
Check(CoopTool.Sent.Count == 2, "scene teardown cannot queue corpse destruction as looting");
SceneLoader.IsSceneLoading = false;
CorpseRecovery.QueueItemTreeSnapshot(trackedItem);
TestUniTask.Tick();
Check(CoopTool.Sent.Count == 3 && ES3.Deserialize<ItemStatsSystem.Data.InventoryData>(CoopTool.Sent[2].Message.Data)
    .entries.Single().itemTreeData.Durability == 32, "registered corpse changes still capture during gameplay");
trackedItem.Tree.Children[0].Count = 4;
CorpseRecovery.QueueItemTreeSnapshot(new ItemStatsSystem.Item { InInventory = new() { AttachedToItem = trackedItem } });
TestUniTask.Tick();
Check(CoopTool.Sent.Count == 4 && ES3.Deserialize<ItemStatsSystem.Data.InventoryData>(CoopTool.Sent[3].Message.Data)
    .entries.Single().itemTreeData.Children[0].Count == 4, "nested corpse contents resolve through their registered container");
var largePayload = new CorpseInventoryRpc { RecordId = recordId, Revision = 80, Data = new byte[70000] };
writer.Reset(); largePayload.Serialize(writer);
corpsePacket.Deserialize(new NetDataReader(writer.CopyData()));
Check(corpsePacket.Data.Length == 70000, "corpse payloads are not truncated to ushort length");
writer.Reset(); writer.Put(recordId); writer.Put(90L); writer.Put(50); writer.Put(new byte[2]);
var invalidRejected = false;
try { corpsePacket.Deserialize(new NetDataReader(writer.CopyData())); } catch (InvalidOperationException) { invalidRejected = true; }
Check(invalidRejected, "truncated corpse payloads are rejected before save application");

// Inspect installed native IL without loading Unity. These checks fail if the patch's native assumptions change.
var game = Environment.GetEnvironmentVariable("DUCKOV_GAME_DIRECTORY");
if (string.IsNullOrEmpty(game)) throw new Exception("Set DUCKOV_GAME_DIRECTORY to run native compatibility checks.");
using var pe = new PEReader(File.OpenRead(Path.Combine(game, "Duckov_Data", "Managed", "TeamSoda.Duckov.Core.dll")));
var metadata = pe.GetMetadataReader();
var healthType = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(t => metadata.GetString(t.Name) == "Health");
var hurt = healthType.GetMethods().Select(metadata.GetMethodDefinition).Single(m => metadata.GetString(m.Name) == "Hurt");
var il = pe.GetMethodBody(hurt.RelativeVirtualAddress).GetILBytes();
var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
    .Select(f => (OpCode)f.GetValue(null)).ToDictionary(o => unchecked((ushort)o.Value));
var presetLoads = 0;
var healthWrites = 0;
var shelterRestores = 0;
for (var offset = 0; offset < il.Length;)
{
    ushort code = il[offset++];
    if (code == 0xfe) code = (ushort)(0xfe00 | il[offset++]);
    var opcode = opcodes[code];
    if (opcode.OperandType == OperandType.InlineField || opcode.OperandType == OperandType.InlineMethod)
    {
        var handle = System.Reflection.Metadata.Ecma335.MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset));
        var name = handle.Kind switch
        {
            HandleKind.FieldDefinition => metadata.GetString(metadata.GetFieldDefinition((FieldDefinitionHandle)handle).Name),
            HandleKind.MethodDefinition => metadata.GetString(metadata.GetMethodDefinition((MethodDefinitionHandle)handle).Name),
            HandleKind.MemberReference => metadata.GetString(metadata.GetMemberReference((MemberReferenceHandle)handle).Name),
            _ => ""
        };
        if (opcode == OpCodes.Ldfld && name == "characterPreset") presetLoads++;
        if (name == "set_CurrentHealth") healthWrites++;
        if (name == "SetHealth") shelterRestores++;
    }
    offset += opcode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
        _ => 4
    };
}
Check(presetLoads == 2, "installed Hurt has exactly the two AI combat preset loads being patched");
Check(healthWrites >= 1 && shelterRestores == 1, "native HP subtraction and shelter protection remain present");
Console.WriteLine($"PASS: {assertions} health, spectator, corpse recovery, buff replay, RPC and native compatibility assertions.");

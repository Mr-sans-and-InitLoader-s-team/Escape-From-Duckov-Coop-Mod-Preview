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
object Invoke(Type type, string method, params object[] args) =>
    type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

// A snapshot received first can sit in the work queue while newer reliable HP/death is applied.
var order = new HealthSnapshotOrder();
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
Console.WriteLine($"PASS: {assertions} health authority, ordering, RPC and installed native compatibility assertions.");

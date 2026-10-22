using System.Reflection;
using Duckov.Quests.Tasks;

namespace EscapeFromDuckovCoopMod;

internal static class AiQuestCredit
{
    internal const int MaxEvidence = 64;
    private sealed class Contribution
    {
        internal NetPeer Peer;
        internal readonly Dictionary<(int Weapon, bool Headshot), DamageInfo> Hits = new();
    }

    private static readonly Dictionary<int, Dictionary<CharacterMainControl, Contribution>> Hits = new();
    private static readonly HashSet<int> Dead = new();
    private static readonly MethodInfo NativeKillTask = AccessTools.Method(typeof(QuestTask_KillCount), "Health_OnDead");
    private static readonly FieldInfo DeathHandlers = AccessTools.Field(typeof(Health), "OnDead");
    private static Health _clientVictim;
    private static DamageInfo[] _clientEvidence;
    [ThreadStatic] private static bool _applying;

    internal static void Reset()
    {
        Hits.Clear();
        Dead.Clear();
        _clientVictim = null;
        _clientEvidence = null;
    }

    internal static void CleanupDeaths()
    {
        foreach (var id in Dead) Hits.Remove(id);
        Dead.Clear();
    }

    internal static void Forget(int id)
    {
        Hits.Remove(id);
        Dead.Remove(id);
    }

    internal static void Record(int id, DamageInfo damage)
    {
        var service = NetService.Instance;
        var attacker = damage.fromCharacter;
        if (service == null || !service.IsServer || !attacker || damage.finalDamage <= 0f || Dead.Contains(id)) return;
        NetPeer peer = null;
        if (attacker != CharacterMainControl.Main)
        {
            peer = service.remoteCharacters.FirstOrDefault(p => p.Value && p.Value.GetComponent<CharacterMainControl>() == attacker).Key;
            if (peer == null) return;
        }
        if (!Hits.TryGetValue(id, out var players)) Hits[id] = players = new();
        if (!players.TryGetValue(attacker, out var contribution))
            players[attacker] = contribution = new Contribution { Peer = peer };
        var key = (damage.fromWeaponItemID, damage.crit > 0);
        if (contribution.Hits.ContainsKey(key) || contribution.Hits.Count < MaxEvidence)
            contribution.Hits[key] = damage;
    }

    internal static DamageForwardPayload[] Evidence(int id, CharacterMainControl attacker)
    {
        if (!attacker || !Hits.TryGetValue(id, out var players) || !players.TryGetValue(attacker, out var contribution)) return null;
        return contribution.Hits.Values.Select(d => DamageForwardPayload.FromDamageInfo(d)).ToArray();
    }

    internal static DamageForwardPayload[] Evidence(int id, NetPeer peer)
    {
        if (!Hits.TryGetValue(id, out var players)) return null;
        return players.Values.FirstOrDefault(p => p.Peer == peer)?.Hits.Values
            .Select(d => DamageForwardPayload.FromDamageInfo(d)).ToArray();
    }

    internal static NetPeer FindPeer(int id, CharacterMainControl attacker) =>
        attacker && Hits.TryGetValue(id, out var players) && players.TryGetValue(attacker, out var contribution)
            ? contribution.Peer : null;

    internal static void PublishAssists(AISyncEntry entry, DamageInfo? death)
    {
        if (death.HasValue) Record(entry.Id, death.Value);
        if (!Dead.Add(entry.Id) || !Hits.TryGetValue(entry.Id, out var players)) return;
        if (SharedHome.Active) return;
        foreach (var player in players)
        {
            var peer = player.Value.Peer;
            if (peer == null || peer.ConnectionState != ConnectionState.Connected ||
                death.HasValue && death.Value.fromCharacter == player.Key) continue;
            var evidence = Evidence(entry.Id, player.Key);
            if (evidence == null || evidence.Length == 0) continue;
            var message = new AIHealthBroadcastRpc
            {
                Id = entry.Id, MaxHealth = entry.MaxHealth, CurrentHealth = entry.CurrentHealth,
                BodyArmor = entry.BodyArmor, HeadArmor = entry.HeadArmor, IsDead = true,
                HasDamage = true, Damage = evidence[0], AssistCredit = true, QuestEvidence = evidence
            };
            CoopTool.SendRpcTo(peer, in message);
        }
    }

    internal static bool AllowNativeTask(QuestTask_KillCount task, Health victim)
    {
        if (_applying) return true;
        if (victim == _clientVictim && _clientEvidence != null)
        {
            ApplyTask(task, victim, _clientEvidence);
            return false;
        }
        var service = NetService.Instance;
        var character = victim ? victim.TryGetCharacter() : null;
        var controller = character ? character.aiCharacterController : null;
        if (service == null || !service.IsServer || !controller ||
            !CoopSyncDatabase.AI.TryGet(controller, out var entry) || !Hits.TryGetValue(entry.Id, out var players)) return true;
        var evidence = SharedHome.Active ? players.Values.SelectMany(p => p.Hits.Values).ToArray() :
            CharacterMainControl.Main && players.TryGetValue(CharacterMainControl.Main, out var own) ? own.Hits.Values.ToArray() : null;
        if (evidence == null) return false;
        ApplyTask(task, victim, evidence);
        return false;
    }

    private static void ApplyTask(QuestTask_KillCount task, Health victim, DamageInfo[] evidence)
    {
        var before = task.GenerateSaveData();
        var previous = _applying;
        try
        {
            _applying = true;
            foreach (var damage in evidence)
            {
                NativeKillTask.Invoke(task, new object[] { victim, damage });
                if (!Equals(before, task.GenerateSaveData())) break;
            }
        }
        finally { _applying = previous; }
    }

    internal static void ApplyClient(Health victim, DamageInfo[] evidence, Action killEvent)
    {
        var previousVictim = _clientVictim;
        var previousEvidence = _clientEvidence;
        try
        {
            _clientVictim = victim;
            _clientEvidence = evidence;
            if (killEvent != null) killEvent();
            else if (DeathHandlers.GetValue(null) is Action<Health, DamageInfo> handlers)
                foreach (var callback in handlers.GetInvocationList())
                    if (callback.Target is QuestTask_KillCount task && task) ApplyTask(task, victim, evidence);
        }
        finally
        {
            _clientVictim = previousVictim;
            _clientEvidence = previousEvidence;
        }
    }
}

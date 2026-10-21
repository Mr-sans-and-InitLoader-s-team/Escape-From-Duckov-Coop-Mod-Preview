namespace EscapeFromDuckovCoopMod;

public sealed partial class AISyncService
{
    private readonly HealthSnapshotOrder _clientBuffOrder = new();
    private readonly BuffReplayOrder _aiBuffReplayOrder = new();
    private readonly Dictionary<(int Ai, int Buff), int> _clientBuffLayers = new();

    private static int GetBuffLayers(CharacterMainControl character, int buffId) =>
        character ? character.GetBuffManager().Buffs.FirstOrDefault(b => b && b.ID == buffId)?.CurrentLayers ?? 0 : 0;

    private void SetClientBuffState(AISyncEntry entry, int weaponId, int buffId, int? layers)
    {
        entry.Buffs.RemoveAll(b => b.BuffId == buffId);
        if (layers != 0) AppendBuffState(entry.Buffs, weaponId, buffId);
        if (layers.HasValue) _clientBuffLayers[(entry.Id, buffId)] = layers.Value;
    }

    private int? ClientBuffLayers(int id, int buffId) =>
        _clientBuffLayers.TryGetValue((id, buffId), out var layers) ? layers : null;

    private void RefreshServerBuffs(AISyncEntry entry)
    {
        var character = TryGetCharacter(entry.Id);
        if (!character) return;
        entry.Buffs.Clear();
        foreach (var buff in character.GetBuffManager().Buffs)
            if (buff) AppendBuffState(entry.Buffs, buff.fromWeaponID, buff.ID);
    }

    private void ApplySnapshotBuffs(AISyncEntry entry, in AISnapshotEntry snapshot)
    {
        if (!_clientBuffOrder.TryApply(entry.Id, snapshot.BuffReceiveOrder)) return;
        var previous = entry.Buffs.ToArray();
        entry.Buffs.Clear();
        _clientPendingBuffs.Remove(entry.Id);
        var count = Math.Min(snapshot.BuffWeaponTypeIds?.Length ?? 0, snapshot.BuffIds?.Length ?? 0);
        for (var i = 0; i < count; i++)
        {
            var layers = snapshot.BuffLayers != null && i < snapshot.BuffLayers.Length ? (int?)snapshot.BuffLayers[i] : null;
            SetClientBuffState(entry, snapshot.BuffWeaponTypeIds[i], snapshot.BuffIds[i], layers);
        }
        if (_clientReplicas.TryGetValue(entry.Id, out var replica) && replica != null)
        {
            foreach (var buff in previous)
                if (!entry.Buffs.Any(b => b.BuffId == buff.BuffId))
                    ApplyBuffProxy(replica.Character, buff.WeaponTypeId, buff.BuffId, 0, false);
            replica.ApplyBuffList(entry.Buffs);
        }
    }

    private async UniTask ApplyReportedBuff(AISyncEntry entry, CharacterMainControl character, int weaponId, int buffId)
    {
        await ApplyBuffProxyAsync(character, weaponId, buffId);
        if (IsServer && character && CoopSyncDatabase.AI.TryGet(entry.Id, out var current) && current == entry)
            BroadcastAIBuff(entry, weaponId, buffId);
    }
}

namespace EscapeFromDuckovCoopMod;

public partial class Buff_
{
    internal static PlayerBuffState[] CaptureLocal() => CharacterMainControl.Main.GetBuffManager().Buffs
        .Where(b => b).Select(b => new PlayerBuffState { WeaponTypeId = b.fromWeaponID, BuffId = b.ID, Layers = b.CurrentLayers }).ToArray();

    internal void ReceiveSnapshot(string playerId, PlayerBuffState[] buffs)
    {
        if (buffs == null || Service == null || Service.IsSelfId(playerId)) return;
        CharacterMainControl character = null;
        if (Service.IsServer)
        {
            foreach (var pair in Service.remoteCharacters)
                if (Service.GetPlayerId(pair.Key) == playerId && pair.Value)
                    character = pair.Value.GetComponent<CharacterMainControl>();
        }
        else if (Service.clientRemoteCharacters.TryGetValue(playerId, out var go) && go)
            character = go.GetComponent<CharacterMainControl>();
        // A fresh owner heartbeat will reconcile replicas that are still being constructed.
        if (!character || character.IsMainCharacter) return;

        var desired = new HashSet<int>(buffs.Select(b => b.BuffId));
        var known = _replayOrder.BuffsFor(playerId).Concat(character.GetBuffManager().Buffs.Where(b => b).Select(b => b.ID)).Distinct().ToArray();
        foreach (var buffId in known)
            if (!desired.Contains(buffId))
                ApplyBuffToCharacter(playerId, character, 0, buffId, true, 0).Forget();
        foreach (var buff in buffs)
            ApplyBuffToCharacter(playerId, character, buff.WeaponTypeId, buff.BuffId, true, buff.Layers).Forget();
    }
}

namespace EscapeFromDuckovCoopMod;

public partial class Buff_
{
    private readonly BuffReplayOrder _replayOrder = new();

    public void Reset()
    {
        _replayOrder.Clear();
        CoopTool._cliPendingProxyBuffs.Clear();
    }

    internal void PublishLocalRemoval(int buffId)
    {
        var service = Service;
        if (service == null || !service.networkStarted || ApplyingNetworkBuff) return;
        var layers = CharacterMainControl.Main.GetBuffManager().Buffs.FirstOrDefault(b => b && b.ID == buffId)?.CurrentLayers ?? 0;
        if (service.IsServer) Server_BroadcastHostBuff(0, buffId, true, layers);
        else CoopTool.SendRpc(new PlayerBuffReportRpc { BuffId = buffId, Removed = true, Layers = layers });
    }

    internal UniTask ApplyPendingProxyBuff(string playerId, CharacterMainControl character, int weaponId, int buffId,
        int? layers, bool removed, long order) =>
        ApplyBuffToCharacter(playerId, character, weaponId, buffId, removed, layers, order).AsUniTask();

    private async UniTask<bool> ApplyBuffToCharacter(string playerId, CharacterMainControl character, int weaponId, int buffId,
        bool removed, int? layers = null, long? receivedOrder = null)
    {
        var order = receivedOrder ?? _replayOrder.Receive(playerId, buffId, removed || layers.HasValue);
        var clear = layers == 0 || (removed && !layers.HasValue);
        if (!character) return false;
        var prefab = clear ? null : await COOPManager.ResolveBuffAsync(weaponId, buffId);
        if (!character || (!clear && !prefab) || !_replayOrder.IsCurrent(playerId, buffId, order)) return false;

        var previous = ApplyingNetworkBuff;
        try
        {
            ApplyingNetworkBuff = true;
            if (clear) character.RemoveBuff(buffId, false);
            else
            {
                var active = character.GetBuffManager().Buffs.FirstOrDefault(b => b && b.ID == buffId);
                if (!removed || !active) character.AddBuff(prefab, null, weaponId);
                active = character.GetBuffManager().Buffs.FirstOrDefault(b => b && b.ID == buffId);
                if (active && layers.HasValue) active.CurrentLayers = Mathf.Clamp(layers.Value, 1, Mathf.Max(1, active.MaxLayers));
            }
        }
        finally { ApplyingNetworkBuff = previous; }
        return true;
    }
}

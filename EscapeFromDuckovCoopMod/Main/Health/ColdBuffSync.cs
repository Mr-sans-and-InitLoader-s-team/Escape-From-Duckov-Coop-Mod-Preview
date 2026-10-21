using Duckov.Buffs;
using Duckov.Utilities;

namespace EscapeFromDuckovCoopMod;

internal static class ColdBuffSync
{
    private static readonly Dictionary<(string PlayerId, int BuffId), int> Pending = new();
    private static int _blockedProxyReports;

    private static Buff Prefab(int id)
    {
        var buffs = GameplayDataSettings.Buffs;
        if (buffs == null) return null;
        if (buffs.Cold != null && buffs.Cold.ID == id) return buffs.Cold;
        if (buffs.SuperCold != null && buffs.SuperCold.ID == id) return buffs.SuperCold;
        return null;
    }

    public static bool IsCold(int id) => Prefab(id) != null;

    public static void PublishAllLocal()
    {
        var buffs = GameplayDataSettings.Buffs;
        if (buffs?.Cold != null) PublishLocal(CharacterMainControl.Main, buffs.Cold.ID);
        if (buffs?.SuperCold != null) PublishLocal(CharacterMainControl.Main, buffs.SuperCold.ID);
    }

    public static void PublishLocal(CharacterMainControl character, int buffId)
    {
        var service = NetService.Instance;
        if (service == null || !service.networkStarted || character == null || !character.IsMainCharacter ||
            Buff_.ApplyingNetworkBuff || !IsCold(buffId)) return;
        var current = character.GetBuffManager()?.Buffs.FirstOrDefault(buff => buff != null && buff.ID == buffId);
        var layers = current != null ? current.CurrentLayers : 0;
        if (service.IsServer)
            Broadcast(service.GetSelfNetworkId(), buffId, layers, null);
        else
            CoopTool.SendRpc(new PlayerBuffReportRpc { BuffId = buffId, ColdLayers = layers });
    }

    public static void ReceiveReport(CoopPeer sender, PlayerBuffReportRpc message)
    {
        var service = NetService.Instance;
        // Environmental cold belongs to the player's own simulation. An observer
        // must never turn its proxy's cold into an application request to the host.
        if (service == null || !service.IsServer || sender == null) return;
        if (!string.IsNullOrEmpty(message.TargetPlayerId)) { _blockedProxyReports++; return; }
        if (!message.ColdLayers.HasValue) return;
        var playerId = service.GetPlayerId(sender);
        var layers = message.ColdLayers.Value;
        var prefab = Prefab(message.BuffId);
        if (prefab == null || layers < 0 || layers > prefab.MaxLayers) return;
        StoreAndApply(playerId, message.BuffId, layers);
        Broadcast(playerId, message.BuffId, layers, sender);
    }

    public static void ReceiveBroadcast(PlayerBuffBroadcastRpc message)
    {
        if (message.ColdLayers.HasValue)
            StoreAndApply(message.PlayerId, message.BuffId, message.ColdLayers.Value);
    }

    private static void Broadcast(string playerId, int buffId, int layers, CoopPeer exclude)
    {
        CoopTool.SendRpc(new PlayerBuffBroadcastRpc { PlayerId = playerId, BuffId = buffId, ColdLayers = layers }, exclude);
    }

    private static void StoreAndApply(string playerId, int buffId, int layers)
    {
        var service = NetService.Instance;
        var prefab = Prefab(buffId);
        if (service == null || string.IsNullOrEmpty(playerId) || service.IsSelfId(playerId) || prefab == null ||
            layers < 0 || layers > prefab.MaxLayers) return;
        Pending[(playerId, buffId)] = layers;
        GameObject remote = null;
        if (service.IsServer)
        {
            if (service.TryGetPeerByPlayerId(playerId, out var peer)) service.remoteCharacters.TryGetValue(peer, out remote);
        }
        else service.clientRemoteCharacters.TryGetValue(playerId, out remote);
        if (remote != null) Apply(remote.GetComponent<CharacterMainControl>(), prefab, layers);
    }

    public static void ApplyPending(string playerId, CharacterMainControl character)
    {
        if (character == null || character.IsMainCharacter) return;
        foreach (var entry in Pending)
            if (entry.Key.PlayerId == playerId)
                Apply(character, Prefab(entry.Key.BuffId), entry.Value);
    }

    private static void Apply(CharacterMainControl character, Buff prefab, int layers)
    {
        if (character == null || character.IsMainCharacter || prefab == null) return;
        var previous = Buff_.ApplyingNetworkBuff;
        try
        {
            Buff_.ApplyingNetworkBuff = true;
            if (layers == 0) character.RemoveBuff(prefab.ID, false);
            else
            {
                character.AddBuff(prefab, null, 0);
                var current = character.GetBuffManager()?.Buffs.FirstOrDefault(buff => buff != null && buff.ID == prefab.ID);
                if (current != null) current.CurrentLayers = layers;
            }
        }
        finally { Buff_.ApplyingNetworkBuff = previous; }
    }

    public static void Forget(string playerId)
    {
        foreach (var key in Pending.Keys.Where(key => key.PlayerId == playerId).ToArray()) Pending.Remove(key);
    }
    public static void Reset()
    {
        Pending.Clear();
        _blockedProxyReports = 0;
    }

    public static void LogLocalState()
    {
        var main = CharacterMainControl.Main;
        if (main == null || main.Health == null) return;
        var cold = TimeOfDayController.coldLevel;
        var protection = main.ColdProtection;
        var nativeWillApply = !main.Health.Invincible && cold - protection > 0.1f;
        CoopLogSystem.WriteNetworkDiagnostic($"[ColdBuff] cold={cold:F2} protection={protection:F2} invincible={main.Health.Invincible} nativeWillApply={nativeWillApply} blockedProxyReports={_blockedProxyReports}");
        var manager = main.GetBuffManager();
        if (manager == null) return;
        foreach (var buff in manager.Buffs)
            if (buff != null && IsCold(buff.ID))
                CoopLogSystem.WriteNetworkDiagnostic($"[ColdBuff] id={buff.ID} layers={buff.CurrentLayers} remaining={buff.RemainingTime}");
    }
}

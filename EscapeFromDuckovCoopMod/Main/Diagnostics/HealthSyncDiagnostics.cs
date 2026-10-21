namespace EscapeFromDuckovCoopMod;

internal static class HealthSyncDiagnostics
{
    private static readonly Queue<string> RecentAiHits = new();

    public static void RecordAiHit(int id, float raw, float before, float after)
    {
        if (RecentAiHits.Count == 12) RecentAiHits.Dequeue();
        RecentAiHits.Enqueue($"[HealthSync] AI={id} requested={raw:F2} before={before:F2} after={after:F2} applied={before - after:F2}");
    }

    public static void LogLocalState()
    {
        var service = NetService.Instance;
        var health = CharacterMainControl.Main?.Health;
        if (service == null || !health) return;
        CoopLogSystem.WriteNetworkDiagnostic($"[HealthSync] role={(service.IsServer ? "host" : "client")} hp={health.CurrentHealth:F2}/{health.MaxHealth:F2} raid={LevelManager.Instance?.IsRaidMap}");
        var stats = service.netManager?.Statistics;
        CoopLogSystem.WriteNetworkDiagnostic($"[HealthSync] frameMs={Time.unscaledDeltaTime * 1000f:F1} cap={Application.targetFrameRate} vSync={QualitySettings.vSyncCount} focus={Application.isFocused} timeScale={Time.timeScale:F2} sent={stats?.PacketsSent} received={stats?.PacketsReceived} buffs={DescribeBuffs(CharacterMainControl.Main)}");
        if (service.IsServer)
        {
            foreach (var pair in service.remoteCharacters)
                LogMirror(service.GetPlayerId(pair.Key), pair.Value);
        }
        else
        {
            foreach (var pair in service.clientRemoteCharacters)
                LogMirror(pair.Key, pair.Value);
        }
        foreach (var hit in RecentAiHits) CoopLogSystem.WriteNetworkDiagnostic(hit);
    }

    private static void LogMirror(string playerId, GameObject instance)
    {
        var health = instance ? instance.GetComponent<CharacterMainControl>()?.Health : null;
        if (health) CoopLogSystem.WriteNetworkDiagnostic($"[HealthSync] mirror={playerId} hp={health.CurrentHealth:F2}/{health.MaxHealth:F2} locked={RemoteHealthState.Get(health)?.HasSnapshot == true} buffs={DescribeBuffs(health.TryGetCharacter())}");
    }

    private static string DescribeBuffs(CharacterMainControl character)
    {
        var manager = character ? character.GetBuffManager() : null;
        return manager ? string.Join(",", manager.Buffs.Where(buff => buff).Take(16).Select(buff => $"{buff.ID}:{buff.CurrentLayers}")) : string.Empty;
    }

    public static void Reset() => RecentAiHits.Clear();
}

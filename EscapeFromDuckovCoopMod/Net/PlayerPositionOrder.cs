namespace EscapeFromDuckovCoopMod;

internal static class PlayerPositionOrder
{
    private static readonly Dictionary<string, double> Timestamps = new(StringComparer.Ordinal);

    internal static bool Accept(string playerId, double timestamp)
    {
        if (string.IsNullOrEmpty(playerId) || timestamp <= 0d || double.IsNaN(timestamp) || double.IsInfinity(timestamp)) return false;
        if (Timestamps.TryGetValue(playerId, out var previous) && timestamp <= previous) return false;
        Timestamps[playerId] = timestamp;
        return true;
    }

    internal static void Forget(string playerId)
    {
        if (playerId != null) Timestamps.Remove(playerId);
    }

    internal static void Clear() => Timestamps.Clear();
}

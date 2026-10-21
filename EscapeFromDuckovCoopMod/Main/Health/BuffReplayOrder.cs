namespace EscapeFromDuckovCoopMod;

internal sealed class BuffReplayOrder
{
    private long _sequence;
    private readonly Dictionary<(string Player, int Buff), long> _received = new();

    public long Receive(string player, int buff, bool removed = false)
    {
        // Legacy additions can stack; callers also supersede pending work for absolute layer snapshots.
        if (!removed && _received.TryGetValue((player, buff), out var current)) return current;
        var order = ++_sequence;
        _received[(player, buff)] = order;
        return order;
    }

    public bool IsCurrent(string player, int buff, long order) =>
        _received.TryGetValue((player, buff), out var latest) && latest == order;

    public void Clear() => _received.Clear();
    public int[] BuffsFor(string player) => _received.Keys.Where(k => k.Player == player).Select(k => k.Buff).ToArray();
}

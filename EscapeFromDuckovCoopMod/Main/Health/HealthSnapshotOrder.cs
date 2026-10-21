namespace EscapeFromDuckovCoopMod;

internal sealed class HealthSnapshotOrder
{
    private long _received;
    private readonly Dictionary<int, long> _applied = new();

    public long Receive() => ++_received;

    public bool TryApply(int id, long order)
    {
        if (order <= 0 || (_applied.TryGetValue(id, out var previous) && order <= previous)) return false;
        _applied[id] = order;
        return true;
    }

    public void Remove(int id) => _applied.Remove(id);

    public void Clear()
    {
        _applied.Clear();
        _received = 0;
    }
}

namespace EscapeFromDuckovCoopMod;

internal sealed class WeaponReplayOrder
{
    private readonly Dictionary<int, (int Socket, long Receipt)> _latest = new();
    private long _next;

    public long Receive(int modelId, int socket, bool hasItem)
    {
        // A full loadout includes empty entries for the other, unused sockets.
        if (!hasItem && _latest.TryGetValue(modelId, out var current) && current.Socket != socket)
            return current.Receipt;

        var receipt = ++_next;
        _latest[modelId] = (socket, receipt);
        return receipt;
    }

    public bool IsCurrent(int modelId, long receipt) =>
        _latest.TryGetValue(modelId, out var current) && current.Receipt == receipt;
}

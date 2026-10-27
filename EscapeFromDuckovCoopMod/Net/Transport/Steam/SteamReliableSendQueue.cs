namespace EscapeFromDuckovCoopMod;

internal sealed class SteamReliableSendQueue
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private const double StallTimeoutSeconds = 20;
    private readonly Queue<(byte[] Data, int Flags)> _frames = new();
    private int _bytes;
    private double _lastProgress;

    internal int Count => _frames.Count;
    internal bool IsStalled(double now) => Count > 0 && now - _lastProgress >= StallTimeoutSeconds;
    internal (byte[] Data, int Flags) Peek() => _frames.Peek();

    internal bool Enqueue(byte[] data, int length, int flags, double now)
    {
        if (length > MaxBytes - _bytes) return false;
        if (Count == 0) _lastProgress = now;
        _frames.Enqueue((data.AsSpan(0, length).ToArray(), flags));
        _bytes += length;
        return true;
    }

    internal void Dequeue(double now)
    {
        _bytes -= _frames.Dequeue().Data.Length;
        _lastProgress = now;
    }

    internal void Clear()
    {
        _frames.Clear();
        _bytes = 0;
    }
}

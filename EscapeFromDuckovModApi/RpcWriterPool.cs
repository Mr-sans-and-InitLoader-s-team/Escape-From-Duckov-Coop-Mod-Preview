using System.Collections.Concurrent;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public static class RpcWriterPool
{
    private static readonly ConcurrentBag<NetDataWriter> _pool = new();

    public static NetDataWriter Rent()
    {
        if (_pool.TryTake(out var writer))
        {
            writer.Reset();
            return writer;
        }

        return new NetDataWriter();
    }

    public static void Return(NetDataWriter writer)
    {
        if (writer == null) return;
        writer.Reset();
        _pool.Add(writer);
    }
}

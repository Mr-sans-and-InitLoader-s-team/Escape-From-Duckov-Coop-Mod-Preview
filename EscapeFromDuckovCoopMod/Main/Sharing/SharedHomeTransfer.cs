namespace EscapeFromDuckovCoopMod;

internal static class SharedHomeTransfer
{
    internal const byte ChunkKind = 13;
    private const int ChunkSize = 32 * 1024;
    private const int MaxLength = 4 * 1024 * 1024;
    private const int HeaderSize = 17;
    private static long _serial;
    private sealed class Pending
    {
        internal long Id;
        internal SharedHomeRpc Message;
        internal int Received;
    }
    private static readonly Dictionary<NetPeer, Pending> Incoming = new();

    internal static void Send(SharedHomeRpc message, NetPeer peer = null)
    {
        if (message.Data == null || message.Data.Length <= ChunkSize)
        {
            SendPacket(message, peer);
            return;
        }
        if (message.Data.Length > MaxLength) throw new InvalidOperationException("Shared home payload too large");
        var id = ++_serial;
        for (var offset = 0; offset < message.Data.Length; offset += ChunkSize)
        {
            var count = Math.Min(ChunkSize, message.Data.Length - offset);
            var writer = new NetDataWriter();
            writer.Put(message.Kind);
            writer.Put(id);
            writer.Put(message.Data.Length);
            writer.Put(offset);
            writer.Put(message.Data, offset, count);
            SendPacket(new SharedHomeRpc { Kind = ChunkKind, Session = message.Session,
                Revision = message.Revision, Data = writer.CopyData() }, peer);
        }
        CoopSessionLog.Write("SharedSave", $"Sent kind={message.Kind} bytes={message.Data.Length} chunks={(message.Data.Length + ChunkSize - 1) / ChunkSize}");
    }

    private static void SendPacket(SharedHomeRpc message, NetPeer peer)
    {
        if (peer != null) CoopTool.SendRpcTo(peer, in message);
        else CoopTool.SendRpc(in message);
    }

    internal static bool Receive(NetPeer peer, SharedHomeRpc chunk, out SharedHomeRpc message)
    {
        message = default;
        try
        {
            if (peer == null || chunk.Data == null || chunk.Data.Length <= HeaderSize || chunk.Data.Length > HeaderSize + ChunkSize)
                throw new InvalidOperationException("Invalid shared home chunk size");
            var reader = new NetPacketReader(chunk.Data, 0, chunk.Data.Length);
            var kind = reader.GetByte();
            var id = reader.GetLong();
            var total = reader.GetInt();
            var offset = reader.GetInt();
            var count = reader.AvailableBytes;
            if (kind >= ChunkKind || total <= ChunkSize || total > MaxLength || offset < 0 || offset > total - count)
                throw new InvalidOperationException("Invalid shared home chunk header");
            if (offset == 0)
            {
                Incoming[peer] = new Pending { Id = id, Message = new SharedHomeRpc {
                    Kind = kind, Session = chunk.Session, Revision = chunk.Revision, Data = new byte[total] } };
            }
            if (!Incoming.TryGetValue(peer, out var pending) || pending.Id != id || pending.Received != offset ||
                pending.Message.Kind != kind || pending.Message.Session != chunk.Session ||
                pending.Message.Revision != chunk.Revision || pending.Message.Data.Length != total)
                throw new InvalidOperationException("Out-of-order shared home chunk");
            Buffer.BlockCopy(chunk.Data, HeaderSize, pending.Message.Data, offset, count);
            pending.Received += count;
            if (pending.Received != total) return false;
            message = pending.Message;
            Incoming.Remove(peer);
            CoopSessionLog.Write("SharedSave", $"Received kind={kind} bytes={total}");
            return true;
        }
        catch
        {
            if (peer != null) Incoming.Remove(peer);
            throw;
        }
    }

    internal static void PeerDisconnected(NetPeer peer) => Incoming.Remove(peer);
    internal static void Reset() => Incoming.Clear();
}

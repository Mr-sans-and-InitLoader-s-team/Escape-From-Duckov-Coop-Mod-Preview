namespace EscapeFromDuckovCoopMod;

[Rpc(Op.CORPSE_INVENTORY, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct CorpseInventoryRpc : IRpcMessage
{
    public string RecordId;
    public long Revision;
    public byte[] Data;
    public void Serialize(NetDataWriter writer)
    {
        writer.Put(RecordId ?? string.Empty);
        writer.Put(Revision);
        CorpsePayload.Write(writer, Data);
    }
    public void Deserialize(NetPacketReader reader)
    {
        RecordId = reader.GetString();
        Revision = reader.GetLong();
        Data = CorpsePayload.Read(reader);
    }
}

internal static class CorpsePayload
{
    private const int MaxLength = 4 * 1024 * 1024;
    internal static void Write(NetDataWriter writer, byte[] data)
    {
        var length = data?.Length ?? 0;
        if (length > MaxLength) throw new InvalidOperationException("Corpse payload too large");
        writer.Put(length);
        if (length > 0) writer.Put(data);
    }
    internal static byte[] Read(NetPacketReader reader)
    {
        var length = reader.GetInt();
        if (length < 0 || length > MaxLength || length > reader.AvailableBytes)
            throw new InvalidOperationException("Invalid corpse payload length");
        var bytes = new byte[length];
        reader.GetBytes(bytes, length);
        return bytes;
    }
}

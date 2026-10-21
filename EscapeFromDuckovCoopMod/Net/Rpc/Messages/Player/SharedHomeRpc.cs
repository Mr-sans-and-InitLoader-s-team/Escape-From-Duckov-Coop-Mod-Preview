namespace EscapeFromDuckovCoopMod;

[Rpc(Op.SHARED_HOME, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct SharedHomeRpc : IRpcMessage
{
    public byte Kind;
    public string Session;
    public long Revision;
    public byte[] Data;
    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Kind);
        writer.Put(Session ?? string.Empty);
        writer.Put(Revision);
        CorpsePayload.Write(writer, Data);
    }
    public void Deserialize(NetPacketReader reader)
    {
        Kind = reader.GetByte();
        Session = reader.GetString();
        Revision = reader.GetLong();
        Data = CorpsePayload.Read(reader);
    }
}

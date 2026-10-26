namespace EscapeFromDuckovCoopMod;

[Rpc(Op.CORPSE_SPAWN_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct CorpseSpawnRequestRpc : IRpcMessage
{
    public string RecordId;
    public string PlayerIdentity;
    public int SceneIndex;
    public Vector3 Position;
    public byte[] Data;
    public void Serialize(NetDataWriter writer)
    {
        writer.Put(RecordId ?? string.Empty);
        writer.Put(SceneIndex);
        writer.PutVector3(Position);
        CorpsePayload.Write(writer, Data);
        writer.Put(PlayerIdentity ?? string.Empty);
    }
    public void Deserialize(NetPacketReader reader)
    {
        RecordId = reader.GetString();
        SceneIndex = reader.GetInt();
        Position = reader.GetVector3();
        Data = CorpsePayload.Read(reader);
        PlayerIdentity = reader.AvailableBytes > 0 ? reader.GetString() : string.Empty;
    }
}

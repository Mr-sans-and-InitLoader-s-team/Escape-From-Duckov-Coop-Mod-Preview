namespace EscapeFromDuckovCoopMod;

[Rpc(Op.CORPSE_SPAWN_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct CorpseSpawnRequestRpc : IRpcMessage
{
    public string RecordId;
    public int SceneIndex;
    public Vector3 Position;
    public byte[] Data;
    public void Serialize(NetDataWriter writer)
    {
        writer.Put(RecordId ?? string.Empty);
        writer.Put(SceneIndex);
        writer.PutVector3(Position);
        CorpsePayload.Write(writer, Data);
    }
    public void Deserialize(NetPacketReader reader)
    {
        RecordId = reader.GetString();
        SceneIndex = reader.GetInt();
        Position = reader.GetVector3();
        Data = CorpsePayload.Read(reader);
    }
}

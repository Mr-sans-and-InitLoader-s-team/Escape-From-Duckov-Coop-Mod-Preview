namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_SPAWNER_TRIGGER_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct AISpawnerTriggerRequestRpc : IRpcMessage
{
    public int SpawnerGuid;
    public Vector3 Position;
    public string SceneId;
    public byte TargetKind;
    public string ComponentPath;
    public byte Flags;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(SpawnerGuid);
        writer.PutVector3(Position);
        writer.Put(SceneId ?? string.Empty);
        writer.Put(TargetKind);
        writer.Put(ComponentPath ?? string.Empty);
        writer.Put(Flags);
    }

    public void Deserialize(NetPacketReader reader)
    {
        SpawnerGuid = reader.GetInt();
        Position = reader.GetVector3();
        SceneId = reader.GetString();
        TargetKind = reader.GetByte();
        ComponentPath = reader.GetString();
        Flags = reader.AvailableBytes >= 1 ? reader.GetByte() : (byte)0;
    }
}

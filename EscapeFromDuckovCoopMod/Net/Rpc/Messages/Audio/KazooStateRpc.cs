namespace EscapeFromDuckovCoopMod;

[Rpc(Op.KAZOO_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct KazooStateRpc : IRpcMessage
{
    public string PlayerId;
    public string SceneId;
    public uint Sequence;
    public bool Playing;
    public bool Transition;
    public float Pitch;
    public float Intensity;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(SceneId ?? string.Empty);
        writer.Put(Sequence);
        writer.Put(Playing);
        writer.Put(Transition);
        writer.Put(Pitch);
        writer.Put(Intensity);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        SceneId = reader.GetString();
        Sequence = reader.GetUInt();
        Playing = reader.GetBool();
        Transition = reader.GetBool();
        Pitch = reader.GetFloat();
        Intensity = reader.GetFloat();
    }
}

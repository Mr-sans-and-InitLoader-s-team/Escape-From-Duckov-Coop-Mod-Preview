namespace EscapeFromDuckovCoopMod;

[Rpc(Op.INTERACTABLE_FINISH_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct InteractableFinishRequestRpc : IRpcMessage
{
    public int KeyHash;
    public Vector3 Position;
    public string SceneId;
    public string Name;
    public byte Phase;
    public bool RequesterUsedRequiredItem;
    public int RequireItemId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(KeyHash);
        writer.PutVector3(Position);
        writer.Put(SceneId ?? string.Empty);
        writer.Put(Name ?? string.Empty);
        writer.Put(Phase);
        writer.Put(RequesterUsedRequiredItem);
        writer.Put(RequireItemId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        KeyHash = reader.GetInt();
        Position = reader.GetVector3();
        SceneId = reader.GetString();
        Name = reader.GetString();
        Phase = reader.AvailableBytes >= 1 ? reader.GetByte() : (byte)0;
        RequesterUsedRequiredItem = reader.AvailableBytes >= 1 && reader.GetBool();
        RequireItemId = reader.AvailableBytes >= 4 ? reader.GetInt() : 0;
    }
}

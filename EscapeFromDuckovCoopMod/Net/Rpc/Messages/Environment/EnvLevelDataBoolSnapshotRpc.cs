namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_LEVEL_BOOL_SNAPSHOT, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct EnvLevelDataBoolSnapshotRpc : IRpcMessage
{
    public string SceneId;
    public int[] Keys;
    public bool[] Values;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(SceneId);
        writer.Put((byte)Keys.Length);
        for (var i = 0; i < Keys.Length; i++) { writer.Put(Keys[i]); writer.Put(Values[i]); }
    }
    public void Deserialize(NetPacketReader reader)
    {
        SceneId = reader.GetString();
        var count = reader.GetByte();
        Keys = new int[count];
        Values = new bool[count];
        for (var i = 0; i < count; i++) { Keys[i] = reader.GetInt(); Values[i] = reader.GetBool(); }
    }
}

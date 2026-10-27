namespace EscapeFromDuckovCoopMod;

[Rpc(Op.SCENE_SPAWN_ENTRY, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
internal struct SceneSpawnEntryRpc : IRpcMessage
{
    public string Token, Map, Scene, Path;
    public int Beacon;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Token); writer.Put(Map); writer.Put(Scene); writer.Put(Path); writer.Put(Beacon);
    }
    public void Deserialize(NetPacketReader reader)
    {
        Token = reader.GetString(); Map = reader.GetString(); Scene = reader.GetString(); Path = reader.GetString(); Beacon = reader.GetInt();
    }
}

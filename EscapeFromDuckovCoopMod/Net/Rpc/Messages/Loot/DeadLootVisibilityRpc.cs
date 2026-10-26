namespace EscapeFromDuckovCoopMod;

[Rpc(Op.DEAD_LOOT_VISIBILITY, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct DeadLootVisibilityRpc : IRpcMessage
{
    public int SceneIndex, LootUid;
    public bool Visible;
    public void Serialize(NetDataWriter writer) { writer.Put(SceneIndex); writer.Put(LootUid); writer.Put(Visible); }
    public void Deserialize(NetPacketReader reader) { SceneIndex = reader.GetInt(); LootUid = reader.GetInt(); Visible = reader.GetBool(); }
}

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.STARTER_LOOT_CONTEXT, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct StarterLootContextRpc : IRpcMessage
{
    public string WorldId;
    public void Serialize(NetDataWriter writer) => writer.Put(WorldId);
    public void Deserialize(NetPacketReader reader) => WorldId = reader.GetString();
}

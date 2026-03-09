using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOOT_REQ_SPLIT, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct LootStackRequestRpc : IRpcMessage
{
    public LootIdentifier Id;
    public int Slot;
    public int Stack;
    public int TypeId;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.Put(Slot);
        writer.Put(Stack);
        writer.Put(TypeId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        Slot = reader.GetInt();
        Stack = reader.GetInt();
        TypeId = reader.GetInt();
    }
}

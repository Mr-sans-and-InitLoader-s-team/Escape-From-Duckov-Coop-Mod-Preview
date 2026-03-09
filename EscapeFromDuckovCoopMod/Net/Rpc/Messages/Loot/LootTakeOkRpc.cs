using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOOT_TAKE_OK, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct LootTakeOkRpc : IRpcMessage
{
    public LootIdentifier Id;
    public uint Token;
    public int Slot;
    public int TypeId;
    public int Stack;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.Put(Token);
        writer.Put(Slot);
        writer.Put(TypeId);
        writer.Put(Stack);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        Token = reader.GetUInt();
        Slot = reader.GetInt();
        TypeId = reader.GetInt();
        Stack = reader.GetInt();
    }
}

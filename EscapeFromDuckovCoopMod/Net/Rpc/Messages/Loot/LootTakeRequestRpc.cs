using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOOT_REQ_TAKE, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct LootTakeRequestRpc : IRpcMessage
{
    public LootIdentifier Id;
    public uint Token;
    public int Position;
    public int PreferDest;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.Put(Token);
        writer.Put(Position);
        writer.Put(PreferDest);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        Token = reader.GetUInt();
        Position = reader.GetInt();
        PreferDest = reader.GetInt();
    }
}

using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOOT_REQ_PUT, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct LootPutRequestRpc : IRpcMessage
{
    public LootIdentifier Id;
    public uint Token;
    public int PreferPos;
    public int TypeId;
    public int Count;
    public ItemSnapshot Item;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.Put(Token);
        writer.Put(PreferPos);
        writer.Put(TypeId);
        writer.Put(Count);
        ItemTool.WriteItemSnapshot(writer, Item);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        Token = reader.GetUInt();
        PreferPos = reader.GetInt();
        TypeId = reader.GetInt();
        Count = reader.GetInt();
        Item = ItemTool.ReadItemSnapshot(reader);
    }
}

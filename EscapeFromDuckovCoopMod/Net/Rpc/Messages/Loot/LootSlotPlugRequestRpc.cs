using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOOT_REQ_SLOT_PLUG, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct LootSlotPlugRequestRpc : IRpcMessage
{
    public LootIdentifier Id;
    public int ParentSlot;
    public string SlotKey;
    public string SourceSlotKey;
    public ItemSnapshot Child;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.Put(ParentSlot);
        writer.Put(SlotKey ?? string.Empty);
        writer.Put(SourceSlotKey ?? string.Empty);
        ItemTool.WriteItemSnapshot(writer, Child);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        ParentSlot = reader.GetInt();
        SlotKey = reader.GetString();
        SourceSlotKey = reader.GetString();
        Child = ItemTool.ReadItemSnapshot(reader);
    }
}

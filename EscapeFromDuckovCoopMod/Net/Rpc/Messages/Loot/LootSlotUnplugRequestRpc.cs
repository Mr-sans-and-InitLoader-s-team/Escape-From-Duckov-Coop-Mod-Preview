using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOOT_REQ_SLOT_UNPLUG, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct LootSlotUnplugRequestRpc : IRpcMessage
{
    public LootIdentifier Id;
    public int ParentSlot;
    public string SlotKey;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.Put(ParentSlot);
        writer.Put(SlotKey ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        ParentSlot = reader.GetInt();
        SlotKey = reader.GetString();
    }
}

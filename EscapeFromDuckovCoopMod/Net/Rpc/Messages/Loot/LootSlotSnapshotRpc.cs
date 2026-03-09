using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOOT_REQ_SLOT_SNAPSHOT, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct LootSlotSnapshotRpc : IRpcMessage
{
    public LootIdentifier Id;
    public int ParentSlot;
    public ItemSnapshot Snapshot;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.Put(ParentSlot);
        ItemTool.WriteItemSnapshot(writer, Snapshot);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        ParentSlot = reader.GetInt();
        Snapshot = ItemTool.ReadItemSnapshot(reader);
    }
}

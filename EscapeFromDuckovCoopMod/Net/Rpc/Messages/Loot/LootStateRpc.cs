using System;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public struct LootSlotSnapshot
{
    public bool HasItem;
    public int TypeId;
    public int Stack;
    public ItemSnapshot Item;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(HasItem);
        if (!HasItem) return;
        writer.Put(TypeId);
        writer.Put(Stack);
        ItemTool.WriteItemSnapshot(writer, Item);
    }

    public void Deserialize(NetPacketReader reader)
    {
        HasItem = reader.GetBool();
        if (!HasItem) return;
        TypeId = reader.GetInt();
        Stack = reader.GetInt();
        Item = ItemTool.ReadItemSnapshot(reader);
    }
}

[Rpc(Op.LOOT_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct LootStateRpc : IRpcMessage
{
    public LootIdentifier Id;
    public bool IsSnapshot;
    public int Version;
    public int Capacity;
    public LootSlotSnapshot[] Snapshot;
    public int Slot;
    public LootSlotSnapshot Delta;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.Put(IsSnapshot);
        writer.Put(Version);
        if (IsSnapshot)
        {
            writer.Put(Capacity);
            var count = Snapshot?.Length ?? 0;
            writer.Put((ushort)count);
            for (var i = 0; i < count; i++)
                Snapshot[i].Serialize(writer);
        }
        else
        {
            writer.Put(Slot);
            Delta.Serialize(writer);
        }
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        IsSnapshot = reader.GetBool();
        Version = reader.GetInt();
        if (IsSnapshot)
        {
            Capacity = reader.GetInt();
            var count = reader.GetUShort();
            Snapshot = count == 0 ? Array.Empty<LootSlotSnapshot>() : new LootSlotSnapshot[count];
            for (var i = 0; i < count; i++)
                Snapshot[i].Deserialize(reader);
        }
        else
        {
            Slot = reader.GetInt();
            Delta.Deserialize(reader);
        }
    }
}

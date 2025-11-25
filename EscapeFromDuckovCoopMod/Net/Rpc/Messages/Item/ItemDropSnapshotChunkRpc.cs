using System;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ITEM_DROP_SNAPSHOT_CHUNK, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct ItemDropSnapshotChunkRpc : IRpcMessage
{
    public int Version;
    public bool Reset;
    public bool IsLast;
    public ItemDropSnapshotEntry[] Entries;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Version);
        writer.Put(Reset);
        writer.Put(IsLast);
        var count = Entries?.Length ?? 0;
        writer.Put((ushort)count);
        for (var i = 0; i < count; i++)
        {
            Entries[i].Serialize(writer);
        }
    }

    public void Deserialize(NetPacketReader reader)
    {
        Version = reader.GetInt();
        Reset = reader.GetBool();
        IsLast = reader.GetBool();
        var count = reader.GetUShort();
        Entries = count == 0 ? Array.Empty<ItemDropSnapshotEntry>() : new ItemDropSnapshotEntry[count];
        for (var i = 0; i < count; i++)
        {
            Entries[i].Deserialize(reader);
        }
    }
}

public struct ItemDropSnapshotEntry
{
    public uint DropId;
    public Vector3 Position;
    public Vector3 Direction;
    public float Angle;
    public bool CreateRigidbody;
    public LootNet.ItemSnapshot Snapshot;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(DropId);
        writer.PutV3cm(Position);
        writer.PutDir(Direction);
        writer.Put(Angle);
        writer.Put(CreateRigidbody);
        ItemTool.WriteItemSnapshot(writer, Snapshot);
    }

    public void Deserialize(NetPacketReader reader)
    {
        DropId = reader.GetUInt();
        Position = reader.GetV3cm();
        Direction = reader.GetDir();
        Angle = reader.GetFloat();
        CreateRigidbody = reader.GetBool();
        Snapshot = ItemTool.ReadItemSnapshot(reader);
    }
}

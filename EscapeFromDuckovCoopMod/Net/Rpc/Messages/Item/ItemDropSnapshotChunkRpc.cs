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
        for (var i = 0; i < count; i++)
        {
            writer.Put(Entries[i].SceneBuildIndex);
        }
        for (var i = 0; i < count; i++)
        {
            writer.Put(Entries[i].PickupThrowable);
            if (Entries[i].PickupThrowable) writer.PutV3cm(Entries[i].Velocity);
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
        if (reader.AvailableBytes >= count * sizeof(int))
        {
            for (var i = 0; i < count; i++)
                Entries[i].SceneBuildIndex = reader.GetInt();
        }
        if (reader.AvailableBytes >= count)
        {
            for (var i = 0; i < count; i++)
            {
                Entries[i].PickupThrowable = reader.GetBool();
                if (Entries[i].PickupThrowable) Entries[i].Velocity = reader.GetV3cm();
            }
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
    public ItemSnapshot Snapshot;
    public int SceneBuildIndex;
    public bool PickupThrowable;
    public Vector3 Velocity;

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
        SceneBuildIndex = -1;
    }
}

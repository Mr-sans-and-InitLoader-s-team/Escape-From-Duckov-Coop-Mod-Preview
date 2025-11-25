using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ITEM_SPAWN, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct ItemSpawnRpc : IRpcMessage
{
    public uint Token;
    public uint DropId;
    public Vector3 Position;
    public Vector3 Direction;
    public float Angle;
    public bool CreateRigidbody;
    public LootNet.ItemSnapshot Snapshot;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Token);
        writer.Put(DropId);
        writer.PutV3cm(Position);
        writer.PutDir(Direction);
        writer.Put(Angle);
        writer.Put(CreateRigidbody);
        ItemTool.WriteItemSnapshot(writer, Snapshot);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Token = reader.GetUInt();
        DropId = reader.GetUInt();
        Position = reader.GetV3cm();
        Direction = reader.GetDir();
        Angle = reader.GetFloat();
        CreateRigidbody = reader.GetBool();
        Snapshot = ItemTool.ReadItemSnapshot(reader);
    }
}

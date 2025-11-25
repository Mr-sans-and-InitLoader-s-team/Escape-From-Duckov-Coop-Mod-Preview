using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ITEM_DROP_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct ItemDropRequestRpc : IRpcMessage
{
    public uint Token;
    public Vector3 Position;
    public Vector3 Direction;
    public float Angle;
    public bool CreateRigidbody;
    public LootNet.ItemSnapshot Snapshot;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Token);
        writer.PutV3cm(Position);
        writer.PutDir(Direction);
        writer.Put(Angle);
        writer.Put(CreateRigidbody);
        ItemTool.WriteItemSnapshot(writer, Snapshot);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Token = reader.GetUInt();
        Position = reader.GetV3cm();
        Direction = reader.GetDir();
        Angle = reader.GetFloat();
        CreateRigidbody = reader.GetBool();
        Snapshot = ItemTool.ReadItemSnapshot(reader);
    }
}

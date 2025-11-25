using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_SNAPSHOT_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct AISnapshotRequestRpc : IRpcMessage
{
    public bool HasRadius;
    public Vector3 Center;
    public float Radius;
    public bool ForceFull;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(HasRadius);
        writer.PutVector3(Center);
        writer.Put(Radius);
        writer.Put(ForceFull);
    }

    public void Deserialize(NetPacketReader reader)
    {
        HasRadius = reader.GetBool();
        Center = reader.GetVector3();
        Radius = reader.GetFloat();
        ForceFull = reader.GetBool();
    }
}

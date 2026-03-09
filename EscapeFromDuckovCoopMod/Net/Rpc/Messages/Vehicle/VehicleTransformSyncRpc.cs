using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.VEHICLE_TRANSFORM_SYNC, DeliveryMethod.Unreliable, RpcDirection.Bidirectional)]
public struct VehicleTransformSyncRpc : IRpcMessage
{
    public string PlayerId;
    public int VehicleId;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public double Timestamp;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(VehicleId);
        writer.PutVector3(Position);
        writer.PutQuaternion(Rotation);
        writer.PutVector3(Velocity);
        writer.Put(Timestamp);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        VehicleId = reader.GetInt();
        Position = reader.GetVector3();
        Rotation = reader.GetQuaternion();
        Velocity = reader.GetVector3();
        Timestamp = reader.GetDouble();
    }
}

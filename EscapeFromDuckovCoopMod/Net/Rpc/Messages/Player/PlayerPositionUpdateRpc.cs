using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.POSITION_UPDATE, DeliveryMethod.Unreliable, RpcDirection.Bidirectional)]
public struct PlayerPositionUpdateRpc : IRpcMessage
{
    public string EndPoint;
    public Vector3 Position;
    public Vector3 Forward;
    public Vector3 Velocity;
    public double Timestamp;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(EndPoint ?? string.Empty);
        writer.PutV3cm(Position);
        writer.PutDir(Forward);
        writer.PutVector3(Velocity);
        writer.Put(Timestamp);
    }

    public void Deserialize(NetPacketReader reader)
    {
        EndPoint = reader.GetString();
        Position = reader.GetV3cm();
        Forward = reader.GetDir();
        Velocity = reader.GetVector3();
        Timestamp = reader.GetDouble();
    }
}

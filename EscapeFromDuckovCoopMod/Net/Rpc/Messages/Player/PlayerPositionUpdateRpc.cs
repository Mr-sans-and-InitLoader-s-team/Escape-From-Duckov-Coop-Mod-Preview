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
    public int VehicleId;
    public string SpectatingPlayerId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(EndPoint ?? string.Empty);
        writer.PutV3cm(Position);
        writer.PutDir(Forward);
        writer.PutVector3(Velocity);
        writer.Put(Timestamp);
        writer.Put(VehicleId);
        writer.Put(SpectatingPlayerId ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        EndPoint = reader.GetString();
        Position = reader.GetV3cm();
        Forward = reader.GetDir();
        Velocity = reader.GetVector3();
        Timestamp = reader.GetDouble();
        VehicleId = reader.AvailableBytes >= 4 ? reader.GetInt() : 0;
        SpectatingPlayerId = reader.AvailableBytes >= 2 ? reader.GetString() : string.Empty;
    }
}

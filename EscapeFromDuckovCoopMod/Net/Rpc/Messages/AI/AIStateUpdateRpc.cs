using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_STATE_UPDATE, DeliveryMethod.Unreliable, RpcDirection.ServerToClient)]
public struct AIStateUpdateRpc : IRpcMessage
{
    public int Id;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Velocity;
    public float CurrentHealth;
    public AIStatus StatusOverride;
    public float MoveSpeed;
    public float MoveDirX;
    public float MoveDirY;
    public bool IsDashing;
    public bool IsAttacking;
    public int HandState;
    public bool GunReady;
    public int StateHash;
    public float NormTime;
    public double Timestamp;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.PutVector3(Position);
        writer.PutQuaternion(Rotation);
        writer.PutVector3(Velocity);
        writer.Put(CurrentHealth);
        writer.Put((byte)StatusOverride);
        writer.Put(MoveSpeed);
        writer.Put(MoveDirX);
        writer.Put(MoveDirY);
        writer.Put(IsDashing);
        writer.Put(IsAttacking);
        writer.Put(HandState);
        writer.Put(GunReady);
        writer.Put(StateHash);
        writer.Put(NormTime);
        writer.Put(Timestamp);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        Position = reader.GetVector3();
        Rotation = reader.GetQuaternion();
        Velocity = reader.GetVector3();
        CurrentHealth = reader.GetFloat();
        StatusOverride = (AIStatus)reader.GetByte();
        MoveSpeed = reader.GetFloat();
        MoveDirX = reader.GetFloat();
        MoveDirY = reader.GetFloat();
        IsDashing = reader.GetBool();
        IsAttacking = reader.GetBool();
        HandState = reader.GetInt();
        GunReady = reader.GetBool();
        StateHash = reader.GetInt();
        NormTime = reader.GetFloat();
        Timestamp = reader.GetDouble();
    }
}

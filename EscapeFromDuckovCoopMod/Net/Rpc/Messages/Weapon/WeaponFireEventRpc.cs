using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.FIRE_EVENT, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct WeaponFireEventRpc : IRpcMessage
{
    public string ShooterId;
    public int WeaponTypeId;
    public Vector3 MuzzlePosition;
    public Vector3 Direction;
    public float Speed;
    public float Distance;
    public bool IsFake;
    public bool PlayFx;
    public int Team;
    public int AiId;
    public bool HasPayload;
    public ProjectileContext Payload;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(ShooterId ?? string.Empty);
        writer.Put(WeaponTypeId);
        writer.PutV3cm(MuzzlePosition);
        writer.PutDir(Direction);
        writer.Put(Speed);
        writer.Put(Distance);
        writer.Put(IsFake);
        writer.Put(PlayFx);
        writer.Put(Team);
        writer.Put(AiId);
        if (HasPayload)
            writer.PutProjectilePayload(Payload);
        else
            writer.Put(false);
    }

    public void Deserialize(NetPacketReader reader)
    {
        ShooterId = reader.GetString();
        WeaponTypeId = reader.GetInt();
        MuzzlePosition = reader.GetV3cm();
        Direction = reader.GetDir();
        Speed = reader.GetFloat();
        Distance = reader.GetFloat();
        IsFake = reader.GetBool();
        PlayFx = reader.GetBool();
        Team = reader.GetInt();
        AiId = reader.GetInt();
        Payload = default;
        HasPayload = NetPackProjectile.TryGetProjectilePayload(reader, ref Payload);
    }
}

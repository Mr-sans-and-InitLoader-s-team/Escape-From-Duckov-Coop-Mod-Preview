using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ANIM_SYNC, DeliveryMethod.Sequenced, RpcDirection.Bidirectional)]
public struct PlayerAnimationSyncRpc : IRpcMessage
{
    public string PlayerId;
    public float MoveSpeed;
    public float MoveDirX;
    public float MoveDirY;
    public bool IsDashing;
    public bool IsAttacking;
    public int HandState;
    public bool GunReady;
    public int StateHash;
    public float NormTime;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(MoveSpeed);
        writer.Put(MoveDirX);
        writer.Put(MoveDirY);
        writer.Put(IsDashing);
        writer.Put(IsAttacking);
        writer.Put(HandState);
        writer.Put(GunReady);
        writer.Put(StateHash);
        writer.Put(NormTime);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        MoveSpeed = reader.GetFloat();
        MoveDirX = reader.GetFloat();
        MoveDirY = reader.GetFloat();
        IsDashing = reader.GetBool();
        IsAttacking = reader.GetBool();
        HandState = reader.GetInt();
        GunReady = reader.GetBool();
        StateHash = reader.GetInt();
        NormTime = reader.GetFloat();
    }
}

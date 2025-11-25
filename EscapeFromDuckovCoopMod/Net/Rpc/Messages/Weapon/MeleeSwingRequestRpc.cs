using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.MELEE_ATTACK_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct MeleeSwingRequestRpc : IRpcMessage
{
    public string PlayerId;
    public float DealDelay;
    public Vector3 SnapshotPosition;
    public Vector3 SnapshotDirection;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(DealDelay);
        writer.PutV3cm(SnapshotPosition);
        writer.PutDir(SnapshotDirection);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        DealDelay = reader.GetFloat();
        SnapshotPosition = reader.GetV3cm();
        SnapshotDirection = reader.GetDir();
    }
}

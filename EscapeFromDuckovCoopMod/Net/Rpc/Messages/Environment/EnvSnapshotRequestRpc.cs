using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_SYNC_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct EnvSnapshotRequestRpc : IRpcMessage
{
    public void Serialize(NetDataWriter writer)
    {
    }

    public void Deserialize(NetPacketReader reader)
    {
    }
}

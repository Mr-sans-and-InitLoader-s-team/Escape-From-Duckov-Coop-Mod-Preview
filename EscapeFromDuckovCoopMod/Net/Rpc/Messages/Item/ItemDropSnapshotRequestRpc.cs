using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ITEM_DROP_SNAPSHOT_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct ItemDropSnapshotRequestRpc : IRpcMessage
{
    public void Serialize(NetDataWriter writer)
    {
    }

    public void Deserialize(NetPacketReader reader)
    {
    }
}

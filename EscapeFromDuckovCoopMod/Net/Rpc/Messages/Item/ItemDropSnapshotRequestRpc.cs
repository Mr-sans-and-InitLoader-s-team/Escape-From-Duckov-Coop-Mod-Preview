using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ITEM_DROP_SNAPSHOT_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct ItemDropSnapshotRequestRpc : IRpcMessage
{
    public bool Reset;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Reset);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Reset = reader.GetBool();
    }
}

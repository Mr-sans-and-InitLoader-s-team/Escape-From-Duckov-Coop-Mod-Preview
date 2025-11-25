using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ITEM_PICKUP_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct ItemPickupRequestRpc : IRpcMessage
{
    public uint DropId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(DropId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        DropId = reader.GetUInt();
    }
}

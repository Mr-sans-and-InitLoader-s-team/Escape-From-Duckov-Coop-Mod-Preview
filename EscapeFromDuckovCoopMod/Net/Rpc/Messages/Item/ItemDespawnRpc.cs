using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ITEM_DESPAWN, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct ItemDespawnRpc : IRpcMessage
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

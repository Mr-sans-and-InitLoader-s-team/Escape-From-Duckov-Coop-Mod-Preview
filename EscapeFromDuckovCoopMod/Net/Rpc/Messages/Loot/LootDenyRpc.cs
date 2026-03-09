using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOOT_DENY, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct LootDenyRpc : IRpcMessage
{
    public string Reason;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Reason ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Reason = reader.GetString();
    }
}

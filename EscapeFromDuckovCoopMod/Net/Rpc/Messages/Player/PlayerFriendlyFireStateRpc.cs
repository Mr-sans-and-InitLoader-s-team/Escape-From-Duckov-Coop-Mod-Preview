using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_FRIENDLY_FIRE_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct PlayerFriendlyFireStateRpc : IRpcMessage
{
    public bool Enabled;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Enabled);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Enabled = reader.GetBool();
    }
}

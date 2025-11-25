using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.SPECTATOR_FORCE_END, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct SpectatorForceEndRpc : IRpcMessage
{
    public void Serialize(NetDataWriter writer)
    {
    }

    public void Deserialize(NetPacketReader reader)
    {
    }
}

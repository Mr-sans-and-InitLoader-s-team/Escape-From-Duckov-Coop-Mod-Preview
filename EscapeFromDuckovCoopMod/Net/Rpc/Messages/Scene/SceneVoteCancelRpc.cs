using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.SCENE_CANCEL, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct SceneVoteCancelRpc : IRpcMessage
{
    public void Serialize(NetDataWriter writer)
    {
    }

    public void Deserialize(NetPacketReader reader)
    {
    }
}

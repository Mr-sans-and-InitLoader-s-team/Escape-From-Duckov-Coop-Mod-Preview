using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.MOD_API_REPLAY_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct ModApiReplayRequestRpc : IRpcMessage
{
    public ModApiReplayRequestRpc(string channel)
    {
        Channel = channel ?? string.Empty;
    }

    public string Channel { get; private set; }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Channel ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Channel = reader.GetString();
    }
}

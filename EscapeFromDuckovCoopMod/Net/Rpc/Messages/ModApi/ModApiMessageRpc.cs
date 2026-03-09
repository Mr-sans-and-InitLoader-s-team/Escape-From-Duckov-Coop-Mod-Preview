using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.MOD_API_MESSAGE, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct ModApiMessageRpc : IRpcMessage
{
    public ModApiMessageRpc(string channel, byte[] payload)
    {
        Channel = channel ?? string.Empty;
        Payload = payload ?? Array.Empty<byte>();
    }

    public string Channel { get; private set; }

    public byte[] Payload { get; private set; }

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Channel ?? string.Empty);
        writer.PutBytesWithLength(Payload ?? Array.Empty<byte>());
    }

    public void Deserialize(NetPacketReader reader)
    {
        Channel = reader.GetString();
        Payload = reader.GetBytesWithLength();
    }
}

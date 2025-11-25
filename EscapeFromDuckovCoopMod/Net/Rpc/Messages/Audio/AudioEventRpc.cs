using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AUDIO_EVENT, DeliveryMethod.ReliableUnordered, RpcDirection.Bidirectional)]
public struct AudioEventRpc : IRpcMessage
{
    public CoopAudioEventPayload Payload;

    public void Serialize(NetDataWriter writer)
    {
        Payload.Write(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Payload = CoopAudioEventPayload.Read(reader);
    }
}

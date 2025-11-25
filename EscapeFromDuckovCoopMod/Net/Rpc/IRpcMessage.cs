using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public interface IRpcMessage
{
    void Serialize(NetDataWriter writer);

    void Deserialize(NetPacketReader reader);
}

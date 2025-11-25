using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_ACTIVATION_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct AIActivationRequestRpc : IRpcMessage
{
    public int Id;
    public bool Force;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put(Force);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        Force = reader.GetBool();
    }
}

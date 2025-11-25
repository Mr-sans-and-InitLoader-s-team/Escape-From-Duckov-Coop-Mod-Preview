using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_ACTIVATION_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct AIActivationStateRpc : IRpcMessage
{
    public int Id;
    public bool Activated;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put(Activated);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        Activated = reader.GetBool();
    }
}

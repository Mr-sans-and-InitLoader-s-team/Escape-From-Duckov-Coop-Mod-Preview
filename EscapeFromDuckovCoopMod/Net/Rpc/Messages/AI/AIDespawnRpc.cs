using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_DESPAWN, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct AIDespawnRpc : IRpcMessage
{
    public int Id;
    public AIStatus Status;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put((byte)Status);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        Status = (AIStatus)reader.GetByte();
    }
}

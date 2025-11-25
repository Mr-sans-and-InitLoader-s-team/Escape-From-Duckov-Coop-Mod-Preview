using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_SPAWN, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct AISpawnRpc : IRpcMessage
{
    public AISnapshotEntry Entry;

    public void Serialize(NetDataWriter writer)
    {
        Entry.Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Entry.Deserialize(reader);
    }
}

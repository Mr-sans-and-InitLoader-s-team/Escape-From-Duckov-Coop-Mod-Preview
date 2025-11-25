using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_DESTRUCTIBLE_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct EnvDestructibleStateRpc : IRpcMessage
{
    public bool Reset;
    public uint[] DeadIds;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Reset);
        var count = DeadIds?.Length ?? 0;
        writer.Put((ushort)count);
        for (var i = 0; i < count; i++) writer.Put(DeadIds[i]);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Reset = reader.GetBool();
        var count = reader.GetUShort();
        DeadIds = new uint[count];
        for (var i = 0; i < count; i++) DeadIds[i] = reader.GetUInt();
    }
}

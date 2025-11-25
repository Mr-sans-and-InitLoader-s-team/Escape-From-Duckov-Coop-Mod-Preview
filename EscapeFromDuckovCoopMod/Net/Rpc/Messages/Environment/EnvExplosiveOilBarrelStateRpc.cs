using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_EXPBARREL_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct EnvExplosiveOilBarrelStateRpc : IRpcMessage
{
    public bool Reset;
    public uint[] Ids;
    public bool[] ActiveStates;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Reset);
        var count = Ids?.Length ?? 0;
        writer.Put((ushort)count);
        for (var i = 0; i < count; i++) writer.Put(Ids[i]);

        for (var i = 0; i < count; i++) writer.Put(ActiveStates != null && i < ActiveStates.Length && ActiveStates[i]);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Reset = reader.GetBool();
        var count = reader.GetUShort();
        Ids = new uint[count];
        ActiveStates = new bool[count];
        for (var i = 0; i < count; i++) Ids[i] = reader.GetUInt();
        for (var i = 0; i < count; i++) ActiveStates[i] = reader.GetBool();
    }
}

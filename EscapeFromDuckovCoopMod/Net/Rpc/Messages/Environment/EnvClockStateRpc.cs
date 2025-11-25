using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_CLOCK_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct EnvClockStateRpc : IRpcMessage
{
    public long Day;
    public double SecondsOfDay;
    public float TimeScale;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Day);
        writer.Put(SecondsOfDay);
        writer.Put(TimeScale);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Day = reader.GetLong();
        SecondsOfDay = reader.GetDouble();
        TimeScale = reader.GetFloat();
    }
}

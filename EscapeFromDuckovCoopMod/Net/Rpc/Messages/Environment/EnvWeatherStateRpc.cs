using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_WEATHER_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct EnvWeatherStateRpc : IRpcMessage
{
    public int Seed;
    public bool ForceWeather;
    public int ForceWeatherValue;
    public int CurrentWeather;
    public byte StormLevel;
    public double StormEtaSeconds;
    public double StormIOverSeconds;
    public double StormIIOverSeconds;
    public float StormSleepPercent;
    public float StormRemainPercent;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Seed);
        writer.Put(ForceWeather);
        writer.Put(ForceWeatherValue);
        writer.Put(CurrentWeather);
        writer.Put(StormLevel);
        writer.Put(StormEtaSeconds);
        writer.Put(StormIOverSeconds);
        writer.Put(StormIIOverSeconds);
        writer.Put(StormSleepPercent);
        writer.Put(StormRemainPercent);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Seed = reader.GetInt();
        ForceWeather = reader.GetBool();
        ForceWeatherValue = reader.GetInt();
        CurrentWeather = reader.GetInt();
        StormLevel = reader.GetByte();
        StormEtaSeconds = reader.GetDouble();
        StormIOverSeconds = reader.GetDouble();
        StormIIOverSeconds = reader.GetDouble();
        StormSleepPercent = reader.GetFloat();
        StormRemainPercent = reader.GetFloat();
    }
}

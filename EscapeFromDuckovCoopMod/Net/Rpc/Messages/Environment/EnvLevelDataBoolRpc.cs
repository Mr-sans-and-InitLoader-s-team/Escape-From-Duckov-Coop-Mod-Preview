using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_LEVELDATA_BOOL, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct EnvLevelDataBoolRpc : IRpcMessage
{
    public string KeyString;
    public bool Value;
    public int KeyHash;
    public int VehicleId;
    public int RequireItemId;
    public bool CostTakerPaid;
    public long CostMoney;
    public string SceneId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(KeyString ?? string.Empty);
        writer.Put(Value);
        writer.Put(KeyHash);
        writer.Put(VehicleId);
        writer.Put(RequireItemId);
        writer.Put(CostTakerPaid);
        writer.Put(CostMoney);
        writer.Put(SceneId ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        KeyString = reader.GetString();
        Value = reader.GetBool();
        KeyHash = reader.AvailableBytes >= 4
            ? reader.GetInt()
            : !string.IsNullOrEmpty(KeyString)
                ? KeyString.GetHashCode()
                : 0;
        VehicleId = reader.AvailableBytes >= 4 ? reader.GetInt() : 0;
        RequireItemId = reader.AvailableBytes >= 4 ? reader.GetInt() : 0;
        CostTakerPaid = reader.AvailableBytes >= 1 && reader.GetBool();
        CostMoney = reader.AvailableBytes >= 8 ? reader.GetLong() : 0L;
        SceneId = reader.AvailableBytes > 0 ? reader.GetString() : string.Empty;
    }
}

using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_LEVELDATA_BOOL, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct EnvLevelDataBoolRpc : IRpcMessage
{
    public string KeyString;
    public bool Value;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(KeyString ?? string.Empty);
        writer.Put(Value);
    }

    public void Deserialize(NetPacketReader reader)
    {
        KeyString = reader.GetString();
        Value = reader.GetBool();
    }
}

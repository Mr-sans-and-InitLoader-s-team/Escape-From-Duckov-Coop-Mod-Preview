using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.HOST_BUFF_PROXY_APPLY, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct PlayerBuffBroadcastRpc : IRpcMessage
{
    public string PlayerId;
    public int WeaponTypeId;
    public int BuffId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(WeaponTypeId);
        writer.Put(BuffId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        WeaponTypeId = reader.GetInt();
        BuffId = reader.GetInt();
    }
}

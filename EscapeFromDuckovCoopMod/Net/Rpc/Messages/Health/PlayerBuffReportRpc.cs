using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_BUFF_SELF_APPLY, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct PlayerBuffReportRpc : IRpcMessage
{
    public string TargetPlayerId;
    public int WeaponTypeId;
    public int BuffId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(TargetPlayerId ?? string.Empty);
        writer.Put(WeaponTypeId);
        writer.Put(BuffId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        TargetPlayerId = reader.GetString();
        WeaponTypeId = reader.GetInt();
        BuffId = reader.GetInt();
    }
}

using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_BUFF_SELF_APPLY, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct PlayerBuffReportRpc : IRpcMessage
{
    public int WeaponTypeId;
    public int BuffId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(WeaponTypeId);
        writer.Put(BuffId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        WeaponTypeId = reader.GetInt();
        BuffId = reader.GetInt();
    }
}

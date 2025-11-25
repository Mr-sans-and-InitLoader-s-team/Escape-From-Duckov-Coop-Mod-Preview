using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_BUFF_REPORT, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct AIBuffReportRpc : IRpcMessage
{
    public int Id;
    public int WeaponTypeId;
    public int BuffId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put(WeaponTypeId);
        writer.Put(BuffId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        WeaponTypeId = reader.GetInt();
        BuffId = reader.GetInt();
    }
}

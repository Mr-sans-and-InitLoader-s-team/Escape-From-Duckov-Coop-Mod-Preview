using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_BUFF_BROADCAST, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct AIBuffBroadcastRpc : IRpcMessage
{
    public int Id;
    public int WeaponTypeId;
    public int BuffId;
    public int? Layers;
    public bool Removed;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put(WeaponTypeId);
        writer.Put(BuffId);
        if (Layers.HasValue)
        {
            writer.Put(Layers.Value);
            writer.Put(Removed);
        }
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        WeaponTypeId = reader.GetInt();
        BuffId = reader.GetInt();
        Layers = reader.AvailableBytes >= sizeof(int) ? reader.GetInt() : null;
        Removed = reader.AvailableBytes > 0 && reader.GetBool();
    }
}

using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.EQUIPMENT_UPDATE, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct EquipmentUpdateRpc : IRpcMessage
{
    public string PlayerId;
    public int SlotHash;
    public string ItemId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(SlotHash);
        writer.Put(ItemId ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        SlotHash = reader.GetInt();
        ItemId = reader.GetString();
    }
}

[Rpc(Op.PLAYERWEAPON_UPDATE, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct WeaponUpdateRpc : IRpcMessage
{
    public string PlayerId;
    public int SlotHash;
    public string ItemId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(SlotHash);
        writer.Put(ItemId ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        SlotHash = reader.GetInt();
        ItemId = reader.GetString();
    }
}

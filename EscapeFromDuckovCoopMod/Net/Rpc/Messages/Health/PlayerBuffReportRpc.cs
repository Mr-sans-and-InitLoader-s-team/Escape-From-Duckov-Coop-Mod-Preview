using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_BUFF_SELF_APPLY, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct PlayerBuffReportRpc : IRpcMessage
{
    public string TargetPlayerId;
    public int WeaponTypeId;
    public int BuffId;
    public int? ColdLayers;
    public bool Removed;
    public int? Layers;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(TargetPlayerId ?? string.Empty);
        writer.Put(WeaponTypeId);
        writer.Put(BuffId);
        writer.Put(ColdLayers.HasValue);
        if (ColdLayers.HasValue) writer.Put(ColdLayers.Value);
        writer.Put(Removed);
        if (Layers.HasValue) writer.Put(Layers.Value);
    }

    public void Deserialize(NetPacketReader reader)
    {
        TargetPlayerId = reader.GetString();
        WeaponTypeId = reader.GetInt();
        BuffId = reader.GetInt();
        ColdLayers = reader.AvailableBytes > 0 && reader.GetBool() ? reader.GetInt() : null;
        Removed = reader.AvailableBytes > 0 && reader.GetBool();
        Layers = reader.AvailableBytes >= sizeof(int) ? reader.GetInt() : null;
    }
}

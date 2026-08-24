using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.VEHICLE_CONTROL_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct VehicleControlRequestRpc : IRpcMessage
{
    public int VehicleId;
    public string RequesterId;
    public bool ClaimOnly;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(VehicleId);
        writer.Put(RequesterId ?? string.Empty);
        writer.Put(ClaimOnly);
    }

    public void Deserialize(NetPacketReader reader)
    {
        VehicleId = reader.GetInt();
        RequesterId = reader.GetString();
        ClaimOnly = reader.AvailableBytes > 0 && reader.GetBool();
    }
}

[Rpc(Op.VEHICLE_CONTROL_DECISION, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct VehicleControlDecisionRpc : IRpcMessage
{
    public int VehicleId;
    public string RequesterId;
    public bool Approved;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(VehicleId);
        writer.Put(RequesterId ?? string.Empty);
        writer.Put(Approved);
    }

    public void Deserialize(NetPacketReader reader)
    {
        VehicleId = reader.GetInt();
        RequesterId = reader.GetString();
        Approved = reader.GetBool();
    }
}

[Rpc(Op.VEHICLE_AUTHORITY_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct VehicleAuthorityStateRpc : IRpcMessage
{
    public int VehicleId;
    public string AuthorityPlayerId;
    public string PendingRequesterId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(VehicleId);
        writer.Put(AuthorityPlayerId ?? string.Empty);
        writer.Put(PendingRequesterId ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        VehicleId = reader.GetInt();
        AuthorityPlayerId = reader.GetString();
        PendingRequesterId = reader.GetString();
    }
}

[Rpc(Op.VEHICLE_ITEM_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct VehicleItemStateRpc : IRpcMessage
{
    public int VehicleId;
    public string PlayerId;
    public ItemSnapshot Snapshot;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(VehicleId);
        writer.Put(PlayerId ?? string.Empty);
        ItemTool.WriteItemSnapshot(writer, Snapshot);
    }

    public void Deserialize(NetPacketReader reader)
    {
        VehicleId = reader.GetInt();
        PlayerId = reader.GetString();
        Snapshot = ItemTool.ReadItemSnapshot(reader);
    }
}

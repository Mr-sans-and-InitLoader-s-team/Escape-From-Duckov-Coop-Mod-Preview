namespace EscapeFromDuckovCoopMod;

public enum TeleporterFlowKind : byte { Request, Check, Checked, ShowDeparture, Confirmed, Pay, Paid, Cancel, Aborted }

[Rpc(Op.TELEPORTER_FLOW, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct TeleporterFlowRpc : IRpcMessage
{
    public TeleporterFlowKind Kind;
    public string Token;
    public string SceneId;
    public int BeaconIndex;
    public bool Success;
    public string Reason;
    public string PlayerName;
    public void Serialize(NetDataWriter writer)
    {
        writer.Put((byte)Kind);
        writer.Put(Token ?? string.Empty);
        writer.Put(SceneId ?? string.Empty);
        writer.Put(BeaconIndex);
        writer.Put(Success);
        writer.Put(Reason ?? string.Empty);
        writer.Put(PlayerName ?? string.Empty);
    }
    public void Deserialize(NetPacketReader reader)
    {
        Kind = (TeleporterFlowKind)reader.GetByte();
        Token = reader.GetString();
        SceneId = reader.GetString();
        BeaconIndex = reader.GetInt();
        Success = reader.GetBool();
        Reason = reader.GetString();
        PlayerName = reader.AvailableBytes > 0 ? reader.GetString() : string.Empty;
    }
}

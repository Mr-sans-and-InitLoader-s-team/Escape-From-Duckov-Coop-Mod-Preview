using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.SCENE_VOTE_START, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct SceneVoteStartRpc : IRpcMessage
{
    public const byte CurrentVersion = 2;

    public byte Version;
    public string TargetSceneId;
    public string CurtainGuid;
    public bool UseLocation;
    public bool NotifyEvac;
    public bool SaveToFile;
    public string LocationName;
    public string HostSceneId;
    public string[] ParticipantIds;

    public void Serialize(NetDataWriter writer)
    {
        Version = CurrentVersion;
        writer.Put(Version);
        writer.Put(TargetSceneId ?? string.Empty);

        var hasCurtain = !string.IsNullOrEmpty(CurtainGuid);
        var flags = PackFlag.PackFlags(hasCurtain, UseLocation, NotifyEvac, SaveToFile);
        writer.Put(flags);

        if (hasCurtain)
            writer.Put(CurtainGuid);

        writer.Put(LocationName ?? string.Empty);
        writer.Put(HostSceneId ?? string.Empty);

        var count = ParticipantIds?.Length ?? 0;
        writer.Put(count);
        if (count <= 0 || ParticipantIds == null) return;

        for (var i = 0; i < ParticipantIds.Length; i++)
            writer.Put(ParticipantIds[i] ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Version = reader.GetByte();
        TargetSceneId = reader.GetString();

        var flags = reader.GetByte();
        bool hasCurtain;
        PackFlag.UnpackFlags(flags, out hasCurtain, out var useLocation, out var notifyEvac, out var saveToFile);
        UseLocation = useLocation;
        NotifyEvac = notifyEvac;
        SaveToFile = saveToFile;

        CurtainGuid = hasCurtain ? reader.GetString() : null;
        LocationName = reader.GetString();
        HostSceneId = Version >= 2 ? reader.GetString() : string.Empty;

        var count = reader.GetInt();
        if (count < 0 || count > 256)
            count = 0;

        ParticipantIds = new string[count];
        for (var i = 0; i < count; i++)
            ParticipantIds[i] = reader.GetString() ?? string.Empty;
    }
}

using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.SCENE_VOTE_REQ, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct SceneVoteRequestRpc : IRpcMessage
{
    public string TargetSceneId;
    public string CurtainGuid;
    public bool NotifyEvac;
    public bool SaveToFile;
    public bool UseLocation;
    public string LocationName;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(TargetSceneId ?? string.Empty);

        var hasCurtain = !string.IsNullOrEmpty(CurtainGuid);
        var flags = PackFlag.PackFlags(hasCurtain, UseLocation, NotifyEvac, SaveToFile);
        writer.Put(flags);

        if (hasCurtain)
            writer.Put(CurtainGuid);

        writer.Put(LocationName ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        TargetSceneId = reader.GetString();

        var flags = reader.GetByte();
        bool hasCurtain;
        PackFlag.UnpackFlags(flags, out hasCurtain, out var useLocation, out var notifyEvac, out var saveToFile);
        UseLocation = useLocation;
        NotifyEvac = notifyEvac;
        SaveToFile = saveToFile;

        CurtainGuid = hasCurtain ? reader.GetString() : null;
        LocationName = reader.GetString();
    }
}

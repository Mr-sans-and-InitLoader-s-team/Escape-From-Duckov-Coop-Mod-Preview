using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.SCENE_BEGIN_LOAD, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct SceneBeginLoadRpc : IRpcMessage
{
    public const byte CurrentVersion = 1;

    public byte Version;
    public string SceneId;
    public string CurtainGuid;
    public bool NotifyEvac;
    public bool SaveToFile;
    public bool UseLocation;
    public string LocationName;

    public void Serialize(NetDataWriter writer)
    {
        Version = CurrentVersion;
        writer.Put(Version);
        writer.Put(SceneId ?? string.Empty);

        var hasCurtain = !string.IsNullOrEmpty(CurtainGuid);
        var flags = PackFlag.PackFlags(hasCurtain, UseLocation, NotifyEvac, SaveToFile);
        writer.Put(flags);

        if (hasCurtain)
            writer.Put(CurtainGuid);

        writer.Put(LocationName ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Version = reader.GetByte();
        SceneId = reader.GetString();

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

using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_EXIT_SNAPSHOT_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct EnvExitSnapshotRequestRpc : IRpcMessage
{
    public string SceneId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(SceneId ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        SceneId = reader.GetString();
    }
}

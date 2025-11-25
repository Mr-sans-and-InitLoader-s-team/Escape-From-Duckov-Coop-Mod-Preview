using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.SCENE_READY_SET, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct SceneReadySetRpc : IRpcMessage
{
    public string PlayerId;
    public bool IsReady;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(IsReady);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        IsReady = reader.GetBool();
    }
}

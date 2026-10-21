using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.SCENE_JOIN, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct SceneJoinRpc : IRpcMessage
{
    public byte Kind;
    public string MainSceneId, SceneId;
    public Vector3 Position;
    public Quaternion Rotation;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Kind);
        writer.Put(MainSceneId ?? string.Empty);
        writer.Put(SceneId ?? string.Empty);
        writer.PutVector3(Position);
        writer.PutQuaternion(Rotation);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Kind = reader.GetByte();
        MainSceneId = reader.GetString();
        SceneId = reader.GetString();
        Position = reader.GetVector3();
        Rotation = reader.GetQuaternion();
    }
}

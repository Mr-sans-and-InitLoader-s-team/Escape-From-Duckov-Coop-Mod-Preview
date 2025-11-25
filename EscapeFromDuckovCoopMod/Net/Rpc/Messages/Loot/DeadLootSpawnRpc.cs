using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.DEAD_LOOT_SPAWN, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct DeadLootSpawnRpc : IRpcMessage
{
    public int SceneIndex;
    public int LootUid;
    public Vector3 Position;
    public Quaternion Rotation;
    public bool UseTombPrefab;
    public string PlayerId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(SceneIndex);
        writer.Put(LootUid);
        writer.PutVector3(Position);
        writer.PutQuaternion(Rotation);
        writer.Put(UseTombPrefab);
        writer.Put(PlayerId ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        SceneIndex = reader.GetInt();
        LootUid = reader.GetInt();
        Position = reader.GetVector3();
        Rotation = reader.GetQuaternion();
        UseTombPrefab = reader.GetBool();
        PlayerId = reader.GetString();
    }
}

using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public struct LootIdentifier
{
    public int Scene, PositionKey, InstanceId, LootUid;
    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Scene); writer.Put(PositionKey); writer.Put(InstanceId); writer.Put(LootUid);
    }
    public void Deserialize(NetPacketReader reader)
    {
        Scene=reader.GetInt(); PositionKey=reader.GetInt(); InstanceId=reader.GetInt(); LootUid=reader.GetInt();
    }
}

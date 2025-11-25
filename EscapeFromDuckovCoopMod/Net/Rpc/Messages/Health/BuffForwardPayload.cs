using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public struct BuffForwardPayload
{
    public int WeaponTypeId;
    public int BuffId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(WeaponTypeId);
        writer.Put(BuffId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        WeaponTypeId = reader.GetInt();
        BuffId = reader.GetInt();
    }
}

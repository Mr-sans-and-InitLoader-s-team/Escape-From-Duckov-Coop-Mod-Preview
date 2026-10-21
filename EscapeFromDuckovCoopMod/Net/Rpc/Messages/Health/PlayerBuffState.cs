namespace EscapeFromDuckovCoopMod;

public struct PlayerBuffState
{
    public int WeaponTypeId;
    public int BuffId;
    public int Layers;

    internal static void Write(NetDataWriter writer, PlayerBuffState[] buffs)
    {
        if (buffs == null) return;
        writer.Put((ushort)buffs.Length);
        foreach (var buff in buffs)
        {
            writer.Put(buff.WeaponTypeId);
            writer.Put(buff.BuffId);
            writer.Put(buff.Layers);
        }
    }

    internal static PlayerBuffState[] Read(NetPacketReader reader)
    {
        if (reader.AvailableBytes == 0) return null;
        var result = new PlayerBuffState[reader.GetUShort()];
        for (var i = 0; i < result.Length; i++)
            result[i] = new PlayerBuffState { WeaponTypeId = reader.GetInt(), BuffId = reader.GetInt(), Layers = reader.GetInt() };
        return result;
    }
}

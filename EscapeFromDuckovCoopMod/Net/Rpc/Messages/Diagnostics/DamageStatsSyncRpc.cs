using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public struct DamageStatsEntry
{
    public string PlayerId;
    public float TotalDamage;
}

[Rpc(Op.DAMAGE_STATS_SYNC, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct DamageStatsSyncRpc : IRpcMessage
{
    public List<DamageStatsEntry> Entries;

    public void Serialize(NetDataWriter writer)
    {
        var count = Entries?.Count ?? 0;
        writer.Put((ushort)count);
        if (count == 0 || Entries == null)
            return;

        for (int i = 0; i < count; i++)
        {
            var entry = Entries[i];
            writer.Put(entry.PlayerId ?? string.Empty);
            writer.Put(entry.TotalDamage);
        }
    }

    public void Deserialize(NetPacketReader reader)
    {
        var count = reader.GetUShort();
        Entries = new List<DamageStatsEntry>(count);

        for (int i = 0; i < count; i++)
        {
            Entries.Add(new DamageStatsEntry
            {
                PlayerId = reader.GetString(),
                TotalDamage = reader.GetFloat()
            });
        }
    }
}

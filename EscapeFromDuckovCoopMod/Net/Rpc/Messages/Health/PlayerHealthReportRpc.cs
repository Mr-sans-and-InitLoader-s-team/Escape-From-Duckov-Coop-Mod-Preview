using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_HEALTH_REPORT, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct PlayerHealthReportRpc : IRpcMessage
{
    public float MaxHealth;
    public float CurrentHealth;
    public bool HasDamage;
    public DamageForwardPayload Damage;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(MaxHealth);
        writer.Put(CurrentHealth);
        writer.Put(HasDamage);
        if (HasDamage)
            Damage.Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        MaxHealth = reader.GetFloat();
        CurrentHealth = reader.GetFloat();
        HasDamage = reader.GetBool();
        if (HasDamage)
        {
            Damage = default;
            Damage.Deserialize(reader);
        }
    }
}

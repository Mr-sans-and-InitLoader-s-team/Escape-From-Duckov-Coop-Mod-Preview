using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_HEALTH_REPORT, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct PlayerHealthReportRpc : IRpcMessage
{
    public float MaxHealth;
    public float CurrentHealth;
    public bool HasDamage;
    public DamageForwardPayload Damage;
    public string LifeId;
    public bool IsDead;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(MaxHealth);
        writer.Put(CurrentHealth);
        writer.Put(HasDamage);
        if (HasDamage)
            Damage.Serialize(writer);
        writer.Put(LifeId ?? string.Empty);
        writer.Put(IsDead);
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
        LifeId = reader.AvailableBytes > 0 ? reader.GetString() : string.Empty;
        IsDead = reader.AvailableBytes > 0 && reader.GetBool();
    }
}

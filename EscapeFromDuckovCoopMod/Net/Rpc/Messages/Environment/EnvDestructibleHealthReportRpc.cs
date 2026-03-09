using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.ENV_DESTRUCTIBLE_HEALTH_REPORT, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct EnvDestructibleHealthReportRpc : IRpcMessage
{
    public uint Id;
    public float MaxHealth;
    public float CurrentHealth;
    public bool IsDead;
    public bool HasDamage;
    public DamageForwardPayload Damage;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put(MaxHealth);
        writer.Put(CurrentHealth);
        writer.Put(IsDead);
        writer.Put(HasDamage);
        if (HasDamage)
            Damage.Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetUInt();
        MaxHealth = reader.GetFloat();
        CurrentHealth = reader.GetFloat();
        IsDead = reader.GetBool();
        HasDamage = reader.GetBool();
        if (HasDamage)
        {
            Damage = default;
            Damage.Deserialize(reader);
        }
    }
}

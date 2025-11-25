using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_HEALTH_REPORT, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct AIHealthReportRpc : IRpcMessage
{
    public int Id;
    public float MaxHealth;
    public float CurrentHealth;
    public float BodyArmor;
    public float HeadArmor;
    public bool IsDead;
    public bool HasDamage;
    public DamageForwardPayload Damage;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put(MaxHealth);
        writer.Put(CurrentHealth);
        writer.Put(BodyArmor);
        writer.Put(HeadArmor);
        writer.Put(IsDead);
        writer.Put(HasDamage);
        if (HasDamage)
            Damage.Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        MaxHealth = reader.GetFloat();
        CurrentHealth = reader.GetFloat();
        BodyArmor = reader.GetFloat();
        HeadArmor = reader.GetFloat();
        IsDead = reader.GetBool();
        HasDamage = reader.GetBool();
        if (HasDamage)
        {
            Damage = default;
            Damage.Deserialize(reader);
        }
    }
}

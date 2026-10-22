using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_HEALTH_BROADCAST, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct AIHealthBroadcastRpc : IRpcMessage
{
    public int Id;
    public float MaxHealth;
    public float CurrentHealth;
    public float BodyArmor;
    public float HeadArmor;
    public bool IsDead;
    public bool HasDamage;
    public DamageForwardPayload Damage;
    public bool KillerCredit;
    public float ConfirmedDamage;
    public bool AssistCredit;
    public DamageForwardPayload[] QuestEvidence;

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
        writer.Put(KillerCredit);
        writer.Put(ConfirmedDamage);
        writer.Put(AssistCredit);
        var count = Math.Min(QuestEvidence?.Length ?? 0, AiQuestCredit.MaxEvidence);
        writer.Put((byte)count);
        for (var i = 0; i < count; i++) QuestEvidence[i].Serialize(writer);
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
        KillerCredit = reader.AvailableBytes > 0 && reader.GetBool();
        ConfirmedDamage = reader.AvailableBytes >= sizeof(float) ? reader.GetFloat() : 0f;
        AssistCredit = reader.AvailableBytes > 0 && reader.GetBool();
        var count = reader.AvailableBytes > 0 ? reader.GetByte() : 0;
        if (count > AiQuestCredit.MaxEvidence) throw new InvalidOperationException("Invalid AI quest evidence count");
        QuestEvidence = count == 0 ? null : new DamageForwardPayload[count];
        for (var i = 0; i < count; i++) QuestEvidence[i].Deserialize(reader);
    }
}

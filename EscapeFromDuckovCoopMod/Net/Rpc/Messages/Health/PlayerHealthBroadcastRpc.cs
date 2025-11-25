using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_HEALTH_BROADCAST, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct PlayerHealthBroadcastRpc : IRpcMessage
{
    public string PlayerId;
    public float MaxHealth;
    public float CurrentHealth;
    public bool HasDamage;
    public DamageForwardPayload Damage;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        writer.Put(MaxHealth);
        writer.Put(CurrentHealth);
        writer.Put(HasDamage);
        if (HasDamage)
            Damage.Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
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

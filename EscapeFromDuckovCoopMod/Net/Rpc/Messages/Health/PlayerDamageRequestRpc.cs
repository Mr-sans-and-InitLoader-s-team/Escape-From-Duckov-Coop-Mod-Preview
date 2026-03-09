using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_DAMAGE_REQUEST, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct PlayerDamageRequestRpc : IRpcMessage
{
    public string TargetPlayerId;
    public DamageForwardPayload Damage;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(TargetPlayerId ?? string.Empty);
        Damage.Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        TargetPlayerId = reader.GetString();
        Damage = default;
        Damage.Deserialize(reader);
    }
}

using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.PLAYER_DAMAGE_FORWARD, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct PlayerDamageForwardRpc : IRpcMessage
{
    public string PlayerId;
    public DamageForwardPayload Damage;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(PlayerId ?? string.Empty);
        Damage.Serialize(writer);
    }

    public void Deserialize(NetPacketReader reader)
    {
        PlayerId = reader.GetString();
        Damage = default;
        Damage.Deserialize(reader);
    }
}

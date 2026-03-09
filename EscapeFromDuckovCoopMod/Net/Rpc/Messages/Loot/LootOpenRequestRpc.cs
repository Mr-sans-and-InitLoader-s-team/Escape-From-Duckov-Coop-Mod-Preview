using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOOT_REQ_OPEN, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct LootOpenRequestRpc : IRpcMessage
{
    public LootIdentifier Id;
    public Vector3 PositionHint;
    public bool ForceSnapshot;

    public void Serialize(NetDataWriter writer)
    {
        Id.Serialize(writer);
        writer.PutV3cm(PositionHint);
        writer.Put(ForceSnapshot);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id.Deserialize(reader);
        PositionHint = reader.GetV3cm();
        ForceSnapshot = reader.GetBool();
    }
}

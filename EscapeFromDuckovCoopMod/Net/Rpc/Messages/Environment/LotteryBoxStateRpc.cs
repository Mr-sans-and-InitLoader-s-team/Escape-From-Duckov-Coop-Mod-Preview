using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.LOTTERY_BOX_STATE, DeliveryMethod.ReliableOrdered, RpcDirection.Bidirectional)]
public struct LotteryBoxStateRpc : IRpcMessage
{
    public byte Phase;
    public int KeyHash;
    public Vector3 Position;
    public string SceneId;
    public string Name;
    public int ResultTypeId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Phase);
        writer.Put(KeyHash);
        writer.PutVector3(Position);
        writer.Put(SceneId ?? string.Empty);
        writer.Put(Name ?? string.Empty);
        writer.Put(ResultTypeId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Phase = reader.GetByte();
        KeyHash = reader.GetInt();
        Position = reader.GetVector3();
        SceneId = reader.GetString();
        Name = reader.GetString();
        ResultTypeId = reader.GetInt();
    }
}

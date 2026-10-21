using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_SOUND_EVENT, DeliveryMethod.Unreliable, RpcDirection.ServerToClient)]
public struct AISoundEventRpc : IRpcMessage
{
    public Vector3 Position;
    public float Radius;
    public Teams Team;
    public SoundTypes SoundType;
    public int SourceAiId;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Position.x);
        writer.Put(Position.y);
        writer.Put(Position.z);
        writer.Put(Radius);
        writer.Put((int)Team);
        writer.Put((byte)SoundType);
        writer.Put(SourceAiId);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Position = new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
        Radius = reader.GetFloat();
        Team = (Teams)reader.GetInt();
        SoundType = (SoundTypes)reader.GetByte();
        SourceAiId = reader.AvailableBytes >= sizeof(int) ? reader.GetInt() : 0;
    }
}

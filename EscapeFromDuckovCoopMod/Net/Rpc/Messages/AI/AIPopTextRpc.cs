using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.AI_POPTEXT, DeliveryMethod.ReliableUnordered, RpcDirection.ServerToClient)]
public struct AIPopTextRpc : IRpcMessage
{
    public int Id;
    public string Text;
    public float Speed;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Id);
        writer.Put(Text ?? string.Empty);
        writer.Put(Speed);
    }

    public void Deserialize(NetPacketReader reader)
    {
        Id = reader.GetInt();
        Text = reader.GetString();
        Speed = reader.GetFloat();
    }
}

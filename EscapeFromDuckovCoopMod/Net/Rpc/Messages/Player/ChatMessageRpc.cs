using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

[Rpc(Op.CHAT_SEND, DeliveryMethod.ReliableOrdered, RpcDirection.ClientToServer)]
public struct ChatSendRequestRpc : IRpcMessage
{
    public string SenderName;
    public string Content;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(SenderName ?? string.Empty);
        writer.Put(Content ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        SenderName = reader.GetString();
        Content = reader.GetString();
    }
}

[Rpc(Op.CHAT_MESSAGE, DeliveryMethod.ReliableOrdered, RpcDirection.ServerToClient)]
public struct ChatMessageRpc : IRpcMessage
{
    public string SenderId;
    public string SenderName;
    public string Content;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(SenderId ?? string.Empty);
        writer.Put(SenderName ?? string.Empty);
        writer.Put(Content ?? string.Empty);
    }

    public void Deserialize(NetPacketReader reader)
    {
        SenderId = reader.GetString();
        SenderName = reader.GetString();
        Content = reader.GetString();
    }
}

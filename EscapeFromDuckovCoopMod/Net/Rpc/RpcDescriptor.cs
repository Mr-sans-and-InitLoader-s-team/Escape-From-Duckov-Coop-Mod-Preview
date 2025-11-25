using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

public sealed class RpcDescriptor
{
    public RpcDescriptor(
        Op op,
        DeliveryMethod delivery,
        RpcDirection direction,
        Type messageType,
        Action<RpcContext, NetPacketReader> invoker)
    {
        Op = op;
        Delivery = delivery;
        Direction = direction;
        MessageType = messageType;
        Invoker = invoker;
    }

    public Op Op { get; }

    public DeliveryMethod Delivery { get; }

    public RpcDirection Direction { get; }

    public Type MessageType { get; }

    public Action<RpcContext, NetPacketReader> Invoker { get; }
}

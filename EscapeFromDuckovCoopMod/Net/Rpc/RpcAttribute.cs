using LiteNetLib;

namespace EscapeFromDuckovCoopMod;

[AttributeUsage(AttributeTargets.Struct)]
public sealed class RpcAttribute : Attribute
{
    public RpcAttribute(Op op, DeliveryMethod delivery, RpcDirection direction)
    {
        Op = op;
        Delivery = delivery;
        Direction = direction;
    }

    public Op Op { get; }

    public DeliveryMethod Delivery { get; }

    public RpcDirection Direction { get; }
}

public enum RpcDirection
{
    ClientToServer,
    ServerToClient,
    Bidirectional
}

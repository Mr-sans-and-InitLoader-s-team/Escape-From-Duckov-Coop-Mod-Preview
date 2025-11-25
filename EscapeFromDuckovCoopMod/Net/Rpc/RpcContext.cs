using LiteNetLib;

namespace EscapeFromDuckovCoopMod;

public readonly struct RpcContext
{
    public RpcContext(NetService service, NetPeer sender)
    {
        Service = service;
        Sender = sender;
    }

    public NetService Service { get; }

    public NetPeer Sender { get; }

    public bool IsServer => Service != null && Service.IsServer;
}

using System.Net;

namespace EscapeFromDuckovCoopMod;

internal sealed class LiteNetPeer : CoopPeer
{
    private readonly LiteNetLib.NetPeer _peer;
    public LiteNetPeer(LiteNetLib.NetPeer peer) => _peer = peer;
    public override int Id => _peer.Id;
    public override EndPoint EndPoint => _peer.EndPoint;
    public override int Ping => _peer.Ping;
    public override ConnectionState ConnectionState => _peer.ConnectionState;
    public override void Send(byte[] data, int offset, int length, DeliveryMethod deliveryMethod) => _peer.Send(data, offset, length, deliveryMethod);
    public override void Disconnect() => _peer.Disconnect();
}

using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EscapeFromDuckovCoopMod;

/// <summary>A game connection, independent of the underlying transport.</summary>
public abstract class CoopPeer
{
    public abstract int Id { get; }
    public abstract EndPoint EndPoint { get; }
    public virtual ulong SteamId => 0;
    public abstract int Ping { get; }
    public abstract ConnectionState ConnectionState { get; }
    public abstract void Send(byte[] data, int offset, int length, DeliveryMethod deliveryMethod);
    public void Send(byte[] data, DeliveryMethod deliveryMethod) => Send(data, 0, data.Length, deliveryMethod);
    public void Send(NetDataWriter writer, DeliveryMethod deliveryMethod) => Send(writer.Data, 0, writer.Length, deliveryMethod);
    public abstract void Disconnect();
}

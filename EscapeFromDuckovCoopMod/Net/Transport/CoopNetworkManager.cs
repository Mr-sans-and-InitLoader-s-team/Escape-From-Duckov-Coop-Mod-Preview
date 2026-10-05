using System.Net;
using System.Net.Sockets;

namespace EscapeFromDuckovCoopMod;

public interface ICoopNetworkListener
{
    void OnPeerConnected(CoopPeer peer);
    void OnPeerDisconnected(CoopPeer peer, DisconnectInfo info);
    void OnNetworkReceive(CoopPeer peer, CoopPacketReader reader, byte channel, DeliveryMethod delivery);
    void OnNetworkReceiveUnconnected(IPEndPoint endpoint, CoopPacketReader reader, UnconnectedMessageType type);
    void OnNetworkError(IPEndPoint endpoint, SocketError error);
    void OnNetworkLatencyUpdate(CoopPeer peer, int latency);
    void OnConnectionRequest(ConnectionRequest request);
}

public abstract class CoopNetworkManager
{
    protected readonly ICoopNetworkListener Listener;
    protected CoopNetworkManager(ICoopNetworkListener listener) => Listener = listener;
    public abstract bool IsRunning { get; }
    public abstract int LocalPort { get; }
    public abstract IReadOnlyList<CoopPeer> ConnectedPeerList { get; }
    public int ConnectedPeersCount => ConnectedPeerList.Count;
    public abstract NetStatistics Statistics { get; }
    public abstract bool Start(int port = 0);
    public abstract void Stop();
    public abstract void PollEvents();
    public virtual void TriggerUpdate() { }
    public virtual void Connect(string address, int port, NetDataWriter data) => throw new NotSupportedException();
    public virtual void SendUnconnectedMessage(NetDataWriter data, IPEndPoint endpoint) { }
    public void SendUnconnectedMessage(NetDataWriter data, string address, int port) => SendUnconnectedMessage(data, new IPEndPoint(IPAddress.Parse(address), port));
    public virtual void SendBroadcast(NetDataWriter data, int port) { }

    public void SendToAll(NetDataWriter writer, DeliveryMethod delivery, CoopPeer excludePeer = null)
    {
        // Sending can disconnect a peer on a fatal transport error.
        foreach (var peer in ConnectedPeerList.ToArray())
            if (peer != excludePeer && peer.ConnectionState == ConnectionState.Connected)
                peer.Send(writer, delivery);
    }
}

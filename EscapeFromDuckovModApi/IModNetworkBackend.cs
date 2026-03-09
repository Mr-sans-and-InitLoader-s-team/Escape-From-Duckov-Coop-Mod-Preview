using LiteNetLib;

namespace EscapeFromDuckovCoopMod;

public interface IModNetworkService
{
    bool IsServer { get; }
    bool NetworkStarted { get; }
}

public interface IModNetworkBackend
{
    IModNetworkService Service { get; }
    bool IsServer { get; }
    bool NetworkStarted { get; }
    int ConnectedPeersCount { get; }

    void SendToServer(string channel, byte[] payload);
    void SendToPeer(NetPeer target, string channel, byte[] payload);
    void Broadcast(string channel, byte[] payload);

    void SendReplayRequest(NetPeer target, string channel);
    void SendReplayResponse(NetPeer target, string channel, byte[] payload);
}

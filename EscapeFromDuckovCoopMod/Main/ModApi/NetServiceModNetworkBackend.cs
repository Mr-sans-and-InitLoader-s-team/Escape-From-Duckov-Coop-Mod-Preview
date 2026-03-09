using LiteNetLib;
using EscapeFromDuckovCoopMod;

namespace EscapeFromDuckovCoopMod;

internal sealed class NetServiceModNetworkBackend : IModNetworkBackend, IModNetworkService
{
    private readonly NetService _service;

    public NetServiceModNetworkBackend(NetService service)
    {
        _service = service;
    }

    public IModNetworkService Service => this;

    public bool IsServer => _service?.IsServer == true;

    public bool NetworkStarted => _service?.networkStarted == true;

    public int ConnectedPeersCount => _service?.netManager?.ConnectedPeersCount ?? 0;

    public void SendToServer(string channel, byte[] payload)
    {
        CoopTool.SendRpc(new ModApiMessageRpc(channel, payload));
    }

    public void SendToPeer(NetPeer target, string channel, byte[] payload)
    {
        if (target == null) return;
        CoopTool.SendRpcTo(target, new ModApiMessageRpc(channel, payload));
    }

    public void Broadcast(string channel, byte[] payload)
    {
        CoopTool.SendRpc(new ModApiMessageRpc(channel, payload));
    }

    public void SendReplayRequest(NetPeer target, string channel)
    {
        if (target == null) return;
        CoopTool.SendRpcTo(target, new ModApiReplayRequestRpc(channel));
    }

    public void SendReplayResponse(NetPeer target, string channel, byte[] payload)
    {
        if (target == null || payload == null) return;
        CoopTool.SendRpcTo(target, new ModApiMessageRpc(channel, payload));
    }
}

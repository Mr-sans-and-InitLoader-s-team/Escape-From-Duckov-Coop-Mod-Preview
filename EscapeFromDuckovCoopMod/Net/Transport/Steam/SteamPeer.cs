using System.Net;

namespace EscapeFromDuckovCoopMod;

internal sealed class SteamPeer : CoopPeer
{
    private readonly SteamSocketsTransport _transport;
    private readonly EndPoint _endpoint;
    private readonly uint[] _sent = new uint[5];
    private readonly uint[] _received = new uint[5];
    private readonly bool[] _hasReceived = new bool[5];
    public uint Handle { get; }
    public double CreatedAt { get; }
    public bool NativeConnected;
    public bool Ready;
    public bool Closed;
    public int Latency;
    public int SendFailures;
    public override int Id { get; }
    public override ulong SteamId { get; }
    public override EndPoint EndPoint => _endpoint;
    public override int Ping => Latency;
    public override ConnectionState ConnectionState => Closed ? ConnectionState.Disconnected : Ready ? ConnectionState.Connected : ConnectionState.Outgoing;

    public SteamPeer(SteamSocketsTransport transport, uint handle, ulong steamId, int id, double now)
    {
        _transport = transport;
        Handle = handle;
        SteamId = steamId;
        Id = id;
        CreatedAt = now;
        _endpoint = new SteamPeerEndPoint(steamId);
    }
    public uint NextSequence(DeliveryMethod delivery) => ++_sent[(int)delivery];
    public bool AcceptSequence(DeliveryMethod delivery, uint sequence)
    {
        if (!SteamPacketCodec.IsSequenced(delivery)) return true;
        var index = (int)delivery;
        if (_hasReceived[index] && !SteamPacketCodec.IsNewer(sequence, _received[index])) return false;
        _hasReceived[index] = true;
        _received[index] = sequence;
        return true;
    }
    public override void Send(byte[] data, int offset, int length, DeliveryMethod deliveryMethod) => _transport.Send(this, data, offset, length, deliveryMethod);
    public override void Disconnect() => _transport.Disconnect(this, "Local disconnect");

    private sealed class SteamPeerEndPoint : EndPoint
    {
        private readonly ulong _steamId;
        public SteamPeerEndPoint(ulong steamId) => _steamId = steamId;
        public override string ToString() => $"Steam:{_steamId}";
    }
}

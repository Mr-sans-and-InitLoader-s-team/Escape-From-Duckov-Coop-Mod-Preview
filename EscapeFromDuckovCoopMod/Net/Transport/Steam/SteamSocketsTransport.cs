using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using Steamworks;
using Debug = UnityEngine.Debug;

namespace EscapeFromDuckovCoopMod;

internal sealed class SteamSocketsTransport : CoopNetworkManager
{
    private const int MaxPacketsPerPoll = 512;
    private const double ReceiveBudgetSeconds = 0.004;
    private const double HandshakeTimeoutSeconds = 20;
    private readonly bool _isServer;
    private readonly string _version;
    private readonly Func<ulong, bool> _allowIncoming;
    private readonly ISteamSocketsApi _api;
    private readonly Func<double> _now;
    private readonly ConcurrentQueue<SteamConnectionChange> _changes = new();
    private readonly Dictionary<uint, SteamPeer> _peers = new();
    private readonly List<CoopPeer> _connected = new();
    private readonly Dictionary<SteamPeer, string> _closeRequests = new();
    private readonly NetStatistics _statistics = new();
    private bool _running;
    private uint _listen;
    private int _nextId;
    private double _nextStatusAt;

    public SteamSocketsTransport(ICoopNetworkListener listener, bool isServer, string version,
        Func<ulong, bool> allowIncoming, ISteamSocketsApi api = null, Func<double> now = null) : base(listener)
    {
        _isServer = isServer;
        _version = version;
        _allowIncoming = allowIncoming;
        _api = api ?? new NativeSteamSocketsApi();
        _now = now ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
    }

    public override bool IsRunning => _running;
    public override int LocalPort => 0;
    public override IReadOnlyList<CoopPeer> ConnectedPeerList => _connected;
    public override NetStatistics Statistics => _statistics;

    public override bool Start(int port = 0)
    {
        if (_running) return false;
        try
        {
            if (!_api.Initialize(change => _changes.Enqueue(change))) { _api.Dispose(); return false; }
            if (_isServer && (_listen = _api.Listen()) == 0) { _api.Dispose(); return false; }
            _running = true;
            CoopLogSystem.WriteNetworkDiagnostic($"[SteamSockets] Started role={(_isServer ? "host" : "client")} protocol={SteamPacketCodec.LobbyProtocol}");
            return true;
        }
        catch (Exception ex)
        {
            _api.Dispose();
            Debug.LogError($"[SteamSockets] Start failed: {ex}");
            return false;
        }
    }

    public void Connect(ulong steamId)
    {
        if (!_running || _isServer || steamId == 0) throw new InvalidOperationException("Invalid Steam connect state");
        if (_peers.Count != 0) throw new InvalidOperationException("A Steam host connection already exists");
        var handle = _api.Connect(steamId);
        if (handle == 0) throw new InvalidOperationException("Steam ConnectP2P failed");
        var peer = new SteamPeer(this, handle, steamId, _nextId++, _now());
        _peers.Add(handle, peer);
        if (!_api.Attach(handle)) Disconnect(peer, "Cannot attach receive poll group");
    }

    public override void PollEvents()
    {
        if (!_running) return;
        DrainDisconnects();
        while (_running && _changes.TryDequeue(out var change)) ProcessConnectionChange(change);
        var deadline = _now() + ReceiveBudgetSeconds;
        for (var i = 0; _running && i < MaxPacketsPerPoll && _now() < deadline; i++)
        {
            if (!_api.Receive(Receive)) break;
        }
        foreach (var peer in _peers.Values)
            if (!peer.Ready && _now() - peer.CreatedAt >= HandshakeTimeoutSeconds)
                Disconnect(peer, "Connection/version handshake timed out");
        if (_now() >= _nextStatusAt)
        {
            _nextStatusAt = _now() + 1;
            foreach (var peer in _peers.Values)
            {
                if (peer.Closed || !_api.GetStatus(peer.Handle, out var status)) continue;
                peer.Latency = Math.Max(0, status.m_nPing);
                if (peer.Ready) Listener.OnNetworkLatencyUpdate(peer, peer.Latency);
            }
        }
        DrainDisconnects();
    }

    private void ProcessConnectionChange(SteamConnectionChange change)
    {
        var handle = change.Handle;
        if (!_peers.TryGetValue(handle, out var peer))
        {
            // Ignore callbacks belonging to the game or another mod's listen socket.
            if (!_isServer || _listen == 0 || change.ListenSocket != _listen) return;
            if (change.State != ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                _api.Close(handle, "Untracked connection");
                return;
            }
            var steamId = change.SteamId;
            if (steamId == 0 || _allowIncoming == null || !_allowIncoming(steamId) || _peers.Count >= 15 || _peers.Values.Any(p => p.SteamId == steamId && !p.Closed))
            {
                _api.Close(handle, "Not an eligible lobby member");
                return;
            }
            peer = new SteamPeer(this, handle, steamId, _nextId++, _now());
            _peers.Add(handle, peer);
            if (!_api.Attach(handle) || _api.Accept(handle) != EResult.k_EResultOK)
                Disconnect(peer, "Steam accept failed");
            return;
        }
        if (peer.Closed) return;
        switch (change.State)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                if (peer.NativeConnected) return;
                peer.NativeConnected = true;
                if (!_isServer) SendHandshake(peer, SteamPacketKind.Hello);
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                Disconnect(peer, change.Reason);
                break;
        }
    }

    private void SendHandshake(SteamPeer peer, SteamPacketKind kind)
    {
        var writer = new NetDataWriter();
        writer.Put(_version);
        SendFrame(peer, writer.Data, 0, writer.Length, DeliveryMethod.ReliableOrdered, kind);
    }

    private void Receive(uint handle, byte[] buffer, int length)
    {
        if (!_peers.TryGetValue(handle, out var peer) || peer.Closed) return;
        try
        {
            if (!SteamPacketCodec.TryRead(buffer, length, out var kind, out var delivery, out var sequence))
            {
                Disconnect(peer, "Invalid Steam protocol frame");
                return;
            }
            var reader = new CoopPacketReader(buffer, SteamPacketCodec.HeaderSize, length - SteamPacketCodec.HeaderSize);
            if (kind != SteamPacketKind.Data)
            {
                if (peer.Ready || delivery != DeliveryMethod.ReliableOrdered ||
                    (_isServer ? kind != SteamPacketKind.Hello : kind != SteamPacketKind.Accept))
                {
                    Disconnect(peer, "Unexpected handshake");
                    return;
                }
                if (!string.Equals(reader.GetString(), _version, StringComparison.Ordinal) || reader.AvailableBytes != 0)
                {
                    Disconnect(peer, "Mod version mismatch");
                    return;
                }
                if (_isServer) SendHandshake(peer, SteamPacketKind.Accept);
                if (peer.Closed) return;
                peer.Ready = true;
                _connected.Add(peer);
                CoopLogSystem.WriteNetworkDiagnostic($"[SteamSockets] Connected peer={peer.EndPoint} version={_version}");
                Listener.OnPeerConnected(peer);
                return;
            }
            if (!peer.Ready || reader.AvailableBytes == 0 || !peer.AcceptSequence(delivery, sequence)) return;
            _statistics.IncrementPacketsReceived();
            _statistics.AddBytesReceived(length);
            try
            {
                Listener.OnNetworkReceive(peer, reader, 0, delivery);
            }
            catch (Exception ex)
            {
                // Messages have independent boundaries. A stale Unity object or a
                // rejected RPC does not invalidate the Steam connection itself.
                Debug.LogError($"[SteamSockets] RPC failed peer={peer.EndPoint} op={buffer[SteamPacketCodec.HeaderSize]} bytes={length - SteamPacketCodec.HeaderSize} delivery={delivery}: {ex}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SteamSockets] Receive failed peer={peer.EndPoint}: {ex}");
            Disconnect(peer, "Invalid frame or handshake failure");
        }
    }

    internal void Send(SteamPeer peer, byte[] data, int offset, int length, DeliveryMethod delivery)
    {
        if (!peer.Ready || peer.Closed) return;
        SendFrame(peer, data, offset, length, delivery, SteamPacketKind.Data);
    }

    private void SendFrame(SteamPeer peer, byte[] data, int offset, int length, DeliveryMethod delivery, SteamPacketKind kind)
    {
        if (!_running || peer.Closed) return;
        if (data == null || offset < 0 || length < 0 || offset > data.Length - length || length > SteamPacketCodec.MaxMessageSize - SteamPacketCodec.HeaderSize)
        {
            Disconnect(peer, "Message exceeds Steam transport limit");
            return;
        }
        var size = length + SteamPacketCodec.HeaderSize;
        var buffer = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            SteamPacketCodec.WriteHeader(buffer, kind, delivery, peer.NextSequence(delivery));
            Buffer.BlockCopy(data, offset, buffer, SteamPacketCodec.HeaderSize, length);
            var result = _api.Send(peer.Handle, buffer, size, SteamPacketCodec.SendFlags(delivery));
            if (result == EResult.k_EResultOK)
            {
                _statistics.IncrementPacketsSent();
                _statistics.AddBytesSent(size);
                return;
            }
            peer.SendFailures++;
            if (peer.SendFailures == 1 || peer.SendFailures % 100 == 0)
                CoopLogSystem.WriteNetworkDiagnostic($"[SteamSockets] Send failed peer={peer.EndPoint} result={result} delivery={delivery} bytes={size}");
            // Unreliable data may be dropped under congestion. A rejected reliable
            // message must not silently leave a partially synchronized session alive.
            if (SteamPacketCodec.IsReliable(delivery) || (result != EResult.k_EResultLimitExceeded && result != EResult.k_EResultIgnored))
                Disconnect(peer, $"Steam send failed: {result}");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    internal void Disconnect(SteamPeer peer, string reason)
    {
        if (peer.Closed) return;
        peer.Closed = true;
        _closeRequests[peer] = reason;
    }

    private void DrainDisconnects()
    {
        if (_closeRequests.Count == 0) return;
        var requests = _closeRequests.ToArray();
        _closeRequests.Clear();
        foreach (var request in requests)
        {
            var peer = request.Key;
            _api.Close(peer.Handle, request.Value);
            _peers.Remove(peer.Handle);
            _connected.Remove(peer);
            CoopLogSystem.WriteNetworkDiagnostic($"[SteamSockets] Disconnected peer={peer.EndPoint}: {request.Value}");
            Listener.OnPeerDisconnected(peer, new DisconnectInfo { Reason = DisconnectReason.DisconnectPeerCalled });
        }
    }

    public void LogConnections()
    {
        foreach (var peer in _peers.Values)
            if (_api.GetStatus(peer.Handle, out var state))
                CoopLogSystem.WriteNetworkDiagnostic($"[SteamSockets] peer={peer.EndPoint} ready={peer.Ready} ping={state.m_nPing}ms quality={state.m_flConnectionQualityLocal:F2}/{state.m_flConnectionQualityRemote:F2} pendingReliable={state.m_cbPendingReliable}B pendingUnreliable={state.m_cbPendingUnreliable}B unacked={state.m_cbSentUnackedReliable}B queue={state.m_usecQueueTime.m_SteamNetworkingMicroseconds}us sendFailures={peer.SendFailures}");
    }

    public override void Stop()
    {
        _running = false;
        foreach (var peer in _peers.Values)
        {
            peer.Closed = true;
            _api.Close(peer.Handle, "Network stopped");
        }
        _peers.Clear();
        _connected.Clear();
        _closeRequests.Clear();
        while (_changes.TryDequeue(out _)) { }
        _api.Dispose();
        _listen = 0;
    }
}

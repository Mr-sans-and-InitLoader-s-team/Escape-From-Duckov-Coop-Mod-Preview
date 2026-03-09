using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public readonly struct ModMessageContext
{
    public ModMessageContext(IModNetworkService service, NetPeer sender, string channel, byte[] payload)
    {
        Service = service;
        Sender = sender;
        Channel = channel;
        Payload = payload ?? Array.Empty<byte>();
    }

    public IModNetworkService Service { get; }

    public NetPeer Sender { get; }

    public string Channel { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public bool IsServer => Service != null && Service.IsServer;

    public bool IsHostMessage => IsServer && Sender == null;
}

public static class ModNetworkApi
{
    private const int MaxPendingMessages = 512;
    private const int BaseMessagesPerFrame = 12;
    private const int BurstMessagesPerFrame = 32;
    private const float DropLogInterval = 1.0f;
    private const float ReplayRequestCooldown = 0.75f;

    private static readonly Dictionary<string, List<Action<ModMessageContext>>> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, float> _nextReplayRequestTime = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<ModMessageContext> _pendingMessages = new();
    private static readonly Dictionary<NetPeer, Dictionary<string, byte[]>> _lastSentToPeer = new();
    private static readonly Dictionary<string, byte[]> _lastSentToServer = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, byte[]> _lastBroadcastFromServer = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    [ThreadStatic]
    private static int _dispatchDepth;

    private static float _nextDropLogTime;
    private static int _droppedSinceLastLog;
    private static ModNetworkPump _pump;
    private static IModNetworkBackend _backend;

    public static event Action<NetPeer> PeerConnected;
    public static event Action<NetPeer> PeerDisconnected;

    public static void SetBackend(IModNetworkBackend backend)
    {
        _backend = backend;
    }

    public static IDisposable RegisterHandler(string channel, Action<ModMessageContext> handler)
    {
        if (string.IsNullOrWhiteSpace(channel))
            throw new ArgumentException("Channel must be provided", nameof(channel));
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        lock (_lock)
        {
            if (!_handlers.TryGetValue(channel, out var list))
            {
                list = new List<Action<ModMessageContext>>();
                _handlers[channel] = list;
            }

            list.Add(handler);
        }

        return new HandlerSubscription(channel, handler);
    }

    public static bool SendToServer(string channel, Action<NetDataWriter> payloadBuilder)
    {
        var backend = _backend;
        if (backend == null || !backend.NetworkStarted || backend.IsServer)
            return false;

        var payload = BuildPayload(payloadBuilder);
        if (payload == null) return false;

        backend.SendToServer(channel, payload);
        CacheOutboundToServer(channel, payload);
        return true;
    }

    public static bool SendToClient(NetPeer target, string channel, Action<NetDataWriter> payloadBuilder, bool echoToServer = true)
    {
        var backend = _backend;
        if (backend == null || target == null || !backend.IsServer || !backend.NetworkStarted)
            return false;

        var payload = BuildPayload(payloadBuilder);
        if (payload == null) return false;

        backend.SendToPeer(target, channel, payload);
        CacheOutboundToPeer(target, channel, payload);

        if (echoToServer && ShouldEchoToServer())
        {
            Dispatch(new ModMessageContext(backend.Service, null, channel, payload));
        }

        return true;
    }

    public static int Broadcast(string channel, Action<NetDataWriter> payloadBuilder, bool includeServer = true)
    {
        var backend = _backend;
        if (backend == null || !backend.NetworkStarted)
            return 0;

        var payload = BuildPayload(payloadBuilder);
        if (payload == null) return 0;

        backend.Broadcast(channel, payload);
        if (backend.IsServer)
        {
            CacheBroadcast(channel, payload);
        }

        if (includeServer && backend.IsServer && ShouldEchoToServer())
        {
            Dispatch(new ModMessageContext(backend.Service, null, channel, payload));
        }

        return backend.ConnectedPeersCount;
    }

    public static void Receive(IModNetworkService service, NetPeer sender, string channel, byte[] payload)
    {
        Enqueue(new ModMessageContext(service, sender, channel, payload));
    }

    public static void NotifyPeerConnected(NetPeer peer)
    {
        PeerConnected?.Invoke(peer);
    }

    public static void NotifyPeerDisconnected(NetPeer peer)
    {
        PeerDisconnected?.Invoke(peer);
    }

    private static byte[] BuildPayload(Action<NetDataWriter> builder)
    {
        var writer = RpcWriterPool.Rent();
        try
        {
            builder?.Invoke(writer);
            return writer.CopyData();
        }
        finally
        {
            RpcWriterPool.Return(writer);
        }
    }

    private static void Enqueue(in ModMessageContext context)
    {
        bool dropped = false;
        var now = Time.unscaledTime;

        lock (_lock)
        {
            if (_pendingMessages.Count >= MaxPendingMessages)
            {
                _pendingMessages.Dequeue();
                _droppedSinceLastLog++;
                dropped = true;

                if (context.Sender != null && CanRequestReplay(now, context.Channel))
                {
                    RequestReplay(context.Sender, context.Channel);
                }
            }

            _pendingMessages.Enqueue(context);

            if (_pump == null)
            {
                _pump = ModNetworkPump.Create(ProcessPendingMessages);
            }
        }

        if (dropped && now >= _nextDropLogTime)
        {
            Debug.LogWarning($"[ModNetworkApi] Dropped {_droppedSinceLastLog} mod messages due to backlog (channel={context.Channel})");
            _droppedSinceLastLog = 0;
            _nextDropLogTime = now + DropLogInterval;
        }
    }

    private static void Dispatch(in ModMessageContext context)
    {
        List<Action<ModMessageContext>> targets = null;

        lock (_lock)
        {
            if (_handlers.TryGetValue(context.Channel, out var list))
            {
                targets = new List<Action<ModMessageContext>>(list);
            }
        }

        if (targets == null || targets.Count == 0) return;

        _dispatchDepth++;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                try
                {
                    targets[i]?.Invoke(context);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ModNetworkApi] Handler for '{context.Channel}' threw: {ex}");
                }
            }
        }
        finally
        {
            _dispatchDepth--;
        }
    }

    private static bool ShouldEchoToServer()
    {
        return _dispatchDepth == 0;
    }

    private static void ProcessPendingMessages(float deltaTime)
    {
        int allowance;
        lock (_lock)
        {
            var backlog = _pendingMessages.Count;
            if (backlog == 0) return;

            allowance = backlog > MaxPendingMessages / 2 ? BurstMessagesPerFrame : BaseMessagesPerFrame;
        }

        var processed = 0;
        while (processed < allowance && TryDequeue(out var ctx))
        {
            Dispatch(ctx);
            processed++;
        }
    }

    private static bool TryDequeue(out ModMessageContext context)
    {
        lock (_lock)
        {
            if (_pendingMessages.Count > 0)
            {
                context = _pendingMessages.Dequeue();
                return true;
            }
        }

        context = default;
        return false;
    }

    private static void RequestReplay(NetPeer sender, string channel)
    {
        var backend = _backend;
        if (backend == null || sender == null || !backend.NetworkStarted)
            return;

        backend.SendReplayRequest(sender, channel);
    }

    private static bool CanRequestReplay(float now, string channel)
    {
        if (!_nextReplayRequestTime.TryGetValue(channel ?? string.Empty, out var next))
        {
            _nextReplayRequestTime[channel ?? string.Empty] = now + ReplayRequestCooldown;
            return true;
        }

        if (now < next) return false;

        _nextReplayRequestTime[channel ?? string.Empty] = now + ReplayRequestCooldown;
        return true;
    }

    private static void CacheOutboundToServer(string channel, byte[] payload)
    {
        lock (_lock)
        {
            _lastSentToServer[channel ?? string.Empty] = payload ?? Array.Empty<byte>();
        }
    }

    private static void CacheOutboundToPeer(NetPeer peer, string channel, byte[] payload)
    {
        lock (_lock)
        {
            if (!_lastSentToPeer.TryGetValue(peer, out var perPeer))
            {
                perPeer = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                _lastSentToPeer[peer] = perPeer;
            }

            perPeer[channel ?? string.Empty] = payload ?? Array.Empty<byte>();
        }
    }

    private static void CacheBroadcast(string channel, byte[] payload)
    {
        lock (_lock)
        {
            _lastBroadcastFromServer[channel ?? string.Empty] = payload ?? Array.Empty<byte>();
        }
    }

    public static void HandleReplayRequest(NetPeer sender, string channel)
    {
        var backend = _backend;
        if (backend == null || sender == null)
            return;

        if (TryGetCachedPayloadForPeer(sender, channel, out var payload))
        {
            backend.SendReplayResponse(sender, channel, payload);
        }
    }

    private static bool TryGetCachedPayloadForPeer(NetPeer peer, string channel, out byte[] payload)
    {
        lock (_lock)
        {
            if (peer != null && _lastSentToPeer.TryGetValue(peer, out var perPeer) && perPeer.TryGetValue(channel ?? string.Empty, out payload))
            {
                return true;
            }

            var backend = _backend;
            if (backend != null && backend.IsServer)
            {
                if (_lastBroadcastFromServer.TryGetValue(channel ?? string.Empty, out payload))
                {
                    return true;
                }
            }
            else if (_lastSentToServer.TryGetValue(channel ?? string.Empty, out payload))
            {
                return true;
            }
        }

        payload = null;
        return false;
    }

    private sealed class HandlerSubscription : IDisposable
    {
        private readonly string _channel;
        private readonly Action<ModMessageContext> _handler;
        private bool _disposed;

        public HandlerSubscription(string channel, Action<ModMessageContext> handler)
        {
            _channel = channel;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_lock)
            {
                if (_handlers.TryGetValue(_channel, out var list))
                {
                    list.Remove(_handler);
                    if (list.Count == 0)
                    {
                        _handlers.Remove(_channel);
                    }
                }
            }

            _disposed = true;
        }
    }
}

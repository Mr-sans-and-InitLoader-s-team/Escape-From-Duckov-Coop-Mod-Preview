global using LiteNetLib;
global using LiteNetLib.Utils;
global using UnityEngine;
using System.Net;
using System.Net.Sockets;
using EscapeFromDuckovCoopMod;
using Steamworks;

namespace EscapeFromDuckovCoopMod
{
    internal static class CoopLogSystem
    {
        internal static void WriteNetworkDiagnostic(string message) { }
    }
}

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object value) { }
        public static void LogWarning(object value) { }
        public static void LogError(object value) => Console.Error.WriteLine(value);
    }
}

internal sealed class TestListener : ICoopNetworkListener
{
    public readonly List<CoopPeer> Connected = new();
    public readonly List<CoopPeer> Disconnected = new();
    public readonly List<byte[]> Received = new();
    public bool ThrowOnReceive;
    public int CallbackThread;
    public void OnPeerConnected(CoopPeer peer) { CallbackThread = Environment.CurrentManagedThreadId; Connected.Add(peer); }
    public void OnPeerDisconnected(CoopPeer peer, DisconnectInfo info) => Disconnected.Add(peer);
    public void OnNetworkReceive(CoopPeer peer, CoopPacketReader reader, byte channel, DeliveryMethod delivery)
    {
        if (ThrowOnReceive) throw new InvalidOperationException("intentional test handler failure");
        Received.Add(reader.GetRemainingBytes());
        reader.Recycle();
    }
    public void OnNetworkReceiveUnconnected(IPEndPoint endpoint, CoopPacketReader reader, UnconnectedMessageType type) { }
    public void OnNetworkError(IPEndPoint endpoint, SocketError error) => throw new Exception($"Socket error: {error}");
    public void OnNetworkLatencyUpdate(CoopPeer peer, int latency) { }
    public void OnConnectionRequest(ConnectionRequest request) => request.AcceptIfKey("test");
}

internal sealed class FakeSteamApi : ISteamSocketsApi
{
    public Action<SteamConnectionChange> Callback;
    public readonly Queue<(uint Handle, byte[] Data)> Incoming = new();
    public readonly List<(uint Handle, byte[] Data, int Flags)> Sent = new();
    public readonly List<uint> Closed = new();
    public EResult SendResult = EResult.k_EResultOK;
    public bool AttachResult = true;
    public bool Disposed;
    public int Released;
    public uint NextHandle = 1;
    public bool Initialize(Action<SteamConnectionChange> callback) { Callback = callback; return true; }
    public uint Listen() => 99;
    public uint Connect(ulong steamId) => NextHandle++;
    public EResult Accept(uint connection) => EResult.k_EResultOK;
    public bool Attach(uint connection) => AttachResult;
    public void Close(uint connection, string reason) => Closed.Add(connection);
    public EResult Send(uint connection, byte[] data, int length, int flags)
    {
        Sent.Add((connection, data.Take(length).ToArray(), flags));
        return SendResult;
    }
    public bool Receive(Action<uint, byte[], int> dispatch)
    {
        if (!Incoming.TryDequeue(out var item)) return false;
        try { dispatch(item.Handle, item.Data, item.Data.Length); }
        finally { Released++; }
        return true;
    }
    public bool GetStatus(uint connection, out SteamNetConnectionRealTimeStatus_t status) { status = default; return true; }
    public void Dispose() => Disposed = true;
    public void Change(uint handle, ESteamNetworkingConnectionState state, ulong steamId = 123, uint listen = 0) =>
        Callback(new SteamConnectionChange(handle, listen, steamId, state, "test"));
}
